using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using BMachine.SDK;

namespace BMachine.UI.ViewModels;

public partial class SpreadsheetViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private SheetsService? _sheetsService;
    private string? _sheetId;
    private string? _sheetName;
    private string? _range;
    private int _rangeStartRow = 1;
    private int _rangeStartCol = 0; // 0-based

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private ObservableCollection<SpreadsheetColumnViewModel> _columns = new();
    [ObservableProperty] private ObservableCollection<SpreadsheetRowViewModel> _rows = new();
    [ObservableProperty] private ObservableCollection<SpreadsheetRowViewModel> _filteredRows = new();

    public SpreadsheetViewModel(IDatabase database)
    {
        _database = database;
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterRows();
    }

    [ObservableProperty] private DateTime? _selectedDateFilter;

    partial void OnSelectedDateFilterChanged(DateTime? value)
    {
        FilterRows();
    }

    private void FilterRows()
    {
        IEnumerable<SpreadsheetRowViewModel> filtered = Rows;

        // 1. Text Search
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var lowerSearch = SearchText.ToLowerInvariant();
            filtered = filtered.Where(r => r.Cells.Any(c => c.Value.ToLowerInvariant().Contains(lowerSearch)));
        }

        // 2. Date Filter (Targeting "ORDER TIME" column)
        if (SelectedDateFilter.HasValue)
        {
            var dateStr = SelectedDateFilter.Value.ToString("yyyy-MM-dd");
            
            // Find column with "ORDER TIME" in header (case-insensitive)
            // If not found, ignore filter or maybe warn? For now ignore.
            var timeCol = Columns.FirstOrDefault(c => c.Header.ToUpperInvariant().Contains("ORDER TIME"));
            
            if (timeCol != null)
            {
                filtered = filtered.Where(r => 
                {
                    if (timeCol.Index < r.Cells.Count)
                    {
                         var val = r.Cells[timeCol.Index].Value;
                         // Check if starts with YYYY-MM-DD
                         return val.StartsWith(dateStr);
                    }
                    return false;
                });
            }
        }

        FilteredRows = new ObservableCollection<SpreadsheetRowViewModel>(filtered.ToList());
        
        if (Rows.Count > 0)
            StatusText = $"{FilteredRows.Count} found (of {Rows.Count})";
        else 
            StatusText = _isLoading ? "Loading..." : "No data.";
    }

    [RelayCommand]
    private async Task LoadData()
    {
        IsLoading = true;
        StatusText = "Loading data...";
        Rows.Clear();
        Columns.Clear();
        HasChanges = false;

        try
        {
            // Load Config
            _sheetId = await _database.GetAsync<string>("Google.SheetId");
            // Use separate config for Spreadsheet sheet name, fallback to "ALL DATA REGULER"
            _sheetName = await _database.GetAsync<string>("Spreadsheet.SheetName"); 
            if (string.IsNullOrEmpty(_sheetName)) _sheetName = "ALL DATA REGULER";

            _range = await _database.GetAsync<string>("Spreadsheet.Range");
            if (string.IsNullOrEmpty(_range)) _range = "A1:Z";
            ParseRangeStart(_range);

            var credsPath = await _database.GetAsync<string>("Google.CredsPath");

            if (string.IsNullOrEmpty(_sheetId) || string.IsNullOrEmpty(credsPath))
            {
                StatusText = "Error: Missing Google Credentials or Sheet ID in Settings.";
                IsLoading = false;
                return;
            }

            // Init Service
            GoogleCredential credential;
            using (var stream = new FileStream(credsPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
            }

            _sheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "BMachine",
            });

            // Fetch Data
            var fullRange = $"{_sheetName}!{_range}";
            var request = _sheetsService.Spreadsheets.Values.Get(_sheetId, fullRange);
            var response = await request.ExecuteAsync();
            var values = response.Values;

            if (values != null && values.Count > 0)
            {
                // Process Headers (Row 0)
                var headers = values[0];
                var columnViewModels = new List<SpreadsheetColumnViewModel>();

                for (int i = 0; i < headers.Count; i++)
                {
                    var headerName = headers[i]?.ToString() ?? $"Column {i + 1}";
                    var colVM = new SpreadsheetColumnViewModel { Header = headerName, Index = i, IsVisible = true };
                    
                    colVM.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SpreadsheetColumnViewModel.IsVisible))
                        {
                            SaveColumnSettings();
                        }
                    };
                    
                    columnViewModels.Add(colVM);
                }

                // FETCH METADATA (Validation & Formats)
                // We check the first data row (header + 1) to guess column types
                // Range A2:Z2 (assuming header is row 1)
                try 
                {
                    // Calculate range for first data row (e.g. A2:Z2)
                    // If _range is "A1:Z", then Row 2 is typically the start of data.
                    // We'll just ask for the specific Range of the first data row relative to the sheet.
                    // Actually, simpler: Request the same sheet, but use 'ranges' param and 'fields'.
                    
                    // We need sheet GridProperties to know GridId if we were doing batchUpdate, 
                    // but for Get we just need range A2:XX2.
                    // Let's guess the range is row 2.
                    // Quote sheet name if it contains spaces
                    var quotedSheetName = _sheetName.Contains(" ") ? $"'{_sheetName}'" : _sheetName;
                    var metadataRange = $"{quotedSheetName}!A2:{GetColumnName(columnViewModels.Count - 1)}2";
                    
                    var metadataRequest = _sheetsService.Spreadsheets.Get(_sheetId);
                    metadataRequest.Ranges = new List<string> { metadataRange };
                    // Request effectiveFormat as well
                    metadataRequest.Fields = "sheets(data(rowData(values(dataValidation,userEnteredFormat,effectiveFormat))))";
                    
                    var metadataResponse = await metadataRequest.ExecuteAsync();
                    var sheetData = metadataResponse.Sheets.FirstOrDefault()?.Data?.FirstOrDefault();
                    var rowData = sheetData?.RowData?.FirstOrDefault();

                    // HEURISTICS & METADATA PROCESSING
                    for (int i = 0; i < columnViewModels.Count; i++)
                    {
                        var colVM = columnViewModels[i];
                        CellData? cellData = (rowData?.Values != null && i < rowData.Values.Count) ? rowData.Values[i] : null;

                        // 1. DATE DETECTION
                        // Heuristic A: Header contains "TGL", "DATE", "TIME"
                        var headerUpper = colVM.Header.ToUpperInvariant();
                        if (headerUpper.Contains("TGL") || headerUpper.Contains("DATE") || headerUpper.Contains("TIME") || headerUpper.Contains("DEADLINE"))
                        {
                            colVM.IsDate = true;
                            System.Diagnostics.Debug.WriteLine($"Column {colVM.Header} detected as DATE (Header Keyword).");
                        }
                        // Heuristic B: Metadata Format
                        else if (cellData != null)
                        {
                            var fmt = cellData.EffectiveFormat?.NumberFormat?.Type ?? cellData.UserEnteredFormat?.NumberFormat?.Type;
                            var pattern = cellData.EffectiveFormat?.NumberFormat?.Pattern ?? cellData.UserEnteredFormat?.NumberFormat?.Pattern ?? "";
                            
                            if (fmt == "DATE" || fmt == "DATE_TIME" || fmt == "TIME" || 
                                pattern.Contains("dd") || pattern.Contains("yyyy") || pattern.Contains("MM"))
                            {
                                colVM.IsDate = true;
                                System.Diagnostics.Debug.WriteLine($"Column {colVM.Header} detected as DATE (Format: {fmt}/{pattern}).");
                            }
                        }

                        // 2. DROPDOWN DETECTION
                        bool isDropdown = false;
                        List<string> items = new();

                        if (cellData?.DataValidation != null)
                        {
                            var type = cellData.DataValidation.Condition.Type;
                            if (type == "ONE_OF_LIST")
                            {
                                isDropdown = true;
                                var val = cellData.DataValidation.Condition.Values;
                                if (val != null) items = val.Select(v => v.UserEnteredValue).ToList();
                            }
                            else if (type == "ONE_OF_RANGE")
                            {
                                isDropdown = true;
                                // For Range, values are not in metadata. We must scan the column data.
                                // We'll do this below.
                            }
                        }
                        
                        // Heuristic C: Header contains "EDITOR" or "STATUS" or "HKS" -> Likely Dropdown?
                        // User mentioned "EDITOR", "HKS ADMIN", "SELEKSI". These are likely dropdowns.
                        if (headerUpper.Contains("EDITOR") || headerUpper.Contains("STATUS") || 
                            headerUpper.Contains("SELEKSI") || headerUpper.Contains("HKS") || headerUpper.Contains("NR"))
                        {
                             isDropdown = true;
                        }

                        if (isDropdown)
                        {
                            colVM.IsDropdown = true;
                            
                            // If items are empty (ONE_OF_RANGE or Heuristic), scan the Loaded Data (values)
                            if (items.Count == 0 && values.Count > 1) 
                            {
                                // Skip header (row 0)
                                // Scan column index 'i'
                                var uniqueValues = new HashSet<string>();
                                for (int r = 1; r < values.Count; r++)
                                {
                                    if (i < values[r].Count)
                                    {
                                        var val = values[r][i]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(val)) uniqueValues.Add(val);
                                    }
                                }
                                items = uniqueValues.OrderBy(x => x).ToList();
                            }
                            colVM.DropdownItems = items;
                            System.Diagnostics.Debug.WriteLine($"Column {colVM.Header} detected as DROPDOWN with {items.Count} items.");
                        }
                    }
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"Metadata Fetch Error: {ex.Message}");
                     StatusText = $"Metadata/Heuristic Error: {ex.Message}";
                }

                // Update Columns Collection (Preserve instance for View subscription)
                Columns.Clear();
                foreach (var col in columnViewModels)
                {
                    Columns.Add(col);
                }

                // Load saved column visibility
                await LoadColumnSettings();

                // Process Rows (Row 1+)
                for (int i = 1; i < values.Count; i++)
                {
                    var rowData = values[i];
                    var rowVM = new SpreadsheetRowViewModel(i); // 0-based index relative to data rows
                    rowVM.OriginalRowIndex = i; // 0-based index in the values list (matches Sheet row index - 1 if range starts at A1)

                    for (int j = 0; j < Columns.Count; j++)
                    {
                        var cellValue = j < rowData.Count ? rowData[j]?.ToString() ?? "" : "";
                        var cellVM = new SpreadsheetCellViewModel 
                        { 
                            Value = cellValue, 
                            OriginalValue = cellValue,
                            ColumnIndex = j 
                        };
                        
                        // Hook up change tracking
                        cellVM.PropertyChanged += (s, e) => 
                        {
                            if (e.PropertyName == nameof(SpreadsheetCellViewModel.Value))
                            {
                                CheckHasChanges();
                            }
                        };
                        
                        rowVM.Cells.Add(cellVM);
                    }
                    Rows.Add(rowVM);
                }

                FilterRows();
                StatusText = $"{Rows.Count} rows loaded from '{_sheetName}'";
            }
            else
            {
                StatusText = "No data found.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Spreadsheet Error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CheckHasChanges()
    {
        HasChanges = Rows.Any(r => r.Cells.Any(c => c.IsChanged));
        if (HasChanges) StatusText = "Unsaved changes...";
    }

    [RelayCommand]
    private async Task SaveChanges()
    {
        if (_sheetsService == null || string.IsNullOrEmpty(_sheetId) || string.IsNullOrEmpty(_sheetName)) return;

        IsLoading = true;
        StatusText = "Saving changes...";

        try
        {
            var updates = new List<ValueRange>();

            // Find changed cells
            foreach (var row in Rows)
            {
                foreach (var cell in row.Cells.Where(c => c.IsChanged))
                {
                    // Calculate A1 notation using parsed range start
                    // OriginalRowIndex = i (1-based in values array)
                    // Actual sheet row = OriginalRowIndex + _rangeStartRow
                    // (values[0] = sheet row _rangeStartRow, values[i] = sheet row _rangeStartRow + i)
                    
                    var sheetRowIndex = row.OriginalRowIndex + _rangeStartRow;
                    var colLetter = GetColumnName(cell.ColumnIndex + _rangeStartCol);
                    var range = $"{_sheetName}!{colLetter}{sheetRowIndex}";

                    var valueRange = new ValueRange
                    {
                        Range = range,
                        Values = new List<IList<object>> { new List<object> { cell.Value } }
                    };
                    updates.Add(valueRange);
                }
            }

            if (updates.Count > 0)
            {
                var batchUpdateRequest = new BatchUpdateValuesRequest
                {
                    ValueInputOption = "USER_ENTERED",
                    Data = updates
                };

                var request = _sheetsService.Spreadsheets.Values.BatchUpdate(batchUpdateRequest, _sheetId);
                await request.ExecuteAsync();

                // Reset change tracking
                foreach (var row in Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        cell.OriginalValue = cell.Value;
                    }
                }
                
                HasChanges = false;
                StatusText = $"Saved {updates.Count} changes successfully.";
            }
            else
            {
                StatusText = "No changes to save.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Save Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleColumn(SpreadsheetColumnViewModel column)
    {
        // View binds directly to IsVisible, but this command might be used for other toggles.
        // If View uses CheckBox Binding TwoWay, this command might not be needed for simple toggle,
        // but we keep it. However, the CheckBox changes IsVisible directly.
        // We will hook into PropertyChanged in LoadData.
        column.IsVisible = !column.IsVisible;
    }
    
    [ObservableProperty] private double _zoomLevel = 1.0;

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = Math.Min(ZoomLevel + 0.1, 3.0);

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = Math.Max(ZoomLevel - 0.1, 0.5);

    [RelayCommand]
    private void ClearDateFilter()
    {
        SelectedDateFilter = null;
    }

    [RelayCommand]
    private void ShowAllColumns()
    {
        foreach (var col in Columns) col.IsVisible = true;
        SaveColumnSettings(); // Trigger save
    }

    private async Task LoadColumnSettings()
    {
        var str = await _database.GetAsync<string>("Spreadsheet.HiddenColumns");
        if (!string.IsNullOrEmpty(str))
        {
            var hidden = new HashSet<string>(str.Split(','));
            foreach (var col in Columns)
            {
                if (hidden.Contains(col.Header)) 
                {
                    col.IsVisible = false; // This will trigger PropertyChanged -> Save, which is redundant but harmless.
                    // To avoid redundancy, we could temporarily detach listener or just let it overwrite.
                }
            }
        }
    }

    private void SaveColumnSettings()
    {
        var hiddenHeaders = Columns.Where(c => !c.IsVisible).Select(c => c.Header);
        var str = string.Join(",", hiddenHeaders);
        _ = _database.SetAsync("Spreadsheet.HiddenColumns", str);
    }

    // Helper to convert 1 -> A, 2 -> B, 27 -> AA, etc. (Index is 0-based)
    private string GetColumnName(int index)
    {
        index++; // Convert to 1-based
        string columnName = "";
        while (index > 0)
        {
            int modulo = (index - 1) % 26;
            columnName = Convert.ToChar(65 + modulo) + columnName;
            index = (index - modulo) / 26;
        }
        return columnName;
    }

    /// <summary>
    /// Parse range like "A3:AU" to extract start row (3) and start column (A=0).
    /// </summary>
    private void ParseRangeStart(string range)
    {
        var startPart = range.Split(':')[0]; // e.g., "A3" from "A3:AU"
        var colPart = "";
        var rowPart = "";
        foreach (var c in startPart)
        {
            if (char.IsLetter(c)) colPart += c;
            else if (char.IsDigit(c)) rowPart += c;
        }
        
        // Column letters to 0-based index (A=0, B=1, AA=26)
        _rangeStartCol = 0;
        foreach (var c in colPart.ToUpper())
        {
            _rangeStartCol = _rangeStartCol * 26 + (c - 'A');
        }
        
        // Row number
        _rangeStartRow = int.TryParse(rowPart, out var row) ? row : 1;
        
        System.Diagnostics.Debug.WriteLine($"Range '{range}' parsed: startRow={_rangeStartRow}, startCol={_rangeStartCol} ({colPart})");
    }
}

public partial class SpreadsheetColumnViewModel : ObservableObject
{
    public string Header { get; set; } = "";
    public int Index { get; set; }
    [ObservableProperty] private bool _isVisible = true;
    
    // Metadata
    public bool IsDropdown { get; set; }
    public List<string> DropdownItems { get; set; } = new();
    public bool IsDate { get; set; }
}

public partial class SpreadsheetRowViewModel : ObservableObject
{
    public int OriginalRowIndex { get; set; }
    public ObservableCollection<SpreadsheetCellViewModel> Cells { get; set; } = new();

    public SpreadsheetRowViewModel(int originalIndex)
    {
        OriginalRowIndex = originalIndex;
    }
}

public partial class SpreadsheetCellViewModel : ObservableObject
{
    [ObservableProperty] private string _value = "";
    public string OriginalValue { get; set; } = "";
    public int ColumnIndex { get; set; }

    public bool IsChanged => Value != OriginalValue;
}

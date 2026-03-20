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
    private int? _sheetGid; // Numeric sheet ID for dimension updates

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isOnline = true;
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
        
        // Load saved zoom level
        var savedZoomStr = await _database.GetAsync<string>("Configs.Spreadsheet.ZoomLevel");
        if (double.TryParse(savedZoomStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double savedZoom))
        {
             // Clamp to reasonable range (0.5 to 3.0) to prevent overflow
             if (savedZoom < 0.5) savedZoom = 0.5;
             if (savedZoom > 3.0) savedZoom = 3.0; // User asked for max 100? Maybe they meant max normal. 300% is fine, but lets safe guard.
             ZoomLevel = savedZoom;
        }

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

            // Step 1: Fetch raw values (FAST - no timeout)
            var quotedSheetName = _sheetName.Contains(" ") ? $"'{_sheetName}'" : _sheetName;
            var fullRange = $"{quotedSheetName}!{_range}";
            
            var valuesRequest = _sheetsService.Spreadsheets.Values.Get(_sheetId, fullRange);
            var valuesResponse = await valuesRequest.ExecuteAsync();
            var values = valuesResponse.Values;

            if (values != null && values.Count > 0)
            {
                // Process Headers (Row 0 of fetched values = the header row in the sheet)
                var headers = values[0];
                var columnViewModels = new List<SpreadsheetColumnViewModel>();

                for (int i = 0; i < headers.Count; i++)
                {
                    var headerName = headers[i]?.ToString() ?? $"Column {i + 1}";
                    var colVM = new SpreadsheetColumnViewModel { Header = headerName, Index = i, IsVisible = true };
                    
                    colVM.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SpreadsheetColumnViewModel.IsVisible)) SaveColumnSettings();
                    };
                    
                    columnViewModels.Add(colVM);
                }

                // Step 2: Fetch metadata (LIGHTWEIGHT - only header + first data row for type detection)
                try
                {
                    // Build a small range: just 2 rows from the start of the range for type/validation detection
                    var startCol = _range.Split(':')[0].TrimEnd('0','1','2','3','4','5','6','7','8','9');
                    var endCol = GetColumnName(columnViewModels.Count - 1 + _rangeStartCol);
                    var metaRange = $"{quotedSheetName}!{startCol}{_rangeStartRow}:{endCol}{_rangeStartRow + 1}";
                    
                    var metadataRequest = _sheetsService.Spreadsheets.Get(_sheetId);
                    metadataRequest.Ranges = new List<string> { metaRange };
                    metadataRequest.Fields = "sheets(properties.sheetId,data(rowData(values(dataValidation,effectiveFormat,userEnteredFormat)),columnMetadata))";
                    
                    var metadataResponse = await metadataRequest.ExecuteAsync();
                    var sheetData = metadataResponse.Sheets?.FirstOrDefault()?.Data?.FirstOrDefault();
                    var metaRowDataList = sheetData?.RowData;
                    var colMetadata = sheetData?.ColumnMetadata;

                    // Apply Column Widths from metadata
                    if (colMetadata != null)
                    {
                        for (int i = 0; i < columnViewModels.Count && i < colMetadata.Count; i++)
                        {
                            if (colMetadata[i].PixelSize.HasValue && colMetadata[i].PixelSize.Value > 0)
                            {
                                var px = colMetadata[i].PixelSize.Value;
                                columnViewModels[i].Width = new Avalonia.Controls.DataGridLength(
                                    px, Avalonia.Controls.DataGridLengthUnitType.Pixel);
                                columnViewModels[i].OriginalWidthPixels = px;
                            }
                        }
                    }

                    // Fetch sheet GID for dimension update requests
                    try
                    {
                        var sheetInfo = metadataResponse.Sheets?.FirstOrDefault();
                        if (sheetInfo?.Properties?.SheetId != null)
                            _sheetGid = sheetInfo.Properties.SheetId;
                    }
                    catch { /* non-critical */ }

                    // Use first DATA row (meta row index 1, skipping header) for type detection
                    var firstDataRowMeta = metaRowDataList != null && metaRowDataList.Count > 1 ? metaRowDataList[1] : null;

                    for (int i = 0; i < columnViewModels.Count; i++)
                    {
                        var colVM = columnViewModels[i];
                        CellData? cellData = (firstDataRowMeta?.Values != null && i < firstDataRowMeta.Values.Count) ? firstDataRowMeta.Values[i] : null;

                        // DATE DETECTION
                        var headerUpper = colVM.Header.ToUpperInvariant();
                        if (headerUpper.Contains("TGL") || headerUpper.Contains("DATE") || headerUpper.Contains("TIME") || headerUpper.Contains("DEADLINE"))
                        {
                            colVM.IsDate = true;
                        }
                        else if (cellData != null)
                        {
                            var fmt = cellData.EffectiveFormat?.NumberFormat?.Type ?? cellData.UserEnteredFormat?.NumberFormat?.Type;
                            var pattern = cellData.EffectiveFormat?.NumberFormat?.Pattern ?? cellData.UserEnteredFormat?.NumberFormat?.Pattern ?? "";
                            if (fmt == "DATE" || fmt == "DATE_TIME" || fmt == "TIME" || pattern.Contains("dd") || pattern.Contains("yyyy") || pattern.Contains("MM"))
                                colVM.IsDate = true;
                        }

                        // DROPDOWN DETECTION
                        bool isDropdown = false;
                        List<string> items = new();

                        if (cellData?.DataValidation != null)
                        {
                            var type = cellData.DataValidation.Condition?.Type;
                            if (type == "ONE_OF_LIST" && cellData.DataValidation.Condition?.Values != null)
                            {
                                isDropdown = true;
                                items = cellData.DataValidation.Condition.Values.Select(v => v.UserEnteredValue).ToList();
                            }
                            else if (type == "ONE_OF_RANGE")
                            {
                                isDropdown = true;
                            }
                        }

                        if (headerUpper.Contains("EDITOR") || headerUpper.Contains("STATUS") || headerUpper.Contains("SELEKSI") || headerUpper.Contains("HKS") || headerUpper.Contains("NR"))
                            isDropdown = true;

                        if (isDropdown)
                        {
                            colVM.IsDropdown = true;
                            if (items.Count == 0 && values.Count > 1)
                            {
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
                        }
                    }
                }
                catch (Exception metaEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Metadata Fetch Error (non-fatal): {metaEx.Message}");
                }

                // Build column & row collections
                Columns.Clear();
                foreach (var col in columnViewModels) Columns.Add(col);
                await LoadColumnSettings();

                // Load saved column widths (override metadata widths if user has custom ones)
                var savedWidthsJson = await _database.GetAsync<string>("Configs.Spreadsheet.ColumnWidths");
                if (!string.IsNullOrEmpty(savedWidthsJson))
                {
                    try
                    {
                        var widths = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(savedWidthsJson);
                        if (widths != null)
                        {
                            foreach (var col in Columns)
                            {
                                if (widths.TryGetValue(col.Header, out var width))
                                    col.Width = new Avalonia.Controls.DataGridLength(width, Avalonia.Controls.DataGridLengthUnitType.Pixel);
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                // Process Data Rows (simple & fast - no per-cell format mirroring for performance)
                for (int i = 1; i < values.Count; i++)
                {
                    var rawRow = values[i];
                    var rowVM = new SpreadsheetRowViewModel(i) { OriginalRowIndex = i };

                    for (int j = 0; j < Columns.Count; j++)
                    {
                        var cellValue = j < rawRow.Count ? rawRow[j]?.ToString() ?? "" : "";
                        var cellVM = new SpreadsheetCellViewModel
                        {
                            Value = cellValue,
                            OriginalValue = cellValue,
                            ColumnIndex = j
                        };

                        cellVM.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(SpreadsheetCellViewModel.Value))
                                CheckHasChanges();
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
                    var quotedSheetName = _sheetName.Contains(" ") ? $"'{_sheetName}'" : _sheetName;
                    var range = $"{quotedSheetName}!{colLetter}{sheetRowIndex}";

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

            // Save column widths to Google Sheets (regardless of cell changes)
            await SaveColumnWidthsToSheetAsync();

            // Also save column widths locally
            await SaveColumnWidthsAsync();
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
    
    partial void OnZoomLevelChanged(double value)
    {
        _ = SaveZoomLevelAsync(value);
    }

    private async Task SaveZoomLevelAsync(double value)
    {
        if (_database != null)
        {
             await _database.SetAsync("Configs.Spreadsheet.ZoomLevel", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = Math.Min(ZoomLevel + 0.1, 3.0);

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = Math.Max(ZoomLevel - 0.1, 0.5);

    [RelayCommand]
    private void ClearDateFilter()
    {
        SelectedDateFilter = null;
    }
    
    [ObservableProperty] private SpreadsheetCellViewModel? _selectedCell;
    
    [RelayCommand]
    private async Task ClearSelectedDate()
    {
        if (SelectedCell == null) return;
        
        // Check if current column is a date column or header contains "DATE"/"TGL"
        var colIndex = SelectedCell.ColumnIndex;
        if (colIndex < 0 || colIndex >= Columns.Count) return;
        
        var colVM = Columns[colIndex];
        var header = colVM.Header.ToUpperInvariant();
        
        if (colVM.IsDate || header.Contains("TGL") || header.Contains("DATE") || header.Contains("TIME") || header.Contains("DEADLINE"))
        {
             // Clear local value
             SelectedCell.Value = "";
             
             // We need to update Google Sheets immediately? 
             // Ideally we just mark it as changed and let SaveChanges handle it.
             // But user might expect immediate action like the Trello integration usually implies.
             // However, our architecture uses SaveChanges. Let's stick to SaveChanges flow for consistency?
             // Users usually expect "Delete" key to just clear the cell content, then they click save.
             // BUT, if this is a helper command, maybe we just clear and let the existing change tracking handle it.
             
             // Wait, `Value` setter triggers `IsChanged`. So SaveChanges will pick it up.
             // We don't need to do direct API call here unless requested.
             // Let's just clear the value.
             StatusText = $"Cleared date in {colVM.Header}. Don't forget to Save.";
        }
    }

    public async Task SaveColumnWidthsAsync()
    {
        if (_database == null) return;
        var widths = new Dictionary<string, double>();
        foreach (var col in Columns)
        {
            if (col.Width.IsAbsolute)
            {
                widths[col.Header] = col.Width.Value;
            }
        }
        var json = System.Text.Json.JsonSerializer.Serialize(widths);
        await _database.SetAsync("Configs.Spreadsheet.ColumnWidths", json);
    }

    private async Task SaveColumnWidthsToSheetAsync()
    {
        if (_sheetsService == null || string.IsNullOrEmpty(_sheetId) || !_sheetGid.HasValue) return;

        var requests = new List<Request>();
        for (int i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];
            if (!col.Width.IsAbsolute) continue;

            var currentPx = col.Width.Value;
            // Only send update if width actually changed from original
            if (Math.Abs(currentPx - col.OriginalWidthPixels) < 1) continue;

            requests.Add(new Request
            {
                UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                {
                    Range = new DimensionRange
                    {
                        SheetId = _sheetGid.Value,
                        Dimension = "COLUMNS",
                        StartIndex = i + _rangeStartCol,
                        EndIndex = i + _rangeStartCol + 1
                    },
                    Properties = new DimensionProperties { PixelSize = (int)currentPx },
                    Fields = "pixelSize"
                }
            });

            // Update original so subsequent saves don't re-send
            col.OriginalWidthPixels = currentPx;
        }

        if (requests.Count > 0)
        {
            try
            {
                var batchReq = new BatchUpdateSpreadsheetRequest { Requests = requests };
                await _sheetsService.Spreadsheets.BatchUpdate(batchReq, _sheetId).ExecuteAsync();
                StatusText += $" | {requests.Count} column widths synced.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Column Width Sync Error: {ex.Message}");
            }
        }
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
    
    // Track original width for change detection
    public double OriginalWidthPixels { get; set; } = 100;
    
    // Width property for binding
    private Avalonia.Controls.DataGridLength _width = new(1, Avalonia.Controls.DataGridLengthUnitType.Star);
    public Avalonia.Controls.DataGridLength Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }
}

public partial class SpreadsheetRowViewModel : ObservableObject
{
    public int OriginalRowIndex { get; set; }
    
    [ObservableProperty] private double _height = 36; // Default modern height

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

    // Visual Formatting Properties
    [ObservableProperty] private Avalonia.Media.IBrush? _background;
    [ObservableProperty] private Avalonia.Media.IBrush? _foreground;
    [ObservableProperty] private Avalonia.Media.FontWeight _fontWeight = Avalonia.Media.FontWeight.Normal;
    [ObservableProperty] private Avalonia.Media.FontStyle _fontStyle = Avalonia.Media.FontStyle.Normal;
}

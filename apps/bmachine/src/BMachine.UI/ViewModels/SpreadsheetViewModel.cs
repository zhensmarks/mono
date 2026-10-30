using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
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
    private System.Threading.Timer? _widthSaveTimer;
    private bool _isLoadingWidths; // prevent save during load
    private bool _isLoadingSettings; // prevent hidden column save during load
    private readonly SpreadsheetLoadGeneration _loadSequence = new();
    private CultureInfo _sheetCulture = CultureInfo.CurrentCulture;

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
    [ObservableProperty] private bool _isTodayFilterActive;
    [ObservableProperty] private bool _hasManualDateFilter;
    [ObservableProperty] private string _filterDateDisplay = "";

    partial void OnSelectedDateFilterChanged(DateTime? value)
    {
        IsTodayFilterActive = value.HasValue && value.Value.Date == DateTime.Today;
        HasManualDateFilter = value.HasValue && value.Value.Date != DateTime.Today;
        FilterDateDisplay = value.HasValue 
            ? (value.Value.Date == DateTime.Today ? "Today" : value.Value.ToString("dd MMM yyyy")) 
            : "";
        FilterRows();
    }

    private void FilterRows()
    {
        IEnumerable<SpreadsheetRowViewModel> filtered = Rows;

        // 1. Smart Text Search
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var parts = SearchText.Split(new[] { " AND ", " and " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lowerSearch = p.ToLowerInvariant().Trim();
                int colonIndex = lowerSearch.IndexOf(':');
                
                if (colonIndex > 0)
                {
                    string colName = lowerSearch.Substring(0, colonIndex).Trim();
                    string searchValue = lowerSearch.Substring(colonIndex + 1).Trim();
                    
                    var targetCols = Columns.Where(c => c.Header.ToLowerInvariant().Contains(colName)).ToList();
                    if (targetCols.Any())
                    {
                        filtered = filtered.Where(r => 
                            targetCols.Any(col => 
                                col.Index < r.Cells.Count && 
                                r.Cells[col.Index].Value.ToLowerInvariant().Contains(searchValue)
                            )
                        );
                    }
                    else
                    {
                        // Fallback
                        filtered = filtered.Where(r => r.Cells.Any(c => c.Value.ToLowerInvariant().Contains(lowerSearch)));
                    }
                }
                else
                {
                    filtered = filtered.Where(r => r.Cells.Any(c => c.Value.ToLowerInvariant().Contains(lowerSearch)));
                }
            }
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
            StatusText = IsLoading ? "Loading..." : "No data.";
    }

    [RelayCommand]
    private async Task LoadData()
    {
        if (HasChanges)
        {
            StatusText = "Save or discard changes before refreshing.";
            return;
        }

        var loadGeneration = _loadSequence.Start();

        // Flush any unsaved column widths and visibility settings before reloading
        _widthSaveTimer?.Dispose();
        _widthSaveTimer = null;
        if (Columns.Count > 0 && !_isLoadingWidths)
        {
            try
            {
                await SaveColumnWidthsAsync();
                SaveColumnSettings();
                if (!IsCurrentLoad(loadGeneration)) return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flush settings failed: {ex.GetType().Name}");
            }
        }

        if (!IsCurrentLoad(loadGeneration)) return;
        IsLoading = true;
        _isLoadingWidths = true;
        _isLoadingSettings = true;
        StatusText = "Loading data...";
        
        try
        {
        // Load saved zoom level
        var savedZoomStr = await _database.GetAsync<string>("Configs.Spreadsheet.ZoomLevel");
        if (!IsCurrentLoad(loadGeneration)) return;
        if (double.TryParse(savedZoomStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double savedZoom))
        {
             // Clamp to reasonable range (0.5 to 3.0) to prevent overflow
             if (savedZoom < 0.5) savedZoom = 0.5;
             if (savedZoom > 3.0) savedZoom = 3.0; // User asked for max 100? Maybe they meant max normal. 300% is fine, but lets safe guard.
             ZoomLevel = savedZoom;
        }

            // Load Config
            _sheetId = await _database.GetAsync<string>("Google.SheetId");
            if (!IsCurrentLoad(loadGeneration)) return;
            var credsPath = await _database.GetAsync<string>("Google.CredsPath");
            if (!IsCurrentLoad(loadGeneration)) return;
            _sheetName = await _database.GetAsync<string>("Spreadsheet.SheetName");
            if (!IsCurrentLoad(loadGeneration)) return;
            _range = await _database.GetAsync<string>("Spreadsheet.Range");
            if (!IsCurrentLoad(loadGeneration)) return;

            if (string.IsNullOrEmpty(_sheetId) ||
                string.IsNullOrEmpty(credsPath) ||
                string.IsNullOrEmpty(_sheetName) ||
                string.IsNullOrEmpty(_range))
            {
                StatusText = "Error: Google integration is not configured in Settings.";
                return;
            }

            ParseRangeStart(_range);

            // Init Service
            GoogleCredential credential;
            using (var stream = new FileStream(credsPath, FileMode.Open, FileAccess.Read))
            {
                credential = CredentialFactory.FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential()
                    .CreateScoped(SheetsService.Scope.Spreadsheets);
            }

            _sheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "BMachine",
            });

            // Step 1: Fetch raw values (FAST - no timeout)
            var quotedSheetName = SpreadsheetValueMapper.QuoteSheetName(_sheetName);
            var fullRange = $"{quotedSheetName}!{_range}";
            
            var valuesRequest = _sheetsService.Spreadsheets.Values.Get(_sheetId, fullRange);
            var valuesResponse = await valuesRequest.ExecuteAsync();
            if (!IsCurrentLoad(loadGeneration)) return;
            var values = valuesResponse.Values;

            // Selaraskan jangkar baris/kolom dengan range yang BENAR-BENAR dikembalikan Google.
            // Tanpa ini, bila respons mulai di baris/kolom lain dari teks konfigurasi,
            // seluruh penomoran bergeser tanpa suara dan nilai tersimpan ke sel yang salah.
            var loadAnchor = SpreadsheetLoadAlignment.Resolve(
                valuesResponse.Range, _rangeStartRow, _rangeStartCol);
            if (loadAnchor.StartRow != _rangeStartRow || loadAnchor.StartColumn != _rangeStartCol)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Load anchor adjusted: row {_rangeStartRow}->{loadAnchor.StartRow}, " +
                    $"col {_rangeStartCol}->{loadAnchor.StartColumn} (response '{valuesResponse.Range}')");
                _rangeStartRow = loadAnchor.StartRow;
                _rangeStartCol = loadAnchor.StartColumn;
            }

            var rawValuesRequest = _sheetsService.Spreadsheets.Values.Get(_sheetId, fullRange);
            rawValuesRequest.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
            rawValuesRequest.DateTimeRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.SERIALNUMBER;
            var rawValuesResponse = await rawValuesRequest.ExecuteAsync();
            if (!IsCurrentLoad(loadGeneration)) return;
            var rawValues = rawValuesResponse.Values;

            // Dua request terpisah: kalau panjangnya beda (sheet berubah di antaranya),
            // indeks rawValues bisa menunjuk baris yang berbeda. Lebih aman tidak dipakai.
            if (rawValues != null && values != null && rawValues.Count != values.Count)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"rawValues dropped: {rawValues.Count} rows vs {values.Count} displayed rows.");
                rawValues = null;
            }

            if (values != null && values.Count > 0)
            {
                // Process Headers (Row 0 of fetched values = the header row in the sheet)
                var headers = values[0];
                var columnViewModels = new List<SpreadsheetColumnViewModel>();

                for (int i = 0; i < headers.Count; i++)
                {
                    var headerName = headers[i]?.ToString() ?? $"Column {i + 1}";
                    var letter = GetColumnName(i + _rangeStartCol);
                    var colVM = new SpreadsheetColumnViewModel 
                    { 
                        Header = headerName, 
                        Index = i, 
                        ColumnLetter = letter,
                        IsVisible = true 
                    };
                    
                    colVM.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SpreadsheetColumnViewModel.IsVisible) && !_isLoadingSettings && !IsLoading) SaveColumnSettings();
                        if (e.PropertyName == nameof(SpreadsheetColumnViewModel.Width) && !_isLoadingWidths && !IsLoading) DebounceSaveColumnWidths();
                    };
                    
                    columnViewModels.Add(colVM);
                }

                // Step 2: Fetch metadata (LIGHTWEIGHT - only header + first data row for type detection)
                try
                {
                    // Build a small range: just 2 rows from the start of the range for type/validation detection
                    // Pakai kolom jangkar hasil load (bukan potong string dari teks config,
                    // yang rusak untuk range ber-$ atau kolom multi-huruf).
                    var startCol = GetColumnName(_rangeStartCol);
                    var endCol = GetColumnName(columnViewModels.Count - 1 + _rangeStartCol);
                    var metaRange = $"{quotedSheetName}!{startCol}{_rangeStartRow}:{endCol}{_rangeStartRow + 1}";
                    
                    var metadataRequest = _sheetsService.Spreadsheets.Get(_sheetId);
                    metadataRequest.Ranges = new List<string> { metaRange };
                    metadataRequest.Fields = "properties(locale),sheets(properties.sheetId,data(rowData(values(dataValidation,effectiveFormat,userEnteredFormat)),columnMetadata))";
                    
                    var metadataResponse = await metadataRequest.ExecuteAsync();
                    if (!IsCurrentLoad(loadGeneration)) return;
                    var locale = metadataResponse.Properties?.Locale;
                    if (!string.IsNullOrWhiteSpace(locale))
                    {
                        try { _sheetCulture = CultureInfo.GetCultureInfo(locale.Replace('_', '-')); }
                        catch (CultureNotFoundException) { _sheetCulture = CultureInfo.CurrentCulture; }
                    }
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
                            if (!items.Contains(""))
                            {
                                items.Insert(0, "");
                            }
                            colVM.DropdownItems = items;
                        }
                    }
                }
                catch (Exception metaEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Metadata fetch failed (non-fatal): {metaEx.GetType().Name}");
                }

                var rawDataRows = rawValues?.Skip(1).ToList();
                foreach (var col in columnViewModels)
                    col.InputKind = SpreadsheetValueMapper.InferColumnKind(rawDataRows, col.Index, col.IsDate);

                // Load saved column widths (saved widths override Google Sheets metadata widths)
                Dictionary<string, double>? widths = null;
                try
                {
                    widths = await _database.GetAsync<Dictionary<string, double>>("Configs.Spreadsheet.ColumnWidths");
                    if (!IsCurrentLoad(loadGeneration)) return;
                }
                catch { /* ignore */ }

                if (widths == null)
                {
                    try
                    {
                        var rawStr = await _database.GetAsync<string>("Configs.Spreadsheet.ColumnWidths");
                        if (!IsCurrentLoad(loadGeneration)) return;
                        if (!string.IsNullOrEmpty(rawStr))
                        {
                            if (rawStr.StartsWith("\"") && rawStr.EndsWith("\""))
                            {
                                rawStr = System.Text.Json.JsonSerializer.Deserialize<string>(rawStr);
                            }
                            if (!string.IsNullOrEmpty(rawStr))
                            {
                                widths = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(rawStr);
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                if (widths != null)
                {
                    var indexLookup = new Dictionary<int, double>();
                    var headerLookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in widths)
                    {
                        var colonIdx = kvp.Key.IndexOf(':');
                        var idxStr = colonIdx >= 0 ? kvp.Key.Substring(0, colonIdx) : kvp.Key;
                        if (int.TryParse(idxStr, out int idx))
                        {
                            indexLookup[idx] = kvp.Value;
                        }
                        else if (!string.IsNullOrWhiteSpace(kvp.Key))
                        {
                            headerLookup[kvp.Key.Trim()] = kvp.Value;
                        }
                    }

                    foreach (var col in columnViewModels)
                    {
                        // Priority 1: Match by column index (Excel-style: each column position is independent)
                        if (indexLookup.TryGetValue(col.Index, out var widthByIndex) && widthByIndex >= 30)
                        {
                            col.Width = new Avalonia.Controls.DataGridLength(widthByIndex, Avalonia.Controls.DataGridLengthUnitType.Pixel);
                        }
                        // Priority 2: Fallback for older database configs
                        else if (headerLookup.TryGetValue(col.Header?.Trim() ?? "", out var widthByHeader) && widthByHeader >= 30)
                        {
                            col.Width = new Avalonia.Controls.DataGridLength(widthByHeader, Avalonia.Controls.DataGridLengthUnitType.Pixel);
                        }
                    }
                }

                // Load hidden column settings
                var hiddenStr = await _database.GetAsync<string>("Spreadsheet.HiddenColumns");
                if (!IsCurrentLoad(loadGeneration)) return;
                if (!string.IsNullOrEmpty(hiddenStr))
                {
                    var tokens = hiddenStr.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    var hiddenIndices = new HashSet<int>();
                    var hiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var token in tokens)
                    {
                        var colonIdx = token.IndexOf(':');
                        var idxStr = colonIdx >= 0 ? token.Substring(0, colonIdx) : token;
                        if (int.TryParse(idxStr, out int idx))
                        {
                            hiddenIndices.Add(idx);
                        }
                        else
                        {
                            hiddenHeaders.Add(token);
                        }
                    }

                    foreach (var col in columnViewModels)
                    {
                        // If saved by index, hide exact column. If legacy saved by header, hide matching header.
                        if (hiddenIndices.Contains(col.Index) || hiddenHeaders.Contains(col.Header?.Trim() ?? ""))
                        {
                            col.IsVisible = false;
                        }
                    }
                }

                // Materialize a complete snapshot before replacing the currently displayed rows.
                var loadedRows = new List<SpreadsheetRowViewModel>();
                for (int i = 1; i < values.Count; i++)
                {
                    var displayRow = values[i];
                    var rawRow = rawValues != null && i < rawValues.Count ? rawValues[i] : null;
                    var sheetRowNumber = SpreadsheetValueMapper.GetSheetRowNumber(_rangeStartRow, i);
                    var rowVM = new SpreadsheetRowViewModel(i, this, sheetRowNumber);
                    for (int j = 0; j < columnViewModels.Count; j++)
                    {
                        var cellValue = j < displayRow.Count ? displayRow[j]?.ToString() ?? "" : "";
                        object? rawValue = rawRow != null && j < rawRow.Count ? rawRow[j] : null;
                        var cellVM = new SpreadsheetCellViewModel
                        {
                            Value = cellValue,
                            OriginalValue = cellValue,
                            OriginalRawValue = rawValue,
                            InputKind = columnViewModels[j].InputKind,
                            ColumnIndex = j
                        };

                        cellVM.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(SpreadsheetCellViewModel.Value))
                                CheckHasChanges();
                        };

                        rowVM.Cells.Add(cellVM);
                    }
                    loadedRows.Add(rowVM);
                }

                if (!IsCurrentLoad(loadGeneration)) return;
                Columns.Clear();
                foreach (var col in columnViewModels) Columns.Add(col);
                Rows.Clear();
                foreach (var row in loadedRows) Rows.Add(row);
                HasChanges = false;

                FilterRows();
                StatusText = $"{Rows.Count} rows loaded from '{_sheetName}'";
            }
            else
            {
                if (!IsCurrentLoad(loadGeneration)) return;
                Columns.Clear();
                Rows.Clear();
                HasChanges = false;
                FilterRows();
                StatusText = "No data found.";
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(loadGeneration))
                StatusText = $"Google Sheets request failed ({ex.GetType().Name}).";
            System.Diagnostics.Debug.WriteLine($"Spreadsheet request failed: {ex.GetType().Name}");
        }
        finally
        {
            if (IsCurrentLoad(loadGeneration))
            {
                _isLoadingSettings = false;
                _isLoadingWidths = false;
                IsLoading = false;
            }
        }
    }

    private bool IsCurrentLoad(long generation) => _loadSequence.IsCurrent(generation);

    private void CheckHasChanges()
    {
        HasChanges = Rows.Any(r => r.Cells.Any(c => c.IsChanged));
        if (HasChanges) StatusText = "Unsaved changes...";
    }

    /// <summary>
    /// Membaca ulang kolom kunci (kolom pertama range) untuk baris-baris yang diedit dan
    /// membandingkannya dengan nilai saat data dimuat. Mengembalikan pesan bila sheet
    /// sudah bergeser, atau null bila masih sejajar.
    /// </summary>
    private async Task<string?> CheckRowsStillAlignedAsync()
    {
        if (_sheetsService == null || string.IsNullOrEmpty(_sheetId) || string.IsNullOrEmpty(_sheetName))
            return null;

        var changedRows = Rows.Where(row => row.Cells.Any(cell => cell.IsChanged)).ToList();
        if (changedRows.Count == 0) return null;

        var anchors = changedRows
            .Where(row => row.Cells.Count > 0)
            .Select(row => new SpreadsheetRowAnchor(row.SheetRowNumber, row.Cells[0].OriginalValue ?? string.Empty))
            .ToList();
        if (anchors.Count == 0) return null;

        var firstRow = anchors.Min(anchor => anchor.SheetRowNumber);
        var lastRow = anchors.Max(anchor => anchor.SheetRowNumber);
        if (firstRow < 1) return null;

        var columnLetter = SpreadsheetValueMapper.GetColumnName(_rangeStartCol);
        var quotedSheet = SpreadsheetValueMapper.QuoteSheetName(_sheetName);
        var checkRange = $"{quotedSheet}!{columnLetter}{firstRow}:{columnLetter}{lastRow}";

        var request = _sheetsService.Spreadsheets.Values.Get(_sheetId, checkRange);
        request.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
        var response = await request.ExecuteAsync();

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, response.Values, firstRow);
        return mismatches.Count == 0 ? null : SpreadsheetRowAnchorGuard.Describe(mismatches);
    }

    [RelayCommand]
    private async Task SaveChanges()
    {
        if (_sheetsService == null || string.IsNullOrEmpty(_sheetId) || string.IsNullOrEmpty(_sheetName))
        {
            StatusText = "Google Sheets is not ready. Refresh and check the integration settings.";
            return;
        }

        if (IsLoading)
        {
            StatusText = "Wait for the current spreadsheet operation to finish.";
            return;
        }

        var changes = SpreadsheetCellWritePlanner.Create(Rows, _sheetName, _rangeStartCol, _sheetCulture);

        IsLoading = true;
        StatusText = "Checking sheet alignment...";
        var savedCount = 0;
        try
        {
            // Pengaman anti grid-basi: pastikan baris yang akan ditulis masih berisi data
            // yang sama seperti saat dimuat. Kalau sheet sempat berubah (baris disisip /
            // dihapus / di-cut), penulisan akan meleset 1+ baris, jadi batalkan.
            var alignmentProblem = await CheckRowsStillAlignedAsync();
            if (alignmentProblem != null)
            {
                StatusText = alignmentProblem;
                return;
            }

            StatusText = "Saving changes...";
            foreach (var batch in changes.GroupBy(c => c.MappedValue.InputMode))
            {
                var entries = batch.ToList();
                var batchRequest = new BatchUpdateValuesRequest
                {
                    ValueInputOption = batch.Key == SpreadsheetValueInputMode.Raw ? "RAW" : "USER_ENTERED",
                    IncludeValuesInResponse = true,
                    ResponseValueRenderOption = "FORMATTED_VALUE",
                    ResponseDateTimeRenderOption = "SERIAL_NUMBER",
                    Data = entries.Select(entry => new ValueRange
                    {
                        Range = entry.Range,
                        Values = new List<IList<object>> { new List<object> { entry.MappedValue.Value } }
                    }).ToList()
                };

                var response = await _sheetsService.Spreadsheets.Values.BatchUpdate(batchRequest, _sheetId).ExecuteAsync();
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var rangeResponse = response.Responses != null && i < response.Responses.Count
                        ? response.Responses[i]
                        : null;
                    var returnedValue = rangeResponse?.UpdatedData?.Values?.FirstOrDefault()?.FirstOrDefault();
                    var serverDisplayValue = returnedValue?.ToString() ?? entry.EditedValue;

                    entry.Cell.OriginalValue = serverDisplayValue;
                    entry.Cell.OriginalRawValue = entry.MappedValue.InputMode == SpreadsheetValueInputMode.UserEntered
                        ? null
                        : entry.MappedValue.Value;
                    if (string.Equals(entry.Cell.Value, entry.EditedValue, StringComparison.Ordinal))
                        entry.Cell.Value = serverDisplayValue;
                }

                savedCount += entries.Count;
                CheckHasChanges();
            }
            await SaveColumnWidthsToSheetAsync();
        }
        catch (Exception ex)
        {
            CheckHasChanges();
            StatusText = savedCount > 0
                ? $"Saved {savedCount} cells; remaining changes failed ({ex.GetType().Name})."
                : $"Google Sheets save failed ({ex.GetType().Name}).";
            return;
        }
        finally
        {
            IsLoading = false;
        }

        try
        {
            // Save column widths locally after IsLoading is cleared.
            await SaveColumnWidthsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Cell values saved ({savedCount}); column-width save failed ({ex.GetType().Name}).";
            return;
        }

        CheckHasChanges();
        StatusText = HasChanges
            ? $"Saved {savedCount} cells; newer edits are still unsaved."
            : SpreadsheetSaveStatus.GetCompletionMessage(savedCount);
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
    private void FilterToday()
    {
        if (SelectedDateFilter.HasValue && SelectedDateFilter.Value.Date == DateTime.Today)
        {
            SelectedDateFilter = null;
        }
        else
        {
            SelectedDateFilter = DateTime.Today;
        }
    }

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

    public void DebounceSaveColumnWidths()
    {
        if (_isLoadingWidths || IsLoading) return;
        _widthSaveTimer?.Dispose();
        _widthSaveTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await SaveColumnWidthsAsync();
                System.Diagnostics.Debug.WriteLine("Column widths auto-saved.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Column width save failed: {ex.GetType().Name}");
            }
        }, null, 250, System.Threading.Timeout.Infinite);
    }

    public async Task SaveColumnWidthsAsync()
    {
        if (_database == null || _isLoadingWidths || IsLoading || Columns.Count == 0) return;
        var widths = new Dictionary<string, double>();
        var cols = Columns.ToList();
        foreach (var col in cols)
        {
            if (col.Width.IsAbsolute && col.Width.Value >= 30)
            {
                // Key by Column Index so multiple columns with identical headers (e.g. multiple "TGL DONE") are tracked independently like Excel
                widths[col.Index.ToString()] = Math.Round(col.Width.Value, 1);
            }
        }
        if (widths.Count > 0)
        {
            await _database.SetAsync("Configs.Spreadsheet.ColumnWidths", widths);
        }
    }

    private async Task SaveColumnWidthsToSheetAsync()
    {
        if (_sheetsService == null || string.IsNullOrEmpty(_sheetId) || !_sheetGid.HasValue) return;

        var requests = new List<Request>();
        var cols = Columns.ToList();
        for (int i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
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
                    Properties = new DimensionProperties { PixelSize = (int)Math.Round(currentPx) },
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
                System.Diagnostics.Debug.WriteLine($"Google Sheets column-width sync failed: {ex.GetType().Name}");
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
        if (_isLoadingSettings || IsLoading || Columns.Count == 0) return;
        // Save hidden column indices so duplicate headers are independent like Excel
        var hiddenIndices = Columns.Where(c => !c.IsVisible).Select(c => c.Index.ToString());
        var str = string.Join(",", hiddenIndices);
        _ = _database.SetAsync("Spreadsheet.HiddenColumns", str);
    }

    // Convert a zero-based index to a Google Sheets column label (A, Z, AA, ...).
    private string GetColumnName(int index) => SpreadsheetValueMapper.GetColumnName(index);

    /// <summary>
    /// Parse range like "A3:AU" to extract start row (3) and start column (A=0).
    /// </summary>
    private void ParseRangeStart(string range)
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(range);
        _rangeStartRow = start.RowNumber;
        _rangeStartCol = start.ColumnIndex;
        System.Diagnostics.Debug.WriteLine($"Range '{range}' parsed: startRow={_rangeStartRow}, startCol={_rangeStartCol} ({SpreadsheetValueMapper.GetColumnName(_rangeStartCol)})");
    }
}

public partial class SpreadsheetColumnViewModel : ObservableObject
{
    public string Header { get; set; } = "";
    public int Index { get; set; }
    public string ColumnLetter { get; set; } = "";
    public string DisplayTitle => string.IsNullOrEmpty(ColumnLetter) ? Header : $"[{ColumnLetter}] {Header}";
    [ObservableProperty] private bool _isVisible = true;
    
    // Metadata
    public bool IsDropdown { get; set; }
    public List<string> DropdownItems { get; set; } = new();
    public bool IsDate { get; set; }
    internal SpreadsheetCellInputKind InputKind { get; set; } = SpreadsheetCellInputKind.Text;
    
    // Track original width for change detection
    public double OriginalWidthPixels { get; set; } = 100;
    
    // Width property for binding
    private Avalonia.Controls.DataGridLength _width = new(120, Avalonia.Controls.DataGridLengthUnitType.Pixel);
    public Avalonia.Controls.DataGridLength Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }
}

public partial class SpreadsheetRowViewModel : ObservableObject
{
    private readonly SpreadsheetViewModel? _parent;
    public int OriginalRowIndex { get; set; }
    public int SheetRowNumber { get; }

    [ObservableProperty] private double _height = 36; // Default modern height

    public ObservableCollection<SpreadsheetCellViewModel> Cells { get; set; } = new();

    public SpreadsheetRowViewModel(int originalIndex, SpreadsheetViewModel? parent = null, int? sheetRowNumber = null)
    {
        OriginalRowIndex = originalIndex;
        SheetRowNumber = sheetRowNumber ?? originalIndex;
        _parent = parent;
    }

    [RelayCommand]
    private void CopyId()
    {
        if (_parent == null) return;
        var idCol = _parent.Columns.FirstOrDefault(c => c.Header.Contains("ID"));
        if (idCol != null && idCol.Index < Cells.Count)
        {
            var id = Cells[idCol.Index].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                // Basic clipboard copy via Application
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow?.Clipboard?.SetTextAsync(id);
                    _parent.StatusText = $"Copied ID: {id}";
                }
            }
        }
    }

    [RelayCommand]
    private void OpenInTrello()
    {
        if (_parent == null) return;
        var idCol = _parent.Columns.FirstOrDefault(c => c.Header.Contains("ID"));
        if (idCol != null && idCol.Index < Cells.Count)
        {
            var id = Cells[idCol.Index].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                // Open Trello search in default browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                { 
                    FileName = $"https://trello.com/search?q={id}", 
                    UseShellExecute = true 
                });
                _parent.StatusText = $"Opening Trello Search: {id}";
            }
        }
    }
}

public partial class SpreadsheetCellViewModel : ObservableObject
{
    [ObservableProperty] private string _value = "";
    public string OriginalValue { get; set; } = "";
    internal object? OriginalRawValue { get; set; }
    internal SpreadsheetCellInputKind InputKind { get; set; } = SpreadsheetCellInputKind.Text;
    public int ColumnIndex { get; set; }

    public bool IsChanged => Value != OriginalValue;

    // Visual Formatting Properties
    [ObservableProperty] private Avalonia.Media.IBrush? _background;
    [ObservableProperty] private Avalonia.Media.IBrush? _foreground;
    [ObservableProperty] private Avalonia.Media.FontWeight _fontWeight = Avalonia.Media.FontWeight.Normal;
    [ObservableProperty] private Avalonia.Media.FontStyle _fontStyle = Avalonia.Media.FontStyle.Normal;
}

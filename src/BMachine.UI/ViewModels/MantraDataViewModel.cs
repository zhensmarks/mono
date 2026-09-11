using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using BMachine.SDK;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class MantraDataViewModel : ObservableObject
{
    private readonly MantraDataSettings _settings;
    private readonly ExcelParserService _excelService;
    private readonly WordParserService _wordService;
    private readonly PhotoMatcherService _photoService;
    private readonly PhotoshopBridgeService _psBridge;
    private readonly TransformService _transformService;
    private readonly LocalAIService _aiService;
    private readonly IDatabase? _database;

    public MantraDataSettings Settings => _settings;
    public TransformService TransformService => _transformService;
    public LocalAIService AiService => _aiService;

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Siap. Buka file data atau tarik spreadsheet ke sini.";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private int _totalRows = 0;

    [ObservableProperty]
    private int _matchedPhotosCount = 0;

    [ObservableProperty]
    private int _selectedRowCount = 0;

    [ObservableProperty]
    private DataJobKind _jobKind = DataJobKind.YearbookStudent;

    [ObservableProperty]
    private string _formulaText = string.Empty;

    [ObservableProperty]
    private bool _canUndo = false;
    [ObservableProperty]
    private bool _canRedo = false;

    [ObservableProperty]
    private bool _hasData = false;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _isAdvancedSearchOpen = false;

    // Multi-sheet support
    private List<SheetResult> _allSheets = new();
    [ObservableProperty] private ObservableCollection<string> _sheetNames = new();
    [ObservableProperty] private int _currentSheetIndex = -1;

    // Callbacks for View to present dialogs
    public Func<Task<(string? PsdFolder, string? PhotoFolder)>>? RequestProcessPsdDialogFunc { get; set; }
    public Func<string, string, Task>? RequestAlertFunc { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmFunc { get; set; }
    public Func<string, string, string, Task<(bool Confirmed, string Value)>>? RequestInputFunc { get; set; }
    public Func<IEnumerable<string>, string?, Task<(bool Confirmed, string Find, string Replace, string? TargetColumn, bool MatchCase)>>? RequestFindReplaceFunc { get; set; }
    public Func<IEnumerable<string>, string?, Task<(bool Confirmed, string Source, string Separator, string ColA, string ColB, bool DeleteSource)>>? RequestSplitColumnFunc { get; set; }
    public Func<IEnumerable<string>, IEnumerable<string>?, TableDataRow?, Task<(bool Confirmed, List<string> SelectedColumns, string MergeFormat, string Separator, string NewColumnName, bool DeleteSource)>>? RequestCustomMergeFunc { get; set; }
    public Func<List<string>, List<TableDataRow>, Task<TransformDialogResult>>? RequestTransformFunc { get; set; }
    public Func<List<DataCleaningSuggestion>, Task<CleanerDialogResult>>? RequestCleanerFunc { get; set; }

    public ObservableCollection<string> Columns { get; } = new();
    public ObservableCollection<TableDataRow> Rows { get; } = new();
    public ObservableCollection<TableDataRow> FilteredRows { get; } = new();
    public Dictionary<string, string> ColumnFormulas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DataJobKind> JobKinds { get; } =
        Enum.GetValues<DataJobKind>().ToList();

    private readonly Stack<TableSnapshot> _undoStack = new();
    private readonly Stack<TableSnapshot> _redoStack = new();
    private const int MaxUndo = 30;
    private bool _skipUndo;

    public MantraDataViewModel(IDatabase? database = null)
    {
        _database = database;
        _settings = MantraDataSettings.Load();
        _excelService = new ExcelParserService();
        _wordService = new WordParserService();
        _photoService = new PhotoMatcherService();
        _psBridge = new PhotoshopBridgeService(_settings);
        _transformService = new TransformService();
        _aiService = new LocalAIService { IsEnabled = _settings.AiEnabled };

        Rows.CollectionChanged += (s, e) => RefreshFilteredRows();
    }

    public void RefreshFilteredRows()
    {
        FilteredRows.Clear();
        if (string.IsNullOrWhiteSpace(SearchFilter))
        {
            foreach (var r in Rows) FilteredRows.Add(r);
        }
        else
        {
            var term = SearchFilter.Trim().ToLowerInvariant();
            foreach (var row in Rows)
            {
                if (row.RowNumber.ToString().Contains(term))
                {
                    FilteredRows.Add(row);
                    continue;
                }

                bool match = false;
                foreach (var val in row.Values.Values)
                {
                    if (!string.IsNullOrEmpty(val) && val.ToLowerInvariant().Contains(term))
                    {
                        match = true;
                        break;
                    }
                }
                if (match) FilteredRows.Add(row);
            }
        }
    }

    partial void OnSearchFilterChanged(string value)
    {
        RefreshFilteredRows();
    }

    partial void OnCurrentSheetIndexChanged(int value)
    {
        SwitchToSheet(value);
    }

    public void SwitchToSheet(int index)
    {
        if (index < 0 || index >= _allSheets.Count) return;
        var target = _allSheets[index];

        PushUndo();
        ColumnFormulas.Clear();
        Columns.Clear();
        foreach (var c in target.Columns) Columns.Add(c);

        Rows.Clear();
        foreach (var r in target.Rows) Rows.Add(r);

        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        StatusMessage = "Menampilkan sheet: " + target.Name + " (" + TotalRows + " baris)";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public void NextSheet()
    {
        if (SheetNames.Count == 0) return;
        CurrentSheetIndex = (CurrentSheetIndex + 1) % SheetNames.Count;
    }

    [RelayCommand]
    public void PreviousSheet()
    {
        if (SheetNames.Count == 0) return;
        CurrentSheetIndex = (CurrentSheetIndex - 1 + SheetNames.Count) % SheetNames.Count;
    }

    public async Task LoadFileByPathAsync(string path)
    {
        if (!File.Exists(path)) return;

        IsLoading = true;
        StatusMessage = $"Membaca {Path.GetFileName(path)}...";

        try
        {
            await Task.Run(() =>
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                List<string> cols;
                List<TableDataRow> dataRows;

                if (ext == ".docx")
                {
                    (cols, dataRows) = _wordService.LoadWordDocx(path);
                }
                else
                {
                    (cols, dataRows) = _excelService.LoadExcel(path);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    PushUndo();
                    ColumnFormulas.Clear();
                    Columns.Clear();
                    foreach (var c in cols) Columns.Add(c);

                    Rows.Clear();
                    foreach (var r in dataRows) Rows.Add(r);

                    CurrentFilePath = path;
                    TotalRows = Rows.Count; HasData = Rows.Count > 0;
                    if (ext == ".docx")
                    {
                        _allSheets = new List<SheetResult>();
                        SheetNames.Clear();
                        CurrentSheetIndex = -1;
                    }
                    else
                    {
                        _allSheets = new List<SheetResult>(_excelService.GetAllSheets());
                        SheetNames.Clear();
                        foreach (var s in _allSheets) SheetNames.Add(s.Name);
                        CurrentSheetIndex = SheetNames.Count > 0 ? 0 : -1;
                    }
                    StatusMessage = $"Berhasil memuat {TotalRows} baris dari {Path.GetFileName(path)}";
                    RefreshFilteredRows();
                });
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal memuat file: {ex.Message}";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("BDater Error", $"Error saat membuka file:\n{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ExportDaterAsync()
    {
        if (Rows.Count == 0 || string.IsNullOrEmpty(CurrentFilePath))
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Peringatan", "Belum ada data tabel untuk diekspor.");
            return;
        }

        try
        {
            var txtPath = _excelService.ExportToDater(CurrentFilePath, Columns.ToList(), Rows.ToList());
            StatusMessage = $"Data berhasil diekspor ke folder DATER: {Path.GetFileName(txtPath)}";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Export DATER Sukses", $"Export berhasil disimpan ke:\n{txtPath}");
        }
        catch (Exception ex)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Error Export", $"Gagal export ke DATER: {ex.Message}");
        }
    }

    public async Task ExportToFormatAsync(string format, string savePath)
    {
        if (Rows.Count == 0) return;

        try
        {
            switch (format.ToLowerInvariant())
            {
                case "txt":
                    _excelService.ExportToTxt(savePath, Columns.ToList(), Rows.ToList());
                    break;
                case "csv":
                    _excelService.ExportToCsv(savePath, Columns.ToList(), Rows.ToList());
                    break;
                case "xlsx":
                    _excelService.ExportToXlsx(savePath, Columns.ToList(), Rows.ToList());
                    break;
            }
            StatusMessage = $"Export berhasil: {Path.GetFileName(savePath)}";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Export Sukses", $"Data berhasil diekspor ke:\n{savePath}");
        }
        catch (Exception ex)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Error Export", $"Gagal melakukan export:\n{ex.Message}");
        }
    }

    public void ApplyTitleCase(IEnumerable<TableDataRow> selectedRows, string? targetColumn = null)
    {
        var list = selectedRows.ToList();
        if (list.Count == 0) return;

        PushUndo();
        foreach (var row in list)
        {
            if (!string.IsNullOrEmpty(targetColumn))
            {
                row[targetColumn] = ExcelParserService.ToTitleCase(row[targetColumn]);
            }
            else
            {
                foreach (var col in Columns)
                {
                    row[col] = ExcelParserService.ToTitleCase(row[col]);
                }
            }
        }
        StatusMessage = $"Mengubah {list.Count} baris ke Title Case.";
    }

    public void TransformCells(IEnumerable<(TableDataRow Row, string Column)> cells, Func<string, string, string> transform, string doneMessage)
    {
        var list = cells.Where(c => !string.IsNullOrEmpty(c.Column) && Columns.Contains(c.Column)).ToList();
        if (list.Count == 0) return;

        PushUndo();
        foreach (var (row, col) in list)
            row[col] = transform(col, row[col] ?? string.Empty);

        StatusMessage = doneMessage;
        RefreshFilteredRows();
    }

    public void SmartCleanCells(IEnumerable<(TableDataRow Row, string Column)> cells)
    {
        var list = cells.ToList();
        TransformCells(list, (col, val) => YearbookLayoutService.SmartClean(col, val),
            $"Rapikan otomatis: {list.Count} sel disesuaikan untuk buku tahunan / ID card.");
    }

    public void CleanupSchoolData()
    {
        if (Rows.Count == 0)
        {
            StatusMessage = "Belum ada data untuk dirapikan.";
            return;
        }

        PushUndo();
        _skipUndo = true;
        try
        {
            if (Columns.Count > 0)
                ApplyJobLayout();

            foreach (var row in Rows)
            {
                foreach (var col in Columns.ToList())
                    row[col] = YearbookLayoutService.SmartClean(col, row[col]);
            }

            var ttl = FindCanonical("TTL");
            var tempat = FindCanonical("TEMPAT LAHIR");
            bool yearbook = JobKind is DataJobKind.YearbookStudent or DataJobKind.YearbookTeacher;
            if (yearbook && ttl != null)
            {
                int withTtl = Rows.Count(r => !string.IsNullOrWhiteSpace(r[ttl]));
                int missingPlace = Rows.Count(r =>
                    !string.IsNullOrWhiteSpace(r[ttl]) &&
                    (tempat == null || string.IsNullOrWhiteSpace(r[tempat])));
                if (withTtl > 0 && missingPlace >= Math.Max(1, withTtl / 4))
                    SplitTtlColumn(ttl);
            }

            if (yearbook && FindCanonical("TEMPAT LAHIR") != null && FindCanonical("TGL LAHIR") != null)
                BuildTtlFromParts();

            var no = FindCanonical("NO");
            if (no != null && Rows.All(r => string.IsNullOrWhiteSpace(r[no])))
                FillNumberSeries(no, Rows.ToList());
        }
        finally
        {
            _skipUndo = false;
        }

        StatusMessage = $"Seluruh data dirapikan untuk {DataJobKindInfo.Label(JobKind)}.";
        RefreshFilteredRows();
    }

    private string? FindCanonical(string canonical) =>
        Columns.FirstOrDefault(c => YearbookLayoutService.CanonicalName(c) == canonical);

    public void ApplyDateFormat(IEnumerable<TableDataRow> selectedRows, string targetColumn, string formatType = "Full")
    {
        var list = selectedRows.ToList();
        if (list.Count == 0 || string.IsNullOrEmpty(targetColumn)) return;

        PushUndo();
        foreach (var row in list)
        {
            row[targetColumn] = ExcelParserService.FormatIndonesianDate(row[targetColumn], formatType);
        }
        StatusMessage = $"Format tanggal ({formatType}) diterapkan pada kolom {targetColumn}.";
    }

    public void ApplyPhonePrefix(IEnumerable<TableDataRow> selectedRows, string targetColumn)
    {
        var list = selectedRows.ToList();
        if (list.Count == 0 || string.IsNullOrEmpty(targetColumn)) return;

        PushUndo();
        foreach (var row in list)
            row[targetColumn] = YearbookLayoutService.CleanPhone(row[targetColumn]);
        StatusMessage = $"Nomor HP dirapikan di kolom {targetColumn}.";
    }

    public void CustomMergeColumns(IReadOnlyList<string> cols, string mergeFormat, string separator, string newColName, bool deleteSource = true)
    {
        if (cols == null || cols.Count < 2 || string.IsNullOrWhiteSpace(newColName)) return;

        PushUndo();

        int firstIdx = cols.Select(c => Columns.IndexOf(c)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        bool isTemplate = mergeFormat != "separator" && mergeFormat.Contains("{A}");

        foreach (var row in Rows)
        {
            var values = cols.Select(c => row[c]?.Trim() ?? string.Empty).ToList();

            string merged;
            if (isTemplate && cols.Count == 2)
            {
                var a = values[0];
                var b = values[1];
                if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
                    merged = string.Empty;
                else if (string.IsNullOrEmpty(a))
                    merged = b;
                else if (string.IsNullOrEmpty(b))
                    merged = a;
                else
                    merged = mergeFormat.Replace("{A}", a).Replace("{B}", b);
            }
            else
            {
                var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
                merged = string.Join(separator, nonEmpty);
            }

            row[newColName] = merged;
        }

        if (deleteSource)
        {
            foreach (var col in cols)
            {
                if (col != newColName)
                {
                    DeleteColumnInternal(col);
                }
            }
        }

        if (!Columns.Contains(newColName))
        {
            if (firstIdx >= 0 && firstIdx <= Columns.Count)
                Columns.Insert(firstIdx, newColName);
            else
                Columns.Add(newColName);
        }

        var srcLabel = string.Join(" + ", cols);
        StatusMessage = $"Kolom '{newColName}' berhasil digabungkan dari: {srcLabel}.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public async Task OpenTransformDialog()
    {
        if (Rows.Count == 0)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Info", "Tidak ada data untuk ditransformasi. Buka file terlebih dahulu.");
            return;
        }

        if (RequestTransformFunc == null) return;

        var result = await RequestTransformFunc(Columns.ToList(), Rows.ToList());
        if (!result.Confirmed) return;

        PushUndo();

        try
        {
            if (result.PresetCode == "custom")
            {
                var (newHeaders, newRows) = _transformService.BuildCustomMerge(
                    Columns.ToList(),
                    Rows.ToList(),
                    result.MergeColumns,
                    result.TargetHeader,
                    result.Separator,
                    result.KeepSources);
                ReplaceTable(newHeaders, newRows);
                StatusMessage = $"Gabungan kustom diterapkan ke kolom '{result.TargetHeader}'. {newRows.Count} baris hasil.";
            }
            else if (!string.IsNullOrEmpty(result.PresetCode))
            {
                var (newHeaders, newRows) = _transformService.ApplyPreset(
                    result.PresetCode,
                    Columns.ToList(),
                    Rows.ToList());
                ReplaceTable(newHeaders, newRows);
                StatusMessage = $"Transformasi '{result.PresetCode}' diterapkan. {TotalRows} baris hasil.";
            }
        }
        catch (Exception ex)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Error", $"Error transformasi: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task OpenCleanerDialog()
    {
        if (Rows.Count == 0)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Info", "Tidak ada data untuk dibersihkan. Buka file terlebih dahulu.");
            return;
        }

        if (RequestCleanerFunc == null) return;

        var suggestions = DataCleanerService.AnalyzeSheet(Columns.ToList(), Rows.ToList());
        var merged = new List<DataCleaningSuggestion>(suggestions);

        if (Settings.AiEnabled &&
            !string.IsNullOrWhiteSpace(Settings.AiEndpoint) &&
            !string.IsNullOrWhiteSpace(Settings.AiModel))
        {
            try
            {
                var sample = Rows.Take(20)
                    .Select(r => Columns.Select(c => r[c] ?? "").ToList())
                    .ToList();
                var ai = await AiService.AnalyzeCleanupSuggestionsAsync(
                    Columns.ToList(), sample, Settings.AiEndpoint, Settings.AiModel);
                if (ai.Count > 0)
                {
                    var existing = new HashSet<string>(
                        merged.Select(s => s.Code), StringComparer.OrdinalIgnoreCase);
                    foreach (var s in ai)
                    {
                        if (existing.Add(s.Code))
                            merged.Add(s);
                    }
                }
            }
            catch
            {
                // AI tidak tersedia; lanjut dengan rekomendasi automatis saja.
            }
        }

        var result = await RequestCleanerFunc(merged);
        if (!result.Applied || result.SelectedCodes.Count == 0) return;

        PushUndo();

        try
        {
            DataCleanerService.ApplySuggestions(Columns, Rows, result.SelectedCodes);

            int num = 1;
            foreach (var r in Rows)
                r.RowNumber = num++;

            TotalRows = Rows.Count; HasData = Rows.Count > 0;
            StatusMessage = $"Pembersihan data diterapkan: {result.SelectedCodes.Count} tindakan.";
            RefreshFilteredRows();
        }
        catch (Exception ex)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Error", $"Error pembersihan: {ex.Message}");
        }
    }

    private void ReplaceTable(List<string> newHeaders, List<TableDataRow> newRows)
    {
        ColumnFormulas.Clear();

        Columns.Clear();
        foreach (var h in newHeaders) Columns.Add(h);

        Rows.Clear();
        foreach (var r in newRows) Rows.Add(r);

        TotalRows = Rows.Count;
        HasData = Rows.Count > 0;
        RefreshFilteredRows();
    }

    public void InsertColumn(string newColName, int targetIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(newColName)) return;

        if (Columns.Contains(newColName))
        {
            if (RequestAlertFunc != null)
                _ = RequestAlertFunc("Perhatian", $"Kolom dengan nama '{newColName}' sudah ada.");
            return;
        }

        PushUndo();

        if (targetIndex >= 0 && targetIndex <= Columns.Count)
        {
            Columns.Insert(targetIndex, newColName);
        }
        else
        {
            Columns.Add(newColName);
        }

        foreach (var row in Rows)
        {
            row[newColName] = string.Empty;
        }

        StatusMessage = $"Kolom '{newColName}' berhasil ditambahkan.";
    }

    public void MoveColumn(string colName, int direction)
    {
        if (string.IsNullOrEmpty(colName)) return;
        int curIdx = Columns.IndexOf(colName);
        if (curIdx == -1) return;

        int newIdx = curIdx + direction;
        if (newIdx < 0 || newIdx >= Columns.Count) return;

        PushUndo();
        Columns.Move(curIdx, newIdx);
        StatusMessage = $"Kolom '{colName}' dipindahkan ke posisi {newIdx + 1}.";
    }

    public void ReorderColumns(IEnumerable<string> newOrder)
    {
        var list = newOrder.Where(c => Columns.Contains(c)).ToList();
        if (list.Count != Columns.Count) return;

        bool changed = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (Columns[i] != list[i])
            {
                changed = true;
                break;
            }
        }
        if (!changed) return;

        PushUndo();
        for (int i = 0; i < list.Count; i++)
        {
            int oldIdx = Columns.IndexOf(list[i]);
            if (oldIdx != i)
            {
                Columns.Move(oldIdx, i);
            }
        }
    }

    public void RenameColumn(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName) return;

        PushUndo();
        int idx = Columns.IndexOf(oldName);
        if (idx == -1) return;

        Columns[idx] = newName;

        foreach (var row in Rows)
        {
            var val = row[oldName];
            row.Values.Remove(oldName);
            row[newName] = val;
        }

        StatusMessage = $"Kolom '{oldName}' diubah menjadi '{newName}'.";
    }

    public void DeleteColumn(string colName)
    {
        if (string.IsNullOrWhiteSpace(colName) || !Columns.Contains(colName)) return;

        PushUndo();
        Columns.Remove(colName);
        ColumnFormulas.Remove(colName);
        foreach (var row in Rows)
        {
            row.Values.Remove(colName);
        }

        StatusMessage = $"Kolom '{colName}' berhasil dihapus.";
    }

    public void SetRowHighlight(IEnumerable<TableDataRow> selectedRows, string colorHex)
    {
        var list = selectedRows.ToList();
        foreach (var row in list)
        {
            row.TagColor = colorHex;
        }
        StatusMessage = $"{list.Count} baris diberi tanda warna.";
    }

    public void ClearRowHighlight(IEnumerable<TableDataRow> selectedRows)
    {
        var list = selectedRows.ToList();
        foreach (var row in list)
        {
            row.TagColor = "#00000000";
        }
        StatusMessage = $"Warna sorotan dibersihkan dari {list.Count} baris.";
    }

    public void DeleteRows(IEnumerable<TableDataRow> selectedRows)
    {
        var list = selectedRows.ToList();
        if (list.Count == 0) return;

        PushUndo();
        foreach (var r in list)
        {
            Rows.Remove(r);
        }

        int num = 1;
        foreach (var r in Rows)
        {
            r.RowNumber = num++;
        }

        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        StatusMessage = $"{list.Count} baris dihapus dari tabel.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public async Task ProcessPhotoshopAsync()
    {
        if (Rows.Count == 0)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Peringatan", "Data tabel masih kosong.");
            return;
        }

        string psdDir = string.Empty;
        string photoDir = string.Empty;

        if (RequestProcessPsdDialogFunc != null)
        {
            var res = await RequestProcessPsdDialogFunc();
            if (string.IsNullOrEmpty(res.PsdFolder) || string.IsNullOrEmpty(res.PhotoFolder))
                return;

            psdDir = res.PsdFolder;
            photoDir = res.PhotoFolder;
        }

        if (string.IsNullOrEmpty(psdDir) || string.IsNullOrEmpty(photoDir))
            return;

        _settings.LastPsdFolder = psdDir;
        _settings.LastPhotoFolder = photoDir;
        _settings.Save();

        IsLoading = true;
        StatusMessage = "Menganalisis template PSD dan mencocokkan foto...";

        await Task.Run(() =>
        {
            var photos = _photoService.CollectPhotosRecursive(photoDir);
            var psdFiles = Directory.Exists(psdDir)
                ? Directory.EnumerateFiles(psdDir, "*.psd", SearchOption.AllDirectories).ToList()
                : new List<string>();

            int matchCount = 0;

            foreach (var row in Rows)
            {
                var name = row["NAMA"];
                if (string.IsNullOrEmpty(name)) name = row["Nama"];

                string? matchedPhotoPath = null;
                string matchedFileName = "-";
                int matchScore = 0;

                var matchedPsd = psdFiles.FirstOrDefault(p => _photoService.MatchDataRowToPsd(name, Path.GetFileName(p)) >= 300);
                if (!string.IsNullOrEmpty(matchedPsd))
                {
                    matchedPhotoPath = _photoService.MatchPhotoToPsd(Path.GetFileName(matchedPsd), photos);
                    if (!string.IsNullOrEmpty(matchedPhotoPath))
                    {
                        matchedFileName = Path.GetFileName(matchedPhotoPath);
                        matchScore = 100;
                    }
                    else
                    {
                        matchedFileName = $"[PSD] {Path.GetFileName(matchedPsd)}";
                        matchScore = 100;
                    }
                }

                if (string.IsNullOrEmpty(matchedPhotoPath) && !string.IsNullOrEmpty(name))
                {
                    var match = _photoService.FindBestMatch(name, photos, _settings.PhotoMatchThreshold);
                    if (match.IsPassed)
                    {
                        matchedPhotoPath = match.MatchedFilePath;
                        matchedFileName = match.MatchedFileName;
                        matchScore = match.Score;
                    }
                }

                bool isPassed = !string.IsNullOrEmpty(matchedPhotoPath) || !string.IsNullOrEmpty(matchedPsd);
                row.MatchedPhoto = matchedFileName;
                row.MatchScore = matchScore;
                row.IsPhotoMatched = isPassed;

                if (isPassed)
                {
                    row["_MATCHED_PHOTO_PATH"] = matchedPhotoPath ?? string.Empty;
                    matchCount++;
                }
                else
                {
                    row["_MATCHED_PHOTO_PATH"] = string.Empty;
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                MatchedPhotosCount = matchCount;
            });
        });

        var confirmMsg = $"Siap memproses data ke Photoshop:\n\n" +
                         $"• Jumlah Siswa: {Rows.Count}\n" +
                         $"• Foto Cocok: {MatchedPhotosCount} dari {Rows.Count}\n" +
                         $"• Folder PSD: {Path.GetFileName(psdDir)}\n\n" +
                         $"Lanjutkan proses rendering otomatis ke Photoshop?";

        if (RequestConfirmFunc != null)
        {
            var confirmRes = await RequestConfirmFunc("Konfirmasi Proses Photoshop", confirmMsg);
            if (!confirmRes)
            {
                IsLoading = false;
                StatusMessage = "Proses Photoshop dibatalkan oleh pengguna.";
                return;
            }
        }

        var daterDir = Path.Combine(Path.GetDirectoryName(CurrentFilePath) ?? Environment.CurrentDirectory, "DATER");
        if (!Directory.Exists(daterDir)) Directory.CreateDirectory(daterDir);
        var jsonPath = Path.Combine(daterDir, "yb_process_data_table.json");

        var payload = new
        {
            header = Columns.ToList(),
            rows = Rows.Select(r =>
            {
                var dict = Columns.ToDictionary(c => c, c => r[c]);
                dict["_MATCHED_PHOTO_PATH"] = r["_MATCHED_PHOTO_PATH"];
                return dict;
            }).ToList()
        };
        await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var report = await _psBridge.RunProcessAsync(jsonPath, psdDir, photoDir, progress);

            StatusMessage = $"Proses Photoshop selesai! {report.RowsProcessed} data diproses.";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("BDater Selesai", $"Proses Photoshop selesai dengan sukses!\nTotal Halaman: {report.Pages.Count}\nData Terisi: {report.RowsProcessed}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error proses Photoshop: {ex.Message}";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Error Photoshop", $"Gagal menjalankan proses Photoshop:\n{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void PushUndo()
    {
        if (_skipUndo) return;
        _undoStack.Push(TableSnapshot.Capture(Columns, Rows, ColumnFormulas));
        if (_undoStack.Count > MaxUndo)
        {
            var keep = _undoStack.Take(MaxUndo).Reverse().ToList();
            _undoStack.Clear();
            foreach (var s in keep)
                _undoStack.Push(s);
        }
        _redoStack.Clear();
        CanRedo = false;
        CanUndo = _undoStack.Count > 0;
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var currentSnap = TableSnapshot.Capture(Columns, Rows, ColumnFormulas);
        _redoStack.Push(currentSnap);
        if (_redoStack.Count > MaxUndo)
        {
            var keep = _redoStack.Take(MaxUndo).Reverse().ToList();
            _redoStack.Clear();
            foreach (var s in keep)
                _redoStack.Push(s);
        }
        var snap = _undoStack.Pop();
        snap.Restore(Columns, Rows, ColumnFormulas);
        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        StatusMessage = "Urungkan: langkah terakhir dibatalkan.";
        RefreshFilteredRows();
    }

    public IReadOnlyDictionary<string, string> SnapshotRow(TableDataRow row)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in Columns)
            d[col] = row[col] ?? string.Empty;
        return d;
    }

    public void ApplyFormulaToColumn(string formula, string targetColumn)
    {
        if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(targetColumn)) return;
        if (!Columns.Contains(targetColumn)) return;

        PushUndo();
        var f = formula.Trim();
        if (!f.StartsWith('=')) f = "=" + f;
        ColumnFormulas[targetColumn] = f;

        int ok = 0, err = 0;
        foreach (var row in Rows)
        {
            try
            {
                row[targetColumn] = FormulaEngine.Evaluate(f, SnapshotRow(row), Columns);
                ok++;
            }
            catch (FormulaException)
            {
                row[targetColumn] = "#ERR!";
                err++;
            }
        }

        StatusMessage = err == 0
            ? $"Rumus diterapkan ke kolom {targetColumn} ({ok} baris)."
            : $"Rumus di {targetColumn}: {ok} berhasil, {err} error (#ERR!).";
        RefreshFilteredRows();
    }

    public void ApplyFormulaToRows(string formula, string targetColumn, IEnumerable<TableDataRow> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(targetColumn))
            return;

        PushUndo();
        var f = formula.Trim();
        if (!f.StartsWith('=')) f = "=" + f;

        int ok = 0, err = 0;
        foreach (var row in list)
        {
            try
            {
                row[targetColumn] = FormulaEngine.Evaluate(f, SnapshotRow(row), Columns);
                ok++;
            }
            catch (FormulaException)
            {
                row[targetColumn] = "#ERR!";
                err++;
            }
        }

        StatusMessage = err == 0
            ? $"Rumus dijalankan pada {ok} sel ({targetColumn})."
            : $"Rumus: {ok} berhasil, {err} error.";
        RefreshFilteredRows();
    }

    public void RecalcColumnFormulas()
    {
        if (ColumnFormulas.Count == 0)
        {
            StatusMessage = "Belum ada rumus kolom yang disimpan.";
            return;
        }

        PushUndo();
        int err = 0;
        foreach (var kv in ColumnFormulas.ToList())
        {
            if (!Columns.Contains(kv.Key)) continue;
            foreach (var row in Rows)
            {
                try
                {
                    row[kv.Key] = FormulaEngine.Evaluate(kv.Value, SnapshotRow(row), Columns);
                }
                catch (FormulaException)
                {
                    row[kv.Key] = "#ERR!";
                    err++;
                }
            }
        }
        StatusMessage = err == 0 ? "Semua rumus kolom dihitung ulang." : $"Hitung ulang selesai dengan {err} error.";
        RefreshFilteredRows();
    }

    public void FillDown(IReadOnlyList<(TableDataRow Row, string Column, string Value)> cellsByColumnTop)
    {
        if (cellsByColumnTop.Count == 0) return;
        PushUndo();
        foreach (var cell in cellsByColumnTop)
            cell.Row[cell.Column] = cell.Value;
        StatusMessage = $"Isi ke bawah: {cellsByColumnTop.Count} sel.";
        RefreshFilteredRows();
    }

    public void FillNumberSeries(string column, IList<TableDataRow> rows, int startAt = 1)
    {
        if (string.IsNullOrEmpty(column) || rows.Count == 0) return;
        PushUndo();
        int n = startAt;
        foreach (var row in rows)
        {
            row[column] = n.ToString(CultureInfo.InvariantCulture);
            n++;
        }
        StatusMessage = $"Nomor urut {startAt}-{n - 1} di kolom {column}.";
        RefreshFilteredRows();
    }

    public void SortByColumn(string column, bool ascending)
    {
        if (string.IsNullOrEmpty(column) || !Columns.Contains(column)) return;
        PushUndo();
        var sorted = ascending
            ? Rows.OrderBy(r => r[column], StringComparer.CurrentCultureIgnoreCase).ToList()
            : Rows.OrderByDescending(r => r[column], StringComparer.CurrentCultureIgnoreCase).ToList();

        Rows.Clear();
        int num = 1;
        foreach (var r in sorted)
        {
            r.RowNumber = num++;
            Rows.Add(r);
        }
        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        StatusMessage = ascending
            ? $"Diurutkan A-Z menurut {column}."
            : $"Diurutkan Z-A menurut {column}.";
        RefreshFilteredRows();
    }

    public int FindReplace(string find, string replace, string? column, bool matchCase)
    {
        if (string.IsNullOrEmpty(find)) return 0;
        PushUndo();
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int count = 0;
        var cols = string.IsNullOrEmpty(column) ? Columns.ToList() : new List<string> { column };

        foreach (var row in Rows)
        {
            foreach (var col in cols)
            {
                var val = row[col] ?? string.Empty;
                if (val.IndexOf(find, cmp) < 0) continue;
                row[col] = matchCase
                    ? val.Replace(find, replace)
                    : ReplaceIgnoreCase(val, find, replace);
                count++;
            }
        }

        StatusMessage = $"Cari & ganti: {count} sel diubah.";
        RefreshFilteredRows();
        return count;
    }

    private static string ReplaceIgnoreCase(string input, string find, string replace)
    {
        if (find.Length == 0) return input;
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < input.Length)
        {
            int idx = input.IndexOf(find, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                sb.Append(input[i..]);
                break;
            }
            sb.Append(input, i, idx - i);
            sb.Append(replace);
            i = idx + find.Length;
        }
        return sb.ToString();
    }

    public void SplitColumn(string source, string delimiter, string colA, string colB, bool deleteSource)
    {
        if (string.IsNullOrEmpty(source) || !Columns.Contains(source)) return;
        PushUndo();
        if (!Columns.Contains(colA)) InsertColumnInternal(colA, Columns.IndexOf(source) + 1);
        if (!Columns.Contains(colB)) InsertColumnInternal(colB, Columns.IndexOf(colA) + 1);

        foreach (var row in Rows)
        {
            var parts = YearbookLayoutService.SplitByDelimiter(row[source], delimiter, 2);
            row[colA] = parts[0];
            row[colB] = parts[1];
        }

        if (deleteSource && source != colA && source != colB)
            DeleteColumnInternal(source);

        StatusMessage = $"Kolom {source} dipisah ke {colA} dan {colB}.";
        RefreshFilteredRows();
    }

    public void SplitTtlColumn(string source)
    {
        if (string.IsNullOrEmpty(source) || !Columns.Contains(source)) return;
        PushUndo();

        const string tempat = "TEMPAT LAHIR";
        const string tgl = "TGL LAHIR";
        int idx = Columns.IndexOf(source);
        if (!Columns.Contains(tempat)) InsertColumnInternal(tempat, idx + 1);
        if (!Columns.Contains(tgl)) InsertColumnInternal(tgl, Columns.IndexOf(tempat) + 1);

        foreach (var row in Rows)
        {
            var (a, b) = YearbookLayoutService.SplitTtl(row[source]);
            row[tempat] = a;
            row[tgl] = b;
        }

        StatusMessage = "TTL dipisah otomatis ke TEMPAT LAHIR dan TGL LAHIR.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public void UndoLast() => Undo();

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var currentSnap = TableSnapshot.Capture(Columns, Rows, ColumnFormulas);
        _undoStack.Push(currentSnap);
        CanUndo = _undoStack.Count > 0;

        var snap = _redoStack.Pop();
        snap.Restore(Columns, Rows, ColumnFormulas);
        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        CanRedo = _redoStack.Count > 0;
        StatusMessage = "Ulangi (Redo): perubahan diterapkan kembali.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public void RedoLast() => Redo();

    [RelayCommand]
    public void ClearData()
    {
        if (Rows.Count == 0 && Columns.Count == 0) return;
        PushUndo();
        Rows.Clear();
        Columns.Clear();
        ColumnFormulas.Clear();
        _allSheets.Clear();
        SheetNames.Clear();
        CurrentSheetIndex = -1;
        CurrentFilePath = string.Empty;
        TotalRows = 0;
        HasData = false;
        SearchFilter = string.Empty;
        ReplaceText = string.Empty;
        IsAdvancedSearchOpen = false;
        StatusMessage = "Data telah ditutup. Siap membuka file data baru.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public void ToggleAdvancedSearch()
    {
        IsAdvancedSearchOpen = !IsAdvancedSearchOpen;
    }

    [RelayCommand]
    public async Task QuickReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchFilter))
        {
            if (RequestAlertFunc != null) _ = RequestAlertFunc("Peringatan", "Masukkan teks yang ingin dicari terlebih dahulu.");
            return;
        }
        var pattern = System.Text.RegularExpressions.Regex.Escape(SearchFilter);
        int count = 0;
        var previewValues = new List<string>();
        foreach (var row in Rows)
        {
            foreach (var col in Columns)
            {
                var val = row[col];
                if (!string.IsNullOrEmpty(val) && val.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                    if (previewValues.Count < 5)
                        previewValues.Add(val);
                }
            }
        }

        if (count == 0)
        {
            StatusMessage = "Cari & Ganti: tidak ada nilai yang cocok.";
            if (RequestAlertFunc != null) _ = RequestAlertFunc("Cari & Ganti", $"Tidak ada nilai sel yang cocok dengan '{SearchFilter}'.");
            return;
        }

        string preview = count > 5
            ? $"Ditemukan {count} nilai sel yang cocok dengan '{SearchFilter}'.\n\n5 contoh nilai yang akan diganti:\n" + string.Join("\n", previewValues.Select(v => $"- {v}"))
            : $"Ditemukan {count} nilai sel yang cocok dengan '{SearchFilter}'.\n\nSemua nilai yang akan diganti:\n" + string.Join("\n", previewValues.Select(v => $"- {v}"));

        if (RequestConfirmFunc != null)
        {
            var ok = await RequestConfirmFunc("Konfirmasi Cari & Ganti", $"{preview}\n\nLanjutkan mengganti semua dengan '{ReplaceText ?? string.Empty}'?");
            if (!ok) return;
        }

        PushUndo();
        int applied = 0;
        foreach (var row in Rows)
        {
            foreach (var col in Columns)
            {
                var val = row[col];
                if (!string.IsNullOrEmpty(val) && val.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row[col] = System.Text.RegularExpressions.Regex.Replace(val, pattern, ReplaceText ?? string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    applied++;
                }
            }
        }
        StatusMessage = $"Cari & Ganti selesai: {applied} nilai sel diperbarui.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public async Task ApplyLayoutAsync()
    {
        if (Columns.Count == 0 && Rows.Count == 0)
        {
            await NewTableAsync();
            return;
        }
        ApplyJobLayout();
    }

    [RelayCommand]
    public void RecalcFormulas() => RecalcColumnFormulas();

    [RelayCommand]
    public void CleanupAll() => CleanupSchoolData();

    public void BuildTtlFromParts()
    {
        var tempat = Columns.FirstOrDefault(c => YearbookLayoutService.CanonicalName(c) == "TEMPAT LAHIR") ?? "TEMPAT LAHIR";
        var tgl = Columns.FirstOrDefault(c => YearbookLayoutService.CanonicalName(c) == "TGL LAHIR") ?? "TGL LAHIR";
        if (!Columns.Contains(tempat) || !Columns.Contains(tgl))
        {
            StatusMessage = "Butuh kolom TEMPAT LAHIR dan TGL LAHIR untuk merakit TTL.";
            return;
        }

        PushUndo();
        if (!Columns.Contains("TTL"))
            InsertColumnInternal("TTL", Columns.IndexOf(tgl) + 1);

        foreach (var row in Rows)
        {
            var a = row[tempat]?.Trim() ?? string.Empty;
            var b = row[tgl]?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(a)) row["TTL"] = b;
            else if (string.IsNullOrEmpty(b)) row["TTL"] = a;
            else row["TTL"] = $"{a}, {b}";
        }
        ColumnFormulas["TTL"] = "=TTL([TEMPAT LAHIR],[TGL LAHIR])";
        StatusMessage = "Kolom TTL diisi dari tempat + tanggal lahir.";
        RefreshFilteredRows();
    }

    public void TrimAllCells()
    {
        if (Rows.Count == 0) return;
        PushUndo();
        foreach (var row in Rows)
        {
            foreach (var col in Columns)
            {
                var v = row[col];
                if (string.IsNullOrEmpty(v)) continue;
                row[col] = string.Join(' ', v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        StatusMessage = "Spasi berlebih di seluruh sel dirapikan.";
        RefreshFilteredRows();
    }

    public void NormalizeGenderColumn(string? column)
    {
        var col = column;
        if (string.IsNullOrEmpty(col))
            col = Columns.FirstOrDefault(c => YearbookLayoutService.CanonicalName(c) == "JK");
        if (string.IsNullOrEmpty(col) || !Columns.Contains(col))
        {
            StatusMessage = "Kolom jenis kelamin (JK) tidak ditemukan.";
            return;
        }

        PushUndo();
        foreach (var row in Rows)
            row[col] = YearbookLayoutService.NormalizeGender(row[col]);
        StatusMessage = $"Jenis kelamin dinormalisasi ke L / P di kolom {col}.";
        RefreshFilteredRows();
    }

    public int MarkDuplicates(string? column)
    {
        var col = column;
        if (string.IsNullOrEmpty(col))
            col = Columns.FirstOrDefault(c => YearbookLayoutService.CanonicalName(c) == "NAMA")
                  ?? Columns.FirstOrDefault();
        if (string.IsNullOrEmpty(col)) return 0;

        PushUndo();
        foreach (var row in Rows)
            row.TagColor = "#00000000";

        var groups = Rows
            .Select(r => (Row: r, Key: YearbookLayoutService.Collapse(r[col])))
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key)
            .Where(g => g.Count() > 1)
            .ToList();

        int marked = 0;
        foreach (var g in groups)
        {
            foreach (var item in g)
            {
                item.Row.TagColor = "#33EF4444";
                marked++;
            }
        }

        StatusMessage = marked == 0
            ? $"Tidak ada duplikat di kolom {col}."
            : $"{marked} baris duplikat di kolom {col} ditandai merah.";
        RefreshFilteredRows();
        return marked;
    }

    public void ApplyJobLayout()
    {
        PushUndo();
        var target = YearbookLayoutService.ColumnsFor(JobKind).ToList();

        var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in Columns.ToList())
        {
            var canon = YearbookLayoutService.CanonicalName(col);
            if (canon != null && canon != col && !Columns.Contains(canon) && !renameMap.ContainsValue(canon))
                renameMap[col] = canon;
        }

        foreach (var kv in renameMap)
            RenameColumnInternal(kv.Key, kv.Value);

        foreach (var name in target)
        {
            if (!Columns.Contains(name))
                InsertColumnInternal(name, Columns.Count);
        }

        ReorderColumns(target);
        StatusMessage = $"Susunan kolom: {DataJobKindInfo.Label(JobKind)}.";
        RefreshFilteredRows();
    }

    public void ReorderColumns(IReadOnlyList<string> preferredFirst)
    {
        var ordered = new List<string>();
        foreach (var name in preferredFirst)
        {
            if (Columns.Contains(name) && !ordered.Contains(name))
                ordered.Add(name);
        }
        foreach (var name in Columns)
        {
            if (!ordered.Contains(name))
                ordered.Add(name);
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            int current = Columns.IndexOf(ordered[i]);
            if (current != i && current >= 0)
                Columns.Move(current, i);
        }
    }

    public void InsertRow(int index = -1)
    {
        PushUndo();
        var row = new TableDataRow();
        foreach (var col in Columns)
            row[col] = string.Empty;

        if (index < 0 || index > Rows.Count)
            Rows.Add(row);
        else
            Rows.Insert(index, row);

        int num = 1;
        foreach (var r in Rows)
            r.RowNumber = num++;
        TotalRows = Rows.Count; HasData = Rows.Count > 0;
        StatusMessage = "Baris baru disisipkan.";
        RefreshFilteredRows();
    }

    public void ClearCells(IEnumerable<(TableDataRow Row, string Column)> cells)
    {
        var list = cells.ToList();
        if (list.Count == 0) return;
        PushUndo();
        foreach (var (row, col) in list)
            row[col] = string.Empty;
        StatusMessage = $"{list.Count} sel dikosongkan.";
        RefreshFilteredRows();
    }

    [RelayCommand]
    public async Task NewTableAsync()
    {
        if (Rows.Count > 0 || Columns.Count > 0)
        {
            if (RequestConfirmFunc != null)
            {
                var ask = await RequestConfirmFunc(
                    "Tabel baru",
                    "Buat tabel kosong sesuai jenis pekerjaan? Data yang sedang terbuka akan diganti.");
                if (!ask) return;
            }
        }

        PushUndo();
        Columns.Clear();
        Rows.Clear();
        ColumnFormulas.Clear();
        foreach (var c in YearbookLayoutService.ColumnsFor(JobKind))
            Columns.Add(c);

        var row = new TableDataRow { RowNumber = 1 };
        foreach (var c in Columns)
            row[c] = string.Empty;
        Rows.Add(row);

        CurrentFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            JobKind == DataJobKind.IdCardStudent || JobKind == DataJobKind.IdCardStaff
                ? "idcard_baru.xlsx"
                : "buku_tahunan_baru.xlsx");
        TotalRows = 1;
        StatusMessage = $"Tabel baru: {DataJobKindInfo.Label(JobKind)}. Isi data atau tempel dari Excel.";
        RefreshFilteredRows();
    }

    private void InsertColumnInternal(string newColName, int targetIndex)
    {
        if (Columns.Contains(newColName)) return;
        if (targetIndex >= 0 && targetIndex <= Columns.Count)
            Columns.Insert(targetIndex, newColName);
        else
            Columns.Add(newColName);
        foreach (var row in Rows)
        {
            if (!row.Values.ContainsKey(newColName))
                row[newColName] = string.Empty;
        }
    }

    private void DeleteColumnInternal(string colName)
    {
        if (!Columns.Contains(colName)) return;
        Columns.Remove(colName);
        ColumnFormulas.Remove(colName);
        foreach (var row in Rows)
            row.Values.Remove(colName);
    }

    private void RenameColumnInternal(string oldName, string newName)
    {
        int idx = Columns.IndexOf(oldName);
        if (idx < 0) return;
        Columns[idx] = newName;
        if (ColumnFormulas.Remove(oldName, out var f))
            ColumnFormulas[newName] = f;
        foreach (var row in Rows)
        {
            var val = row[oldName];
            row.Values.Remove(oldName);
            row[newName] = val;
        }
    }

    private sealed class TableSnapshot
    {
        public List<string> Columns { get; init; } = new();
        public List<RowSnap> Rows { get; init; } = new();
        public Dictionary<string, string> Formulas { get; init; } = new();

        public static TableSnapshot Capture(
            ObservableCollection<string> columns,
            ObservableCollection<TableDataRow> rows,
            Dictionary<string, string> formulas)
        {
            return new TableSnapshot
            {
                Columns = columns.ToList(),
                Formulas = new Dictionary<string, string>(formulas, StringComparer.OrdinalIgnoreCase),
                Rows = rows.Select(r => new RowSnap
                {
                    RowNumber = r.RowNumber,
                    TagColor = r.TagColor,
                    Values = new Dictionary<string, string>(r.Values)
                }).ToList()
            };
        }

        public void Restore(
            ObservableCollection<string> columns,
            ObservableCollection<TableDataRow> rows,
            Dictionary<string, string> formulas)
        {
            columns.Clear();
            foreach (var c in Columns) columns.Add(c);

            formulas.Clear();
            foreach (var kv in Formulas) formulas[kv.Key] = kv.Value;

            rows.Clear();
            foreach (var s in Rows)
            {
                var row = new TableDataRow
                {
                    RowNumber = s.RowNumber,
                    TagColor = s.TagColor,
                    Values = new Dictionary<string, string>(s.Values)
                };
                rows.Add(row);
            }
        }

        public sealed class RowSnap
        {
            public int RowNumber { get; set; }
            public string TagColor { get; set; } = "#00000000";
            public Dictionary<string, string> Values { get; set; } = new();
        }
    }
}


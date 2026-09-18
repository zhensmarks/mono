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
    private static int ExtractLeadingFileNumber(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName ?? string.Empty, @"^\s*\(\s*(\d+)\s*\)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : int.MaxValue;
    }

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
    private string _masterPsdFolderPath = string.Empty;

    [ObservableProperty]
    private string _photoFolderPath = string.Empty;

    [ObservableProperty]
    private int _masterPsdFileCount;

    [ObservableProperty]
    private int _photoFileCount;

    public string MasterPsdStatus => string.IsNullOrWhiteSpace(MasterPsdFolderPath)
        ? "Belum dipilih"
        : $"{MasterPsdFileCount} PSD siap";

    public string PhotoFolderStatus => string.IsNullOrWhiteSpace(PhotoFolderPath)
        ? "Belum dipilih"
        : $"{PhotoFileCount} foto siap";

    public void SetMasterPsdFolder(string path)
    {
        MasterPsdFolderPath = path ?? string.Empty;
        MasterPsdFileCount = Directory.Exists(MasterPsdFolderPath)
            ? Directory.EnumerateFiles(MasterPsdFolderPath, "*.*", SearchOption.AllDirectories)
                .Count(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
            : 0;
        _settings.LastPsdFolder = MasterPsdFolderPath;
        _settings.Save();
        OnPropertyChanged(nameof(MasterPsdStatus));
    }

    public void SetPhotoFolder(string path)
    {
        PhotoFolderPath = path ?? string.Empty;
        PhotoFileCount = Directory.Exists(PhotoFolderPath)
            ? Directory.EnumerateFiles(PhotoFolderPath, "*.*", SearchOption.AllDirectories)
                .Count(f => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            : 0;
        _settings.LastPhotoFolder = PhotoFolderPath;
        _settings.Save();
        OnPropertyChanged(nameof(PhotoFolderStatus));

        if (PhotoFileCount > 0 && Rows.Count > 0)
        {
            _ = AutoMatchPhotosAsync();
        }
    }

    /// <summary>
    /// Matching ulang seluruh baris terhadap folder foto aktif. Tidak memerlukan
    /// master PSD — hanya pasangkan nama baris ke file foto terbaik yang tersedia.
    /// </summary>
    public async Task AutoMatchPhotosAsync()
    {
        if (string.IsNullOrWhiteSpace(PhotoFolderPath) || Rows.Count == 0) return;
        if (!Directory.Exists(PhotoFolderPath)) return;

        IsLoading = true;
        StatusMessage = "Mencocokkan foto dengan data...";

        var rowsSnapshot = Rows.ToList();
        var photoDir = PhotoFolderPath;

        var results = await Task.Run(() =>
        {
            var photos = _photoService.CollectPhotosRecursive(photoDir);
            var nameHeaders = new[] { "NAMA", "NAMA LENGKAP", "NAMA SISWA", "NAMA GURU", "NAMA PESERTA DIDIK", "STUDENT NAME" };
            var outResults = new List<(int idx, string path, string fileName, int score, bool matched)>();

            for (int i = 0; i < rowsSnapshot.Count; i++)
            {
                var row = rowsSnapshot[i];
                var name = string.Empty;
                foreach (var column in Columns)
                {
                    if (!nameHeaders.Contains(column.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
                    var value = row[column];
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        name = PhotoMatcherService.ExtractNameFromCell(value);
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    outResults.Add((i, string.Empty, "-", 0, false));
                    continue;
                }

                var match = _photoService.FindBestMatch(name, photos, _settings.PhotoMatchThreshold);
                outResults.Add((i, match.MatchedFilePath ?? string.Empty,
                    match.MatchedFilePath != null ? Path.GetFileName(match.MatchedFilePath) : "-",
                    match.Score, match.IsPassed));
            }

            return outResults;
        });

        foreach (var (idx, photoPath, fileName, score, matched) in results)
        {
            var row = rowsSnapshot[idx];
            row["_MATCHED_PHOTO_PATH"] = photoPath;
            row.MatchedPhoto = fileName;
            row.MatchScore = score;
            row.IsPhotoMatched = matched;
            row.MatchStatus = matched ? "SIAP PROSES" : "PERLU REVIEW";
            row.MatchNote = matched ? "Foto cocok otomatis" : "Tidak ada kandidat aman";
            row.MatchCandidate = fileName;
            row.NotifyPhotoPreviewChanged();
        }

        MatchedPhotosCount = Rows.Count(r => !string.IsNullOrWhiteSpace(r["_MATCHED_PHOTO_PATH"]));
        RefreshPhotoStatusCounts();
        IsLoading = false;
        StatusMessage = MatchedPhotosCount > 0
            ? $"{MatchedPhotosCount} dari {Rows.Count} foto cocok."
            : "Tidak ada foto yang cocok. Periksa folder atau pilih foto manual.";
    }


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

    // Filter cepat berdasarkan status pasangan foto (chip bar di atas tabel).
    [ObservableProperty]
    private string _photoStatusFilter = "SEMUA";

    [ObservableProperty]
    private int _reviewCount;
    [ObservableProperty]
    private int _gandaCount;
    [ObservableProperty]
    private int _cocokCount;
    [ObservableProperty]
    private int _belumCount;

    /// <summary>
    /// Klasifikasi status pasangan foto sebuah baris: COCOK / REVIEW / GANDA / BELUM.
    /// REVIEW mencakup foto yang skor di bawah ambang atau file-nya hilang.
    /// </summary>
    private static string ClassifyPhotoStatus(TableDataRow row)
    {
        var path = row["_MATCHED_PHOTO_PATH"];
        if (string.IsNullOrWhiteSpace(path)) return "BELUM";
        if (!File.Exists(path)) return "HILANG";
        if (!row.IsPhotoMatched) return "REVIEW";
        if (string.Equals(row.MatchStatus, "AMBIGU", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.MatchStatus, "FOTO GANDA", StringComparison.OrdinalIgnoreCase)) return "GANDA";
        return "COCOK";
    }

    public void RefreshPhotoStatusCounts()
    {
        int review = 0, ganda = 0, cocok = 0, belum = 0;
        foreach (var row in Rows)
        {
            switch (ClassifyPhotoStatus(row))
            {
                case "REVIEW":
                case "HILANG":
                    review++; break;
                case "GANDA":
                    ganda++; break;
                case "COCOK":
                    cocok++; break;
                default:
                    belum++; break;
            }
        }
        ReviewCount = review;
        GandaCount = ganda;
        CocokCount = cocok;
        BelumCount = belum;
    }

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
    public Func<Task<(string? PsdFolder, string? PhotoFolder, bool IsRevision, List<string> RevisionFields)>>? RequestProcessPsdDialogFunc { get; set; }
    public Func<string, string, Task>? RequestAlertFunc { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmFunc { get; set; }
    public Func<string, string, string, Task<(bool Confirmed, string Value)>>? RequestInputFunc { get; set; }
    public Func<IEnumerable<string>, string?, Task<(bool Confirmed, string Find, string Replace, string? TargetColumn, bool MatchCase)>>? RequestFindReplaceFunc { get; set; }
    public Func<IEnumerable<string>, string?, Task<(bool Confirmed, string Source, string Separator, string ColA, string ColB, bool DeleteSource)>>? RequestSplitColumnFunc { get; set; }
    public Func<IEnumerable<string>, IEnumerable<string>?, TableDataRow?, Task<(bool Confirmed, List<string> SelectedColumns, string MergeFormat, string Separator, string NewColumnName, bool DeleteSource)>>? RequestCustomMergeFunc { get; set; }
    public Func<List<string>, List<TableDataRow>, Task<TransformDialogResult>>? RequestTransformFunc { get; set; }
    public Func<List<DataCleaningSuggestion>, Task<CleanerDialogResult>>? RequestCleanerFunc { get; set; }
    public Func<TableDataRow, Task<string?>>? RequestManualPhotoFunc { get; set; }
    public Func<Task<string?>>? RequestMasterPsdFolderFunc { get; set; }
    public Func<Task<string?>>? RequestPhotoFolderFunc { get; set; }

    [RelayCommand]
    private async Task PickMasterPsdFolderAsync()
    {
        if (RequestMasterPsdFolderFunc == null) return;
        var path = await RequestMasterPsdFolderFunc();
        if (!string.IsNullOrWhiteSpace(path)) SetMasterPsdFolder(path);
    }

    [RelayCommand]
    private async Task PickPhotoFolderAsync()
    {
        if (RequestPhotoFolderFunc == null) return;
        var path = await RequestPhotoFolderFunc();
        if (!string.IsNullOrWhiteSpace(path)) SetPhotoFolder(path);
    }

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
    private TableSnapshot? _transformPreviewBase;

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

        Rows.CollectionChanged += (s, e) => {
            if (!_isBulkLoadingRows) RefreshFilteredRows();
        };
    }

    private bool _isBulkLoadingRows = false;

    public void SetManualPhotoMatch(TableDataRow row, string photoPath)
    {
        if (row == null || string.IsNullOrWhiteSpace(photoPath)) return;

        row.MatchedPhoto = Path.GetFileName(photoPath);
        row.MatchScore = 1000;
        row.IsPhotoMatched = true;
        row.MatchStatus = "DIPILIH MANUAL";
        row.MatchNote = "Foto dipilih oleh pengguna";
        row.MatchCandidate = Path.GetFileName(photoPath);
        row.MatchReason = "Konfirmasi manual";
        row.Confidence = 100;
        row.Decision = MatchDecisionStatus.Confirmed;
        row.NeedsConfirmation = false;
        row["_MATCHED_PHOTO_PATH"] = photoPath;
        row.NotifyPhotoPreviewChanged();
        MatchedPhotosCount = Rows.Count(r => !string.IsNullOrWhiteSpace(r["_MATCHED_PHOTO_PATH"]));
        RefreshPhotoStatusCounts();
    }

    public void SetManualPhotoMatch(string rowName, string photoPath)
    {
        var row = Rows.FirstOrDefault(r => string.Equals(r["Nama"], rowName, StringComparison.OrdinalIgnoreCase));
        if (row != null) SetManualPhotoMatch(row, photoPath);
    }

    [RelayCommand]
    private async Task SelectManualPhotoAsync(TableDataRow? row)
    {
        if (row == null || RequestManualPhotoFunc == null) return;
        var path = await RequestManualPhotoFunc(row);
        if (!string.IsNullOrWhiteSpace(path)) SetManualPhotoMatch(row, path);
    }

    public void RefreshFilteredRows()
    {
        var term = (SearchFilter ?? string.Empty).Trim().ToLowerInvariant();
        var filter = PhotoStatusFilter ?? "SEMUA";

        FilteredRows.Clear();
        foreach (var row in Rows)
        {
            if (filter != "SEMUA" && !RowMatchesPhotoFilter(row, filter)) continue;

            if (!string.IsNullOrEmpty(term))
            {
                if (!row.RowNumber.ToString().Contains(term))
                {
                    bool match = false;
                    foreach (var val in row.Values.Values)
                    {
                        if (!string.IsNullOrEmpty(val) && val.ToLowerInvariant().Contains(term))
                        {
                            match = true;
                            break;
                        }
                    }
                    if (!match) continue;
                }
            }
            FilteredRows.Add(row);
        }
    }

    private static bool RowMatchesPhotoFilter(TableDataRow row, string filter)
    {
        var st = ClassifyPhotoStatus(row);
        switch (filter)
        {
            case "REVIEW": return st == "REVIEW" || st == "HILANG";
            case "GANDA": return st == "GANDA";
            case "COCOK": return st == "COCOK";
            case "BELUM": return st == "BELUM";
            default: return true;
        }
    }

    partial void OnPhotoStatusFilterChanged(string value)
    {
        RefreshFilteredRows();
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

        if (!string.IsNullOrWhiteSpace(PhotoFolderPath) && Directory.Exists(PhotoFolderPath))
        {
            _ = AutoMatchPhotosAsync();
        }
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
        if (IsLoading) return;

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
                else if (ext is ".csv" or ".tsv" or ".txt")
                {
                    (cols, dataRows) = _excelService.LoadDelimited(path);
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

                    _isBulkLoadingRows = true;
                    Rows.Clear();
                    foreach (var r in dataRows) Rows.Add(r);
                    _isBulkLoadingRows = false;

                    CurrentFilePath = path;
                    TotalRows = Rows.Count; HasData = Rows.Count > 0;
                    if (ext == ".docx")
                    {
                        _allSheets = new List<SheetResult>();
                        SheetNames = new ObservableCollection<string>();
                        CurrentSheetIndex = -1;
                    }
                    else
                    {
                        _allSheets = _excelService.GetAllSheets()
                            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList();
                        SheetNames = new ObservableCollection<string>(_allSheets.Select(s => s.Name));
                        CurrentSheetIndex = SheetNames.Count > 0 ? 0 : -1;
                    }
                    StatusMessage = $"Berhasil memuat {TotalRows} baris dari {Path.GetFileName(path)}";
                    RefreshFilteredRows();
                    RefreshPhotoStatusCounts();

                    if (!string.IsNullOrWhiteSpace(PhotoFolderPath) && Directory.Exists(PhotoFolderPath))
                    {
                        _ = AutoMatchPhotosAsync();
                    }
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
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Info", "Tidak ada data untuk ditransformasi. Buka file terlebih dahulu.");
            return;
        }

        if (RequestTransformFunc == null) return;

        var result = await RequestTransformFunc(Columns.ToList(), Rows.ToList());
        if (!result.Confirmed)
        {
            RestoreTransformPreview();
            return;
        }

        List<string> baseHeaders;
        List<TableDataRow> baseRows;
        if (_transformPreviewBase != null)
        {
            (baseHeaders, baseRows) = SnapshotToTable(_transformPreviewBase);
            CommitTransformPreview();
        }
        else
        {
            baseHeaders = Columns.ToList();
            baseRows = Rows.ToList();
            PushUndo();
        }

        try
        {
            var (newHeaders, newRows) = _transformService.BuildLayerSheet(
                baseHeaders,
                baseRows,
                result.Layers);
            ReplaceTable(newHeaders, newRows);
            var names = string.Join(", ", result.Layers.Select(l => l.Name));
            StatusMessage = $"Kolom hasil diterapkan: {names}. {newRows.Count} baris hasil.";
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
                    Columns.ToList(), sample, Settings.AiProvider, Settings.AiEndpoint, Settings.AiApiKey, Settings.AiModel);
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

    public void ApplyTransformPreview(IReadOnlyList<string> headers, IReadOnlyList<LayerTransformSpec> specs)
    {
        if (_transformPreviewBase == null)
            _transformPreviewBase = TableSnapshot.Capture(Columns, Rows, ColumnFormulas);

        if (specs.Count == 0) return;

        try
        {
            var (baseHeaders, baseRows) = SnapshotToTable(_transformPreviewBase);
            var (newHeaders, newRows) = _transformService.BuildLayerSheet(
                baseHeaders,
                baseRows,
                specs.ToList());
            ReplaceTable(newHeaders, newRows);
        }
        catch (Exception)
        {
            // Spesifikasi belum lengkap saat mengetik — biarkan preview terakhir yang valid.
        }
    }

    public void CommitTransformPreview()
    {
        if (_transformPreviewBase == null) return;

        _undoStack.Push(_transformPreviewBase);
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
        _transformPreviewBase = null;
    }

    public void RestoreTransformPreview()
    {
        if (_transformPreviewBase == null) return;

        _transformPreviewBase.Restore(Columns, Rows, ColumnFormulas);
        _transformPreviewBase = null;
        TotalRows = Rows.Count;
        HasData = Rows.Count > 0;
        RefreshFilteredRows();
    }

    private static (List<string> Headers, List<TableDataRow> Rows) SnapshotToTable(TableSnapshot snapshot)
    {
        var rows = snapshot.Rows.Select(s => new TableDataRow
        {
            RowNumber = s.RowNumber,
            TagColor = s.TagColor,
            Values = new Dictionary<string, string>(s.Values)
        }).ToList();
        return (snapshot.Columns.ToList(), rows);
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
        bool isRevision = false;
        List<string> revisionFields = new();

        if (RequestProcessPsdDialogFunc != null)
        {
            var res = await RequestProcessPsdDialogFunc();
            if (string.IsNullOrEmpty(res.PsdFolder))
                return;

            psdDir = res.PsdFolder;
            isRevision = res.IsRevision;
            revisionFields = res.RevisionFields ?? new List<string>();

            if (isRevision)
            {
                if (revisionFields.Count == 0)
                    return;
            }
            else
            {
                if (string.IsNullOrEmpty(res.PhotoFolder))
                    return;
                photoDir = res.PhotoFolder;
            }
        }

        if (string.IsNullOrEmpty(psdDir))
            return;

        _settings.LastPsdFolder = psdDir;
        if (!isRevision)
        {
            _settings.LastPhotoFolder = photoDir;
        }
        _settings.Save();

        IsLoading = true;
        StatusMessage = isRevision
            ? "Menganalisis template PSD untuk revisi..."
            : "Menganalisis template PSD dan mencocokkan foto...";


        // Ukuran kesiapan PSD-driven (dihitung di bawah, dipakai oleh pesan konfirmasi
        // dan pesan hasil agar konsisten — proses Photoshop berjalan per template PSD).
        int psdReady = 0;
        int psdTotal = 0;
        if (!isRevision)
        {
            // Snapshot Rows ke list lokal agar aman diakses dari background thread
            var rowsSnapshot = Rows.ToList();

            // Semua kalkulasi matching di background thread — JANGAN set ObservableProperty dari sini
                var matchResults = await Task.Run(() =>
            {
                var photos = _photoService.CollectPhotosRecursive(photoDir);
                var psdFiles = Directory.Exists(psdDir)
                    ? Directory.EnumerateFiles(psdDir, "*.*", SearchOption.AllDirectories)
                        .Where(p => p.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) ||
                                    p.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => ExtractLeadingFileNumber(Path.GetFileName(p)))
                        .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : new List<string>();

                int matchCount = 0;
                    var results = new List<(int idx, string matchedPhoto, int matchScore, bool isPassed, string matchedPhotoPath, string status, string note, string candidate)>(rowsSnapshot.Count);
                var nameHeaders = new[] { "NAMA", "NAMA LENGKAP", "NAMA SISWA", "NAMA GURU", "NAMA PESERTA DIDIK", "STUDENT NAME" };

                string ExtractRowName(TableDataRow r)
                {
                    foreach (var column in Columns)
                    {
                        if (!nameHeaders.Contains(column.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
                        var value = r[column];
                        if (!string.IsNullOrWhiteSpace(value))
                            return PhotoMatcherService.ExtractNameFromCell(value);
                    }
                    return string.Empty;
                }

                for (int i = 0; i < rowsSnapshot.Count; i++)
                {
                    var row = rowsSnapshot[i];
                    var name = string.Empty;
                    foreach (var column in Columns)
                    {
                        if (!nameHeaders.Contains(column.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
                        var value = row[column];
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            name = PhotoMatcherService.ExtractNameFromCell(value);
                            break;
                        }
                    }

                    // Sumber utama pasangan foto adalah hasil di tampilan (AutoMatchPhotos /
                    // pilihan manual). Jangan ditimpa heuristik PSD — apa yang tampil di
                    // tabel itulah yang dikirim ke Photoshop.
                    string? matchedPhotoPath = null;
                    string matchedFileName = "-";
                    int matchScore = 0;

                    var existingPhoto = row["_MATCHED_PHOTO_PATH"];
                    if (!string.IsNullOrWhiteSpace(existingPhoto) && File.Exists(existingPhoto))
                    {
                        matchedPhotoPath = existingPhoto;
                        matchedFileName = Path.GetFileName(existingPhoto);
                        matchScore = row.MatchScore > 0 ? row.MatchScore : 100;
                    }

                    var matchedPsd = psdFiles
                        .Select(p => new { Path = p, Score = _photoService.MatchDataRowToPsd(name, Path.GetFileName(p)) })
                        .Where(x => x.Score >= 300)
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.Path)
                        .FirstOrDefault();

                    if (matchedPhotoPath == null && !string.IsNullOrEmpty(name))
                    {
                        var match = _photoService.FindBestMatch(name, photos, _settings.PhotoMatchThreshold);
                        if (match.IsPassed)
                        {
                            matchedPhotoPath = match.MatchedFilePath;
                            matchedFileName = match.MatchedFileName;
                            matchScore = match.Score;
                        }
                    }

                    if (matchedPhotoPath == null && !string.IsNullOrEmpty(matchedPsd))
                    {
                        var viaPsd = _photoService.MatchPhotoToPsd(Path.GetFileName(matchedPsd), photos);
                        if (!string.IsNullOrEmpty(viaPsd))
                        {
                            matchedPhotoPath = viaPsd;
                            matchedFileName = Path.GetFileName(viaPsd);
                            matchScore = 100;
                        }
                        else
                        {
                            matchedFileName = $"[PSD] {Path.GetFileName(matchedPsd)}";
                        }
                    }

                    bool isPassed = !string.IsNullOrEmpty(matchedPhotoPath) || !string.IsNullOrEmpty(matchedPsd);
                    if (isPassed) matchCount++;
                    var status = string.IsNullOrEmpty(name) ? "NAMA KOSONG" : (isPassed ? "SIAP PROSES" : "PERLU REVIEW");
                    var note = isPassed ? "Kandidat ditemukan" : "Tidak ada kandidat aman";
                    results.Add((i, matchedFileName, matchScore, isPassed, matchedPhotoPath ?? string.Empty, status, note, matchedFileName));
                }
                // 1. Tentukan pasangan PSD untuk setiap baris data
                var psdAssignments = new Dictionary<int, string>(); // rowIdx -> psdFileName
                var usedPsds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var pairScores = new List<(int rowIdx, string psdFile, int score)>();
                for (int rIdx = 0; rIdx < rowsSnapshot.Count; rIdx++)
                {
                    var r = rowsSnapshot[rIdx];
                    var rName = ExtractRowName(r);
                    var rPhoto = results[rIdx].matchedPhotoPath;
                    var rNum = r.RowNumber;

                    foreach (var psd in psdFiles)
                    {
                        var psdName = Path.GetFileName(psd);
                        int sc = _photoService.ScorePsdToRow(psdName, rName, rPhoto, rNum);
                        if (sc > 0)
                        {
                            pairScores.Add((rIdx, psdName, sc));
                        }
                    }
                }

                // Pasangkan secara greedy berdasarkan skor tertinggi
                foreach (var pair in pairScores.OrderByDescending(x => x.score))
                {
                    if (!psdAssignments.ContainsKey(pair.rowIdx) && !usedPsds.Contains(pair.psdFile))
                    {
                        psdAssignments[pair.rowIdx] = pair.psdFile;
                        usedPsds.Add(pair.psdFile);
                    }
                }

                // Untuk setiap baris, simpan _MATCHED_PSD_FILE ke results (disisipkan melalui side-channel baris asli karena tuple results sudah fix, kita tulis di bawah setelah Task.Run atau bisa update dictionary row)
                // Agar aman, kita tulis ke r["_MATCHED_PSD_FILE"] langsung karena r adalah reference,
                // ATAU lebih aman kita pass keluar via dictionary psdAssignments.

                int psdReady = psdFiles.Count(psd =>
                {
                    var pName = Path.GetFileName(psd);
                    var matchedPair = psdAssignments.FirstOrDefault(x => string.Equals(x.Value, pName, StringComparison.OrdinalIgnoreCase));
                    if (matchedPair.Value == null) return false;
                    var rPhoto = results[matchedPair.Key].matchedPhotoPath;
                    return !string.IsNullOrWhiteSpace(rPhoto) && File.Exists(rPhoto);
                });

                return (results, matchCount, psdReady, psdTotal: psdFiles.Count, psdAssignments);
            });

            foreach (var (idx, matchedPhoto, matchScore, isPassed, matchedPhotoPath, status, note, candidate) in matchResults.results)
            {
                var row = rowsSnapshot[idx];
                row.MatchedPhoto = matchedPhoto;
                row.MatchScore = matchScore;
                row.IsPhotoMatched = isPassed;
                row.MatchStatus = status;
                row.MatchNote = note;
                row.MatchCandidate = candidate;
                row["_MATCHED_PHOTO_PATH"] = matchedPhotoPath;
                if (matchResults.psdAssignments.TryGetValue(idx, out var assignedPsd))
                {
                    row["_MATCHED_PSD_FILE"] = assignedPsd;
                }
                else
                {
                    row["_MATCHED_PSD_FILE"] = string.Empty;
                }
            }
            psdReady = matchResults.psdReady;
            psdTotal = matchResults.psdTotal;
            MatchedPhotosCount = matchResults.matchCount;
            RefreshPhotoStatusCounts();
        }

        string confirmMsg;
        if (isRevision)
        {
            confirmMsg = $"Siap merevisi data pada template PSD:\n\n" +
                         $"• Kolom Diperbarui: {string.Join(", ", revisionFields)}\n" +
                         $"• Jumlah Siswa: {Rows.Count}\n" +
                         $"• Folder PSD: {Path.GetFileName(psdDir)}\n\n" +
                         $"Foto tidak dimasukkan ulang. Lanjutkan proses revisi ke Photoshop?";
        }
        else
        {
            confirmMsg = $"Siap memproses data ke Photoshop:\n\n" +
                         $"• Data: {Rows.Count} baris\n" +
                         $"• Template PSD: {psdTotal} file\n" +
                         $"• Siap terpasang (data + foto): {psdReady} dari {psdTotal}\n" +
                         $"• Foto cocok di tabel: {MatchedPhotosCount} dari {Rows.Count}\n" +
                         $"• Folder PSD: {Path.GetFileName(psdDir)}\n\n" +
                         (psdTotal > 0 && psdReady < psdTotal
                             ? $"Perhatian: {psdTotal - psdReady} template PSD belum punya pasangan data/foto dan akan dilewati.\n\n"
                             : "") +
                         "Lanjutkan proses rendering otomatis ke Photoshop?";
        }

        if (RequestConfirmFunc != null)
        {
            var confirmRes = await RequestConfirmFunc("Konfirmasi Proses Photoshop", confirmMsg);
            if (!confirmRes)
            {
                IsLoading = false;
                StatusMessage = isRevision
                    ? "Proses revisi Photoshop dibatalkan oleh pengguna."
                    : "Proses Photoshop dibatalkan oleh pengguna.";
                return;
            }
        }

        // Segarkan state folder VM (tanpa memicu matching ulang) supaya prefill dialog
        // dan status toolbar di run berikutnya memakai folder yang baru dipilih.
        SetMasterPsdFolder(psdDir);
        if (!isRevision)
        {
            PhotoFolderPath = photoDir;
            PhotoFileCount = Directory.Exists(photoDir)
                ? Directory.EnumerateFiles(photoDir, "*.*", SearchOption.AllDirectories)
                    .Count(f => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                : 0;
            OnPropertyChanged(nameof(PhotoFolderStatus));
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
                dict["_MATCHED_PHOTO_PATH"] = isRevision ? "" : r["_MATCHED_PHOTO_PATH"];
                dict["_MATCHED_PSD_FILE"] = r["_MATCHED_PSD_FILE"];
                return dict;
            }).ToList()
        };
        await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var report = await _psBridge.RunProcessAsync(jsonPath, psdDir, photoDir, progress,
                operation: isRevision ? "revision" : "full",
                fields: isRevision ? revisionFields : null);

            var unresolvedPsd = Math.Max(0, psdTotal - report.RowsProcessed);
            StatusMessage = isRevision
                ? $"Proses revisi selesai. {report.RowsProcessed} data diperbarui, {unresolvedPsd} belum diproses."
                : $"Proses Photoshop selesai. {report.RowsProcessed} dari {psdTotal} template PSD terpasang, {unresolvedPsd} dilewati (tidak punya pasangan data/foto).";
            if (RequestAlertFunc != null)
                await RequestAlertFunc("Hasil Proses Photoshop",
                    (isRevision
                        ? $"Total data: {Rows.Count}\nData diperbarui: {report.RowsProcessed}\nBelum diproses: {unresolvedPsd}"
                        : $"Total data: {Rows.Count}\nTemplate PSD: {psdTotal}\nTerpasang lengkap: {report.RowsProcessed}\nDilewati (tidak punya pasangan data/foto): {unresolvedPsd}") +
                    "\n\nPeriksa PSD yang dilewati: cocokkan namanya dengan data, atau pakai tombol Pilih Foto di baris yang bersangkutan.");
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




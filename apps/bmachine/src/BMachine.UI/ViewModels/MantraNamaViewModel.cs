using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using ImageMagick;
using NaturalSort.Extension;
namespace BMachine.UI.ViewModels;
public class RenamePreviewItem : ObservableObject
{
    public string OriginalName { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string FolderLabel { get; set; } = "";
    public bool IsHeader { get; set; }
    public bool HasFolder => !string.IsNullOrEmpty(FolderName);

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    private string _newName = "";
    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
    }

    // Visual indicator: sudah dipasangkan di AdvancedPairs
    private bool _isPaired;
    public bool IsPaired
    {
        get => _isPaired;
        set => SetProperty(ref _isPaired, value);
    }
}
public class AdvancedRenamePair : ObservableObject
{
    public string ReferenceName { get; set; } = "";
    public string ReferencePath { get; set; } = "";
    public RenamePreviewItem? ReferencePreview { get; set; }
    private IReadOnlyList<RenamePreviewItem> _targetOptions = Array.Empty<RenamePreviewItem>();
    public IReadOnlyList<RenamePreviewItem> TargetOptions { get => _targetOptions; set => SetProperty(ref _targetOptions, value); }
    private RenamePreviewItem? _selectedTargetItem;
    public RenamePreviewItem? SelectedTargetItem
    {
        get => _selectedTargetItem;
        set
        {
            var sameSource = string.Equals(_selectedTargetItem?.OriginalPath, value?.OriginalPath, StringComparison.OrdinalIgnoreCase);
            if (!SetProperty(ref _selectedTargetItem, value)) return;
            TargetName = value?.OriginalName ?? "";
            TargetPath = value?.OriginalPath ?? "";
            if (!sameSource) IsReviewed = false;
            TargetChanged?.Invoke(this);
        }
    }
    private string _targetName = "";
    public string TargetName { get => _targetName; set => SetProperty(ref _targetName, value); }
    private string _targetPath = "";
    public string TargetPath { get => _targetPath; set => SetProperty(ref _targetPath, value); }
    private string _resultName = "";
    public string ResultName { get => _resultName; set => SetProperty(ref _resultName, value); }
    private bool _isReviewed;
    public bool IsReviewed
    {
        get => _isReviewed;
        set
        {
            if (!SetProperty(ref _isReviewed, value)) return;
            OnPropertyChanged(nameof(ReviewStateLabel));
            ReviewChanged?.Invoke(this);
        }
    }
    private bool _isSelectedForBulkReview = false;
    public bool IsSelectedForBulkReview { get => _isSelectedForBulkReview; set => SetProperty(ref _isSelectedForBulkReview, value); }
    public string ReviewStateLabel => IsReviewed ? "SUDAH DITINJAU" : "BELUM DITINJAU";
    private string _status = "Siap";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    public event Action<AdvancedRenamePair>? TargetChanged;
    public event Action<AdvancedRenamePair>? ReviewChanged;
}

public sealed record AdvancedUnmatchedFile(string Side, string FileName);

public record AdvancedRenameUndo(string OldPath, string NewPath);


public partial class MantraNamaViewModel : ObservableObject
{
    [ObservableProperty] private bool _isAdvancedMode;
    [ObservableProperty] private bool _isThumbnailMode;
    [ObservableProperty] private bool _autoPairEnabled = true;
    [ObservableProperty] private bool _advancedRenameSiblings = true;
    // Pengaturan jumlah kolom thumbnail per panel (1-4)
    [ObservableProperty] private int _advancedReferenceColumns = 2;
    [ObservableProperty] private int _advancedTargetColumns = 2;
    // Grouping per folder saat subfolder aktif
    [ObservableProperty] private bool _advancedGroupByFolder = true;
    // Urutan (sort) TERPISAH per panel: "Nama / No" atau "Tipe"
    [ObservableProperty] private string _advancedReferenceSortBy = "Nama / No";
    [ObservableProperty] private string _advancedTargetSortBy = "Nama / No";
    // Backward-compat alias (dipakai di beberapa tempat lama)
    public string AdvancedSortBy
    {
        get => AdvancedReferenceSortBy;
        set { AdvancedReferenceSortBy = value; AdvancedTargetSortBy = value; }
    }
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Nama / No", "Tipe" };
    public IReadOnlyList<int> ColumnOptions { get; } = new[] { 1, 2, 3, 4 };

    // Counter per panel
    [ObservableProperty] private int _advancedReferenceCount;
    [ObservableProperty] private int _advancedTargetCount;
    [ObservableProperty] private string _advancedUnmatchedSummary = "";

    // Undo stack
    private readonly Stack<List<AdvancedRenameUndo>> _undoStack = new();
    public bool CanUndo => _undoStack.Count > 0;
    [ObservableProperty] private string _undoLabel = "";
    public bool IsListMode
    {
        get => !IsThumbnailMode;
        set { if (value) IsThumbnailMode = false; }
    }
    [ObservableProperty] private string _advancedReferenceDirectory = string.Empty;
    [ObservableProperty] private string _advancedTargetDirectory = string.Empty;
    [ObservableProperty] private string _advancedReferenceFilter = "Semua gambar";
    [ObservableProperty] private string _advancedTargetFilter = "Semua gambar";
    [ObservableProperty] private bool _advancedIncludeSubfolders;
    [ObservableProperty] private string _advancedReferenceSearch = string.Empty;
    [ObservableProperty] private string _advancedTargetSearch = string.Empty;
    [ObservableProperty] private RenamePreviewItem? _selectedAdvancedReference;
    [ObservableProperty] private RenamePreviewItem? _selectedAdvancedTarget;
    [ObservableProperty] private string _advancedPairFilter = "Semua usulan";
    public ObservableCollection<RenamePreviewItem> AdvancedReferenceFiles { get; } = new();
    public ObservableCollection<RenamePreviewItem> AdvancedTargetFiles { get; } = new();
    public ObservableCollection<AdvancedRenamePair> AdvancedPairs { get; } = new();
    public ObservableCollection<AdvancedRenamePair> VisibleAdvancedPairs { get; } = new();
    public ObservableCollection<AdvancedUnmatchedFile> AdvancedUnmatchedFiles { get; } = new();
    public IReadOnlyList<string> AdvancedPairFilterOptions { get; } = new[] { "Semua usulan", "Belum ditinjau / ragu", "Konflik", "Tanpa pasangan" };
    public bool ShowUnmatchedFiles => string.Equals(AdvancedPairFilter, "Tanpa pasangan", StringComparison.Ordinal);
    public bool ShowPairList => !ShowUnmatchedFiles;
    public IReadOnlyList<string> AdvancedFileFilters { get; } = new[] { "Semua gambar", "JPG / JPEG", "PNG", "WEBP", "BMP", "GIF" };
    private List<RenamePreviewItem> _advancedReferenceMaster = new();
    private List<RenamePreviewItem> _advancedTargetMaster = new();
    private bool _isRefreshingAdvancedTargetChoices;
    partial void OnAdvancedReferenceSearchChanged(string value) => RefreshAdvancedReferenceList();
    partial void OnAdvancedTargetSearchChanged(string value) => RefreshAdvancedTargetList();
    partial void OnAdvancedPairFilterChanged(string value)
    {
        OnPropertyChanged(nameof(ShowUnmatchedFiles));
        OnPropertyChanged(nameof(ShowPairList));
        RefreshVisibleAdvancedPairs();
    }
    partial void OnAdvancedReferenceFilterChanged(string value)
    {
        _advancedReferenceMaster = LoadAdvancedFiles(AdvancedReferenceDirectory, value, AdvancedReferenceSortBy);
        RefreshAdvancedReferenceList();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    partial void OnAdvancedTargetFilterChanged(string value)
    {
        _advancedTargetMaster = LoadAdvancedFiles(AdvancedTargetDirectory, value, AdvancedTargetSortBy);
        RefreshAdvancedTargetList();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    partial void OnAdvancedIncludeSubfoldersChanged(bool value) => ReloadAdvancedFiles();
    private void SetAdvancedPath(string path, bool reference)
    {
        if (File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (reference) AdvancedReferenceDirectory = directory; else AdvancedTargetDirectory = directory;
            ReloadAdvancedFiles();
            var master = reference ? _advancedReferenceMaster : _advancedTargetMaster;
            var filtered = master.Where(x => string.Equals(x.OriginalPath, path, StringComparison.OrdinalIgnoreCase)).ToList();
            if (reference) _advancedReferenceMaster = filtered; else _advancedTargetMaster = filtered;
            RefreshAdvancedLists();
            return;
        }
        if (Directory.Exists(path))
        {
            if (reference) SetAdvancedReferenceDirectory(path); else SetAdvancedTargetDirectory(path);
        }
    }
    public void SetAdvancedReferenceDirectory(string directory)
    {
        AdvancedReferenceDirectory = directory;
        _advancedReferenceMaster = LoadAdvancedFiles(directory, AdvancedReferenceFilter, AdvancedReferenceSortBy);
        RefreshAdvancedReferenceList();
        if (AutoPairEnabled && _advancedTargetMaster.Count > 0) AutoPairByOrder();
    }
    public void SetAdvancedTargetDirectory(string directory)
    {
        AdvancedTargetDirectory = directory;
        _advancedTargetMaster = LoadAdvancedFiles(directory, AdvancedTargetFilter, AdvancedTargetSortBy);
        RefreshAdvancedTargetList();
        if (AutoPairEnabled && _advancedReferenceMaster.Count > 0) AutoPairByOrder();
    }
    public void SetAdvancedReferencePath(string path) => SetAdvancedPath(path, true);
    public void SetAdvancedTargetPath(string path) => SetAdvancedPath(path, false);
    private static bool SupportedImage(string path) => new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool MatchesFilter(string path, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "Semua gambar", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (filter.Contains("JPG", StringComparison.OrdinalIgnoreCase) || filter.Contains("JPEG", StringComparison.OrdinalIgnoreCase))
        {
            return ext is "jpg" or "jpeg";
        }
        return string.Equals(ext, filter.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }
    private List<RenamePreviewItem> LoadAdvancedFiles(string directory, string filter, string? sortBy = null)
    {
        if (!Directory.Exists(directory)) return new();
        try
        {
            var option = AdvancedIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(directory, "*.*", option)
                .Where(SupportedImage)
                .Where(f => MatchesFilter(f, filter))
                .Take(10000)
                .ToList();
            bool group = AdvancedIncludeSubfolders && AdvancedGroupByFolder;
            bool byType = string.Equals(sortBy ?? AdvancedReferenceSortBy, "Tipe", StringComparison.OrdinalIgnoreCase);
            if (group)
            {
                var grouped = files.OrderBy(f => RelativeFolder(directory, f), StringComparer.OrdinalIgnoreCase.WithNaturalSort());
                grouped = byType
                    ? grouped.ThenBy(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase.WithNaturalSort())
                              .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase.WithNaturalSort())
                    : grouped.ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase.WithNaturalSort());
                files = grouped.ToList();
            }
            else
            {
                files = byType
                    ? files.OrderBy(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase.WithNaturalSort())
                           .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase.WithNaturalSort()).ToList()
                    : files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase.WithNaturalSort()).ToList();
            }
            return files.Select(f =>
            {
                var item = new RenamePreviewItem { OriginalName = Path.GetFileName(f), OriginalPath = f, NewName = Path.GetFileName(f) };
                if (group)
                {
                    item.FolderName = Path.GetDirectoryName(f) ?? "";
                    item.FolderLabel = RelativeFolder(directory, f);
                }
                return item;
            }).ToList();
        }
        catch { return new(); }
    }
    private static string RelativeFolder(string root, string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file) ?? "";
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) return ".";
            return Path.GetRelativePath(root, dir);
        }
        catch { return Path.GetFileName(Path.GetDirectoryName(file) ?? ""); }
    }
    private List<RenamePreviewItem> BuildDisplayList(List<RenamePreviewItem> master, string search, int max)
    {
        var filtered = master.Where(x => string.IsNullOrWhiteSpace(search) || x.OriginalName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        bool group = AdvancedIncludeSubfolders && AdvancedGroupByFolder;
        var result = new List<RenamePreviewItem>();
        if (!group)
        {
            foreach (var item in filtered.Take(max)) result.Add(item);
            return result;
        }
        string? last = null;
        int shown = 0;
        foreach (var item in filtered)
        {
            if (!string.Equals(last, item.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                last = item.FolderName;
                var count = filtered.Count(x => string.Equals(x.FolderName, item.FolderName, StringComparison.OrdinalIgnoreCase));
                result.Add(new RenamePreviewItem { IsHeader = true, FolderLabel = item.FolderLabel, OriginalName = item.FolderLabel, NewName = $"{count} file" });
            }
            if (shown >= max) break;
            result.Add(item);
            shown++;
        }
        return result;
    }
    private void ReloadAdvancedFiles()
    {
        _advancedReferenceMaster = LoadAdvancedFiles(AdvancedReferenceDirectory, AdvancedReferenceFilter, AdvancedReferenceSortBy);
        _advancedTargetMaster = LoadAdvancedFiles(AdvancedTargetDirectory, AdvancedTargetFilter, AdvancedTargetSortBy);
        RefreshAdvancedLists();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    partial void OnIsThumbnailModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListMode));
        if (!value && AutoPairEnabled) AutoPairByOrder();
        else if (value) LoadVisibleThumbnails();
    }
    partial void OnAutoPairEnabledChanged(bool value)
    {
        if (value && !IsThumbnailMode) AutoPairByOrder();
    }
    private CancellationTokenSource? _thumbnailCts;
    private void LoadVisibleThumbnails()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;
        var refItems = AdvancedReferenceFiles.Where(x => x.Thumbnail == null && !x.IsHeader).ToList();
        var tgtItems = AdvancedTargetFiles.Where(x => x.Thumbnail == null && !x.IsHeader).ToList();
        if (refItems.Count == 0 && tgtItems.Count == 0) return;
        Task.Run(() =>
        {
            var combined = new List<RenamePreviewItem>();
            int maxCount = Math.Max(refItems.Count, tgtItems.Count);
            for (int i = 0; i < maxCount; i++)
            {
                if (i < refItems.Count) combined.Add(refItems[i]);
                if (i < tgtItems.Count) combined.Add(tgtItems[i]);
            }
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
                CancellationToken = token
            };
            try
            {
                Parallel.ForEach(combined, options, item =>
                {
                    if (token.IsCancellationRequested || item.Thumbnail != null) return;
                    var bmp = LoadThumbnail(item.OriginalPath);
                    if (bmp != null && !token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested) item.Thumbnail = bmp;
                        });
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch { }
        }, token);
    }
    private static Bitmap? LoadThumbnail(string path)
    {
        try
        {
            var settings = new MagickReadSettings { Width = 140, Height = 180 };
            using var img = new MagickImage(path, settings);
            img.AutoOrient();
            img.Resize(new MagickGeometry(140, 180) { IgnoreAspectRatio = false });
            using var ms = new MemoryStream();
            img.Write(ms, MagickFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 140, BitmapInterpolationMode.MediumQuality);
            }
            catch { return null; }
        }
    }
    private void AutoPairByOrder()
    {
        AdvancedPairs.Clear();
        AdvancedUnmatchedFiles.Clear();
        var refList = _advancedReferenceMaster.Take(500).ToList();
        var tgtList = _advancedTargetMaster.Take(500).ToList();
        var count = Math.Min(refList.Count, tgtList.Count);
        for (int i = 0; i < count; i++)
        {
            var pair = CreateAdvancedPair(refList[i], tgtList[i]);
            AdvancedPairs.Add(pair);
        }
        RefreshUnmatchedFiles();
        RefreshPairedFlags();
        RefreshPairStatuses();
        LoadAdvancedPairThumbnails();
        StatusText = count > 0
            ? $"Draf awal dibuat: {count} pasangan BELUM DITINJAU. Urutan hanya saran awal—bukan pengenalan identitas visual. Periksa thumbnail dan koreksi target sebelum menandai ditinjau."
            : "Tidak ada file untuk dibuatkan usulan";
    }
    private void RefreshUnmatchedFiles()
    {
        var referencesInPairs = AdvancedPairs.Select(pair => pair.ReferencePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetsInPairs = AdvancedPairs.Select(pair => pair.TargetPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatchedReferences = _advancedReferenceMaster.Where(file => !referencesInPairs.Contains(file.OriginalPath)).ToList();
        var unmatchedTargets = _advancedTargetMaster.Where(file => !targetsInPairs.Contains(file.OriginalPath)).ToList();
        AdvancedUnmatchedFiles.Clear();
        foreach (var file in unmatchedReferences) AdvancedUnmatchedFiles.Add(new AdvancedUnmatchedFile("Referensi", file.OriginalName));
        foreach (var file in unmatchedTargets) AdvancedUnmatchedFiles.Add(new AdvancedUnmatchedFile("Foto", file.OriginalName));
        var summary = new List<string>();
        if (unmatchedReferences.Count > 0) summary.Add($"referensi tanpa pasangan ({unmatchedReferences.Count}): {string.Join(", ", unmatchedReferences.Take(4).Select(file => file.OriginalName))}");
        if (unmatchedTargets.Count > 0) summary.Add($"foto tanpa pasangan ({unmatchedTargets.Count}): {string.Join(", ", unmatchedTargets.Take(4).Select(file => file.OriginalName))}");
        if (_advancedReferenceMaster.Count > 500 || _advancedTargetMaster.Count > 500) summary.Add("daftar usulan dibatasi 500 file per sisi");
        AdvancedUnmatchedSummary = summary.Count == 0 ? "Tidak ada file tersisa tanpa pasangan." : string.Join(" · ", summary);
    }
    private AdvancedRenamePair CreateAdvancedPair(RenamePreviewItem reference, RenamePreviewItem target)
    {
        var pair = new AdvancedRenamePair
        {
            ReferenceName = reference.OriginalName,
            ReferencePath = reference.OriginalPath,
            ReferencePreview = reference,
            TargetOptions = _advancedTargetMaster.ToList()
        };
        pair.SelectedTargetItem = target;
        pair.ResultName = Path.GetFileNameWithoutExtension(reference.OriginalName) + Path.GetExtension(target.OriginalName);
        pair.TargetChanged += OnAdvancedPairTargetChanged;
        pair.ReviewChanged += _ => RefreshPairStatuses();
        return pair;
    }
    private void OnAdvancedPairTargetChanged(AdvancedRenamePair pair)
    {
        pair.ResultName = pair.SelectedTargetItem == null
            ? ""
            : Path.GetFileNameWithoutExtension(pair.ReferenceName) + Path.GetExtension(pair.TargetName);
        RefreshPairStatuses();
        RefreshPairedFlags();
        LoadAdvancedPairThumbnails();
        if (!_isRefreshingAdvancedTargetChoices) RefreshUnmatchedFiles();
    }
    private void RefreshPairStatuses()
    {
        var pairs = AdvancedPairs.ToList();
        var duplicateTargets = pairs.Where(p => !string.IsNullOrWhiteSpace(p.TargetPath))
            .GroupBy(p => p.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        var destinations = pairs.Where(p => !string.IsNullOrWhiteSpace(p.TargetPath) && !string.IsNullOrWhiteSpace(p.ResultName))
            .Select(p => (Pair: p, Path: Path.Combine(Path.GetDirectoryName(p.TargetPath) ?? "", p.ResultName)))
            .ToList();
        var duplicateDestinations = destinations.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).SelectMany(g => g).Select(x => x.Pair).ToHashSet();
        var reviewedSources = pairs.Where(p => p.IsReviewed && !string.IsNullOrWhiteSpace(p.TargetPath))
            .Select(p => p.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in pairs)
        {
            if (pair.SelectedTargetItem == null || string.IsNullOrWhiteSpace(pair.TargetPath)) { pair.Status = "Target belum dipilih"; continue; }
            if (!File.Exists(pair.TargetPath)) { pair.Status = "Target tidak ditemukan"; continue; }
            if (duplicateTargets.Contains(pair)) { pair.Status = "Konflik: target ganda"; continue; }
            if (duplicateDestinations.Contains(pair)) { pair.Status = "Konflik: nama tujuan ganda"; continue; }
            if (string.IsNullOrWhiteSpace(pair.ResultName) || Path.GetFileName(pair.ResultName) != pair.ResultName || pair.ResultName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { pair.Status = "Konflik: nama tidak valid"; continue; }
            var destination = Path.Combine(Path.GetDirectoryName(pair.TargetPath) ?? "", pair.ResultName);
            if (string.Equals(destination, pair.TargetPath, StringComparison.OrdinalIgnoreCase))
            { pair.Status = pair.IsReviewed ? "Tidak berubah" : "Belum ditinjau"; continue; }
            if ((File.Exists(destination) || Directory.Exists(destination)) && !reviewedSources.Contains(destination))
            { pair.Status = "Konflik: tujuan sudah ada"; continue; }
            if (!pair.IsReviewed) { pair.Status = "Belum ditinjau"; continue; }
            pair.Status = "Siap";
        }
        RefreshVisibleAdvancedPairs();
    }
    private void RefreshVisibleAdvancedPairs()
    {
        VisibleAdvancedPairs.Clear();
        IEnumerable<AdvancedRenamePair> visible = AdvancedPairs;
        if (string.Equals(AdvancedPairFilter, "Belum ditinjau / ragu", StringComparison.Ordinal))
            visible = visible.Where(pair => !pair.IsReviewed || pair.Status.StartsWith("Konflik", StringComparison.Ordinal));
        else if (string.Equals(AdvancedPairFilter, "Konflik", StringComparison.Ordinal))
            visible = visible.Where(pair => pair.Status.StartsWith("Konflik", StringComparison.Ordinal));
        foreach (var pair in visible) VisibleAdvancedPairs.Add(pair);
    }
    private void LoadAdvancedPairThumbnails()
    {
        var items = AdvancedPairs.SelectMany(p => new[] { p.ReferencePreview, p.SelectedTargetItem })
            .Where(x => x != null && x.Thumbnail == null && File.Exists(x.OriginalPath))
            .Cast<RenamePreviewItem>().DistinctBy(x => x.OriginalPath, StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) return;
        _ = Task.Run(() =>
        {
            Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, item =>
            {
                var thumbnail = LoadThumbnail(item.OriginalPath);
                if (thumbnail != null)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => item.Thumbnail = thumbnail);
            });
        });
    }
    private void RefreshPendingTargetChoices()
    {
        var choices = _advancedTargetMaster.ToList();
        var pending = AdvancedPairs.Select(pair => new { Pair = pair, TargetPath = pair.TargetPath, WasReviewed = pair.IsReviewed }).ToList();
        _isRefreshingAdvancedTargetChoices = true;
        try
        {
            foreach (var entry in pending)
            {
                entry.Pair.TargetOptions = choices;
                var refreshedSelection = choices.FirstOrDefault(x => string.Equals(x.OriginalPath, entry.TargetPath, StringComparison.OrdinalIgnoreCase));
                entry.Pair.SelectedTargetItem = refreshedSelection;
                if (refreshedSelection != null && entry.WasReviewed) entry.Pair.IsReviewed = true;
            }
        }
        finally { _isRefreshingAdvancedTargetChoices = false; }
        RefreshUnmatchedFiles();
        RefreshPairStatuses();
        LoadAdvancedPairThumbnails();
    }
    [RelayCommand] private void ClearAdvancedPairs()
    {
        AdvancedPairs.Clear();
        RefreshUnmatchedFiles();
        RefreshVisibleAdvancedPairs();
        RefreshAdvancedLists();
    }
    [RelayCommand] private void RefreshAutoPair() => AutoPairByOrder();
    [RelayCommand] private void ReviewVisibleAdvancedPairs()
    {
        var selected = VisibleAdvancedPairs.Where(pair => pair.IsSelectedForBulkReview).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Tidak ada pasangan terlihat yang dipilih. Centang baris yang sudah diperiksa secara visual terlebih dahulu.";
            return;
        }
        foreach (var pair in selected)
        {
            pair.IsReviewed = true;
            pair.IsSelectedForBulkReview = false;
        }
        RefreshPairStatuses();
        StatusText = $"{selected.Count} pasangan terlihat yang dipilih ditandai sudah ditinjau. Konflik tetap terblokir sampai diperbaiki.";
    }
    private void RefreshAdvancedReferenceList()
    {
        AdvancedReferenceFiles.Clear();
        foreach (var item in BuildDisplayList(_advancedReferenceMaster, AdvancedReferenceSearch, 500))
            AdvancedReferenceFiles.Add(item);
        AdvancedReferenceCount = _advancedReferenceMaster.Count;
        if (IsThumbnailMode) LoadVisibleThumbnails();
    }
    private void RefreshAdvancedTargetList()
    {
        AdvancedTargetFiles.Clear();
        foreach (var item in BuildDisplayList(_advancedTargetMaster, AdvancedTargetSearch, 500))
            AdvancedTargetFiles.Add(item);
        AdvancedTargetCount = _advancedTargetMaster.Count;
        if (IsThumbnailMode) LoadVisibleThumbnails();
    }
    private void RefreshAdvancedLists()
    {
        RefreshAdvancedReferenceList();
        RefreshAdvancedTargetList();
        RefreshPairedFlags();
        RefreshPairStatuses();
    }
    private void RefreshPairedFlags()
    {
        var refPaths = new HashSet<string>(AdvancedPairs.Select(p => p.ReferencePath), StringComparer.OrdinalIgnoreCase);
        var tgtPaths = new HashSet<string>(AdvancedPairs.Select(p => p.TargetPath), StringComparer.OrdinalIgnoreCase);
        foreach (var item in _advancedReferenceMaster) item.IsPaired = refPaths.Contains(item.OriginalPath);
        foreach (var item in _advancedTargetMaster) item.IsPaired = tgtPaths.Contains(item.OriginalPath);
    }
    partial void OnAdvancedGroupByFolderChanged(bool value) => ReloadAdvancedFiles();
    partial void OnAdvancedReferenceSortByChanged(string value)
    {
        _advancedReferenceMaster = LoadAdvancedFiles(AdvancedReferenceDirectory, AdvancedReferenceFilter, value);
        RefreshAdvancedReferenceList();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    partial void OnAdvancedTargetSortByChanged(string value)
    {
        _advancedTargetMaster = LoadAdvancedFiles(AdvancedTargetDirectory, AdvancedTargetFilter, value);
        RefreshAdvancedTargetList();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    [RelayCommand] private void AddAdvancedPair()
    {
        if (SelectedAdvancedReference == null && SelectedAdvancedTarget == null)
        {
            StatusText = "Pilih satu file referensi di kiri dan satu file target di kanan";
            return;
        }
        if (SelectedAdvancedReference == null)
        {
            StatusText = "Pilih file referensi di panel kiri terlebih dahulu";
            return;
        }
        if (SelectedAdvancedTarget == null)
        {
            StatusText = "Pilih file target di panel kanan terlebih dahulu";
            return;
        }
        if (SelectedAdvancedReference.IsHeader || SelectedAdvancedTarget.IsHeader)
        {
            StatusText = "Header folder tidak bisa dipasangkan, pilih file di bawahnya";
            return;
        }
        if (AdvancedPairs.Any(x => string.Equals(x.TargetPath, SelectedAdvancedTarget.OriginalPath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "File target tersebut sudah memiliki pasangan";
            return;
        }
        if (AdvancedPairs.Any(x => string.Equals(x.ReferencePath, SelectedAdvancedReference.OriginalPath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "File referensi tersebut sudah memiliki pasangan";
            return;
        }
        var pair = CreateAdvancedPair(SelectedAdvancedReference, SelectedAdvancedTarget);
        AdvancedPairs.Add(pair);
        RefreshPairStatuses();
        RefreshUnmatchedFiles();
        LoadAdvancedPairThumbnails();
        RefreshPairedFlags();
        StatusText = $"Pasangan ditambahkan: {SelectedAdvancedTarget.OriginalName} → {pair.ResultName}. Tandai sudah ditinjau setelah memeriksa gambar.";
    }
 
    [RelayCommand] private void RemoveAdvancedPair(AdvancedRenamePair? pair)
    {
        if (pair == null) return;
        AdvancedPairs.Remove(pair);
        RefreshAdvancedLists();
        RefreshUnmatchedFiles();
    }

    public bool AssignAdvancedTarget(AdvancedRenamePair? pair, string targetPath)
    {
        if (pair == null || !AdvancedPairs.Contains(pair)) return false;
        var target = pair.TargetOptions.FirstOrDefault(item => string.Equals(item.OriginalPath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (target == null) return false;
        pair.SelectedTargetItem = target;
        StatusText = $"Target untuk {pair.ReferenceName} diubah menjadi {target.OriginalName}. Usulan kembali belum ditinjau.";
        return true;
    }
 
    private sealed class PlannedRenameOperation(AdvancedRenamePair? pair, string sourcePath, string destinationPath)
    {
        public AdvancedRenamePair? Pair { get; } = pair;
        public string SourcePath { get; } = sourcePath;
        public string DestinationPath { get; } = destinationPath;
    }

    private static (Dictionary<AdvancedRenamePair, string> PairErrors, string? GlobalError) InspectRenamePlan(IReadOnlyList<PlannedRenameOperation> operations)
    {
        var pairErrors = new Dictionary<AdvancedRenamePair, string>();
        string? globalError = null;
        void Mark(PlannedRenameOperation operation, string reason)
        {
            if (operation.Pair != null) pairErrors.TryAdd(operation.Pair, reason);
            else globalError ??= reason;
        }

        foreach (var group in operations.GroupBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            foreach (var operation in group) Mark(operation, "satu file sumber dipakai lebih dari sekali");
        foreach (var group in operations.GroupBy(x => x.DestinationPath, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            foreach (var operation in group) Mark(operation, "beberapa file memiliki nama tujuan yang sama");
        foreach (var operation in operations.Where(x => !File.Exists(x.SourcePath)))
            Mark(operation, "file sumber tidak ditemukan");

        while (true)
        {
            var previousIssueCount = pairErrors.Count + (globalError == null ? 0 : 1);
            var active = operations.Where(x => x.Pair == null ? globalError == null : !pairErrors.ContainsKey(x.Pair)).ToList();
            var activeSources = active.Select(x => x.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var operation in active)
            {
                if (string.Equals(operation.SourcePath, operation.DestinationPath, StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.Exists(operation.DestinationPath) || Directory.Exists(operation.DestinationPath)) && !activeSources.Contains(operation.DestinationPath))
                    Mark(operation, "nama tujuan sudah digunakan oleh file yang tidak ikut dipindahkan");
            }
            if (pairErrors.Count + (globalError == null ? 0 : 1) == previousIssueCount) break;
        }
        return (pairErrors, globalError);
    }

    private List<PlannedRenameOperation> BuildRenameOperations(IReadOnlyList<AdvancedRenamePair> pairs)
    {
        var operations = new List<PlannedRenameOperation>();
        var primarySources = pairs.Select(x => x.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var directory = Path.GetDirectoryName(pair.TargetPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException($"Folder target tidak valid: {pair.TargetName}");
            var destination = Path.Combine(directory, pair.ResultName);
            if (!string.Equals(pair.TargetPath, destination, StringComparison.OrdinalIgnoreCase))
                operations.Add(new PlannedRenameOperation(pair, pair.TargetPath, destination));

            if (!AdvancedRenameSiblings || string.Equals(Path.GetFileNameWithoutExtension(pair.TargetName), Path.GetFileNameWithoutExtension(pair.ResultName), StringComparison.OrdinalIgnoreCase)) continue;
            var oldBase = Path.GetFileNameWithoutExtension(pair.TargetName);
            var newBase = Path.GetFileNameWithoutExtension(pair.ResultName);
            foreach (var sibling in Directory.EnumerateFiles(directory, oldBase + ".*", SearchOption.TopDirectoryOnly))
            {
                if (primarySources.Contains(sibling) || !string.Equals(Path.GetFileNameWithoutExtension(sibling), oldBase, StringComparison.OrdinalIgnoreCase)) continue;
                var siblingDestination = Path.Combine(directory, newBase + Path.GetExtension(sibling));
                if (!string.Equals(sibling, siblingDestination, StringComparison.OrdinalIgnoreCase))
                    operations.Add(new PlannedRenameOperation(pair, sibling, siblingDestination));
            }
        }
        return operations;
    }

    private static List<AdvancedRenameUndo> ExecuteTwoPhaseRename(IReadOnlyList<PlannedRenameOperation> operations)
    {
        var inspection = InspectRenamePlan(operations);
        if (inspection.GlobalError != null || inspection.PairErrors.Count > 0)
            throw new IOException(inspection.GlobalError ?? inspection.PairErrors.Values.First());

        var staged = new List<(PlannedRenameOperation Operation, string TemporaryPath, bool Committed)>();
        try
        {
            foreach (var operation in operations)
            {
                var directory = Path.GetDirectoryName(operation.SourcePath) ?? throw new IOException("Folder sumber tidak valid.");
                string temporaryPath;
                do { temporaryPath = Path.Combine(directory, $".bmachine-rename-{Guid.NewGuid():N}.tmp"); }
                while (File.Exists(temporaryPath) || Directory.Exists(temporaryPath));
                File.Move(operation.SourcePath, temporaryPath);
                staged.Add((operation, temporaryPath, false));
            }
            for (var index = 0; index < staged.Count; index++)
            {
                var entry = staged[index];
                if (File.Exists(entry.Operation.DestinationPath) || Directory.Exists(entry.Operation.DestinationPath))
                    throw new IOException($"Tujuan muncul saat proses berjalan: {entry.Operation.DestinationPath}");
                File.Move(entry.TemporaryPath, entry.Operation.DestinationPath);
                staged[index] = (entry.Operation, entry.TemporaryPath, true);
            }
            return operations.Select(x => new AdvancedRenameUndo(x.SourcePath, x.DestinationPath)).ToList();
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<string>();
            foreach (var entry in staged.AsEnumerable().Reverse())
            {
                var currentPath = entry.Committed ? entry.Operation.DestinationPath : entry.TemporaryPath;
                try
                {
                    if (File.Exists(currentPath) && !File.Exists(entry.Operation.SourcePath) && !Directory.Exists(entry.Operation.SourcePath))
                        File.Move(currentPath, entry.Operation.SourcePath);
                    else if (File.Exists(currentPath))
                        rollbackErrors.Add($"{currentPath} tetap ada karena {entry.Operation.SourcePath} sudah digunakan");
                }
                catch (Exception rollbackFailure) { rollbackErrors.Add($"{currentPath}: {rollbackFailure.Message}"); }
            }
            if (rollbackErrors.Count > 0)
                throw new IOException($"Rename gagal ({failure.Message}); pemulihan sebagian gagal: {string.Join("; ", rollbackErrors)}", failure);
            throw;
        }
    }

    [RelayCommand]
    private async Task ExecuteAdvancedRenameAsync()
    {
        if (AdvancedPairs.Count == 0 || IsProcessing) return;
        RefreshPairStatuses();
        var reviewed = AdvancedPairs.Where(x => x.IsReviewed).ToList();
        if (reviewed.Count == 0)
        {
            StatusText = "Belum ada pasangan yang ditinjau. Periksa gambar, koreksi usulan, lalu tandai pasangan yang sudah dicek.";
            return;
        }
        var ready = reviewed.Where(x => x.Status == "Siap").ToList();
        if (ready.Count == 0)
        {
            StatusText = "Tidak ada pasangan siap. Perbaiki konflik tujuan atau tandai pasangan yang sudah diverifikasi.";
            return;
        }

        List<PlannedRenameOperation> operations;
        try { operations = BuildRenameOperations(ready); }
        catch (Exception ex) { StatusText = $"Rencana rename dibatalkan: {ex.Message}"; return; }
        var inspection = InspectRenamePlan(operations);
        foreach (var issue in inspection.PairErrors) issue.Key.Status = $"Konflik: {issue.Value}";
        if (inspection.GlobalError != null)
        {
            StatusText = $"Rencana rename dibatalkan tanpa perubahan: {inspection.GlobalError}.";
            return;
        }
        var blocked = inspection.PairErrors.Keys.ToHashSet();
        ready = ready.Where(x => !blocked.Contains(x)).ToList();
        operations = operations.Where(x => x.Pair == null || !blocked.Contains(x.Pair)).ToList();
        if (operations.Count == 0)
        {
            StatusText = "Tidak ada perubahan yang perlu diterapkan; semua pasangan yang dipilih sudah sama atau mengalami konflik.";
            return;
        }

        IsProcessing = true;
        StatusText = $"Menerapkan {ready.Count} pasangan yang sudah ditinjau secara aman...";
        try
        {
            var undoBatch = await Task.Run(() => ExecuteTwoPhaseRename(operations));
            foreach (var pair in ready) AdvancedPairs.Remove(pair);
            _undoStack.Push(undoBatch);
            UndoLabel = $"Batalkan rename ({undoBatch.Count} file)";
            OnPropertyChanged(nameof(CanUndo));
            ReloadAdvancedFiles();
            RefreshPendingTargetChoices();
            RefreshPairStatuses();
            var notReviewed = AdvancedPairs.Count(x => !x.IsReviewed);
            var remainingConflicts = AdvancedPairs.Count(x => x.Status.StartsWith("Konflik", StringComparison.Ordinal));
            var sidecars = Math.Max(0, undoBatch.Count - ready.Count);
            var extra = sidecars > 0 ? $", termasuk {sidecars} file pendamping" : "";
            StatusText = $"Selesai: {ready.Count} pasangan diubah{extra}; {notReviewed} belum ditinjau, {remainingConflicts} konflik tersisa. Undo tersedia.";
        }
        catch (Exception ex)
        {
            foreach (var pair in ready) pair.Status = "Konflik: transaksi dibatalkan";
            StatusText = $"Rename dibatalkan tanpa overwrite. Periksa sumber/tujuan dan coba lagi: {ex.Message}";
            RefreshPairStatuses();
        }
        finally { IsProcessing = false; }
    }

    [RelayCommand]
    private async Task UndoAdvancedRenameAsync()
    {
        if (_undoStack.Count == 0 || IsProcessing) return;
        var batch = _undoStack.Peek();
        IsProcessing = true;
        StatusText = "Membatalkan rename...";
        try
        {
            var operations = batch.Select(x => new PlannedRenameOperation(null, x.NewPath, x.OldPath)).ToList();
            await Task.Run(() => ExecuteTwoPhaseRename(operations));
            _undoStack.Pop();
            UndoLabel = _undoStack.Count > 0 ? $"Batalkan rename ({_undoStack.Peek().Count} file)" : "";
            OnPropertyChanged(nameof(CanUndo));
            ReloadAdvancedFiles();
            RefreshPendingTargetChoices();
            RefreshPairStatuses();
            StatusText = $"Undo selesai: {batch.Count} file dikembalikan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Undo dibatalkan tanpa overwrite. File mungkin telah berubah sejak rename: {ex.Message}";
        }
        finally { IsProcessing = false; }
    }

    [RelayCommand] private void ToggleAdvancedMode() => IsAdvancedMode = !IsAdvancedMode;
    [RelayCommand] private void ExitAdvancedMode() => IsAdvancedMode = false;
    [RelayCommand] private void SetListMode() => IsThumbnailMode = false;
    [RelayCommand] private void SetThumbnailMode() => IsThumbnailMode = true;

    [ObservableProperty]
    private string _sourceDirectory = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _useRegex = false;

    [ObservableProperty]
    private bool _caseSensitive = false;

    [ObservableProperty]
    private bool _matchAllOccurrences = true;

    [ObservableProperty]
    private bool _excludeExtension = true;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private bool _isOptionsExpanded = false;

    [RelayCommand]
    public void ToggleOptions() => IsOptionsExpanded = !IsOptionsExpanded;

    [ObservableProperty]
    private string _statusText = "Siap untuk merename";

    [RelayCommand]
    public void ClearList()
    {
        _masterList.Clear();
        PreviewList.Clear();
        SourceDirectory = string.Empty;
        StatusText = "List dibersihkan";
    }

    private List<RenamePreviewItem> _masterList = new();
    public ObservableCollection<RenamePreviewItem> PreviewList { get; } = new();

    partial void OnFindTextChanged(string value) => RefreshPreview();
    partial void OnReplaceTextChanged(string value) => RefreshPreview();
    partial void OnUseRegexChanged(bool value) => RefreshPreview();
    partial void OnCaseSensitiveChanged(bool value) => RefreshPreview();
    partial void OnMatchAllOccurrencesChanged(bool value) => RefreshPreview();
    partial void OnExcludeExtensionChanged(bool value) => RefreshPreview();

    public void AddFromDirectory(string directory)
    {
        SourceDirectory = directory;
        var files = Directory.GetFiles(directory).ToList();
        AddFiles(files);
    }

    public void AddFiles(System.Collections.Generic.IEnumerable<string> files)
    {
        var newFiles = files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase.WithNaturalSort()).ToList();
        foreach (var f in newFiles)
        {
            if (!_masterList.Any(x => string.Equals(x.OriginalPath, f, StringComparison.OrdinalIgnoreCase)))
            {
                _masterList.Add(new RenamePreviewItem
                {
                    OriginalName = Path.GetFileName(f),
                    OriginalPath = f,
                    NewName = Path.GetFileName(f)
                });
            }
        }
        
        if (_masterList.Any() && string.IsNullOrEmpty(SourceDirectory))
        {
            SourceDirectory = Path.GetDirectoryName(_masterList.First().OriginalPath) ?? "";
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        PreviewList.Clear();
        foreach (var item in _masterList)
        {
            if (string.IsNullOrWhiteSpace(FilterText) || 
                item.OriginalName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                PreviewList.Add(item);
            }
        }
        RefreshPreview();
    }

    [RelayCommand]
    public void RemoveItem(RenamePreviewItem item)
    {
        if (item != null)
        {
            _masterList.Remove(item);
            PreviewList.Remove(item);
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        if (PreviewList.Count == 0)
        {
            StatusText = "Pilih file atau folder sumber terlebih dahulu";
            return;
        }

        if (string.IsNullOrEmpty(FindText))
        {
            StatusText = "Masukkan teks yang ingin dicari";
            foreach (var item in PreviewList) item.NewName = item.OriginalName;
            return;
        }

        Regex? regex = null;
        RegexOptions options = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        options |= RegexOptions.Compiled;

        if (UseRegex)
        {
            try
            {
                regex = new Regex(FindText, options);
            }
            catch
            {
                StatusText = "Regex tidak valid";
                foreach (var item in PreviewList) item.NewName = item.OriginalName;
                return;
            }
        }
        else
        {
            // Escape find text for regex processing to unify logic
            regex = new Regex(Regex.Escape(FindText), options);
        }

        int changedCount = 0;
        foreach (var item in PreviewList)
        {
            string targetString = item.OriginalName;
            string extension = "";

            if (ExcludeExtension)
            {
                extension = Path.GetExtension(item.OriginalName);
                targetString = Path.GetFileNameWithoutExtension(item.OriginalName);
            }

            string newTarget = item.OriginalName;
            if (regex != null)
            {
                int maxReplacements = MatchAllOccurrences ? -1 : 1;
                newTarget = regex.Replace(targetString, ReplaceText, maxReplacements);
            }

            if (ExcludeExtension)
            {
                newTarget += extension;
            }

            item.NewName = newTarget;
            if (item.NewName != item.OriginalName) changedCount++;
        }

        StatusText = $"{changedCount} file akan diubah";
    }

    [RelayCommand]
    public async Task ExecuteRenameAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || string.IsNullOrEmpty(FindText) || IsProcessing) return;

        IsProcessing = true;
        StatusText = "Memproses...";

        await Task.Run(() =>
        {
            try
            {
                int count = 0;
                var itemsToProcess = PreviewList.ToList();
                var successfulItems = new System.Collections.Generic.List<RenamePreviewItem>();

                foreach (var item in itemsToProcess)
                {
                    if (item.OriginalName == item.NewName) continue;
                    
                    var oldPath = item.OriginalPath;
                    var newPath = Path.Combine(SourceDirectory, item.NewName);

                    if (!File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                        successfulItems.Add(item);
                        count++;
                    }
                }
                
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    foreach (var item in successfulItems)
                    {
                        _masterList.Remove(item);
                        PreviewList.Remove(item);
                    }
                    StatusText = $"Selesai. {count} file berhasil direname.";
                    RefreshPreview();
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
            }
        });

        IsProcessing = false;
    }
}

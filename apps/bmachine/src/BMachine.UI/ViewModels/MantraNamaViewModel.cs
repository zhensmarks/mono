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
    public string TargetName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string ResultName { get; set; } = "";
    private string _status = "Siap";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

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
        get => _advancedReferenceSortBy;
        set { AdvancedReferenceSortBy = value; AdvancedTargetSortBy = value; }
    }
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Nama / No", "Tipe" };
    public IReadOnlyList<int> ColumnOptions { get; } = new[] { 1, 2, 3, 4 };

    // Counter per panel
    [ObservableProperty] private int _advancedReferenceCount;
    [ObservableProperty] private int _advancedTargetCount;

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
    public ObservableCollection<RenamePreviewItem> AdvancedReferenceFiles { get; } = new();
    public ObservableCollection<RenamePreviewItem> AdvancedTargetFiles { get; } = new();
    public ObservableCollection<AdvancedRenamePair> AdvancedPairs { get; } = new();
    public IReadOnlyList<string> AdvancedFileFilters { get; } = new[] { "Semua gambar", "JPG / JPEG", "PNG", "WEBP", "BMP", "GIF" };
    private List<RenamePreviewItem> _advancedReferenceMaster = new();
    private List<RenamePreviewItem> _advancedTargetMaster = new();
    partial void OnAdvancedReferenceSearchChanged(string value) => RefreshAdvancedReferenceList();
    partial void OnAdvancedTargetSearchChanged(string value) => RefreshAdvancedTargetList();
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
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
    }
    public void SetAdvancedTargetDirectory(string directory)
    {
        AdvancedTargetDirectory = directory;
        _advancedTargetMaster = LoadAdvancedFiles(directory, AdvancedTargetFilter, AdvancedTargetSortBy);
        RefreshAdvancedTargetList();
        if (!IsThumbnailMode && AutoPairEnabled) AutoPairByOrder();
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
        var refList = _advancedReferenceMaster.Where(x => string.IsNullOrWhiteSpace(AdvancedReferenceSearch) || x.OriginalName.Contains(AdvancedReferenceSearch, StringComparison.OrdinalIgnoreCase)).Take(500).ToList();
        var tgtList = _advancedTargetMaster.Where(x => string.IsNullOrWhiteSpace(AdvancedTargetSearch) || x.OriginalName.Contains(AdvancedTargetSearch, StringComparison.OrdinalIgnoreCase)).Take(500).ToList();
        var count = Math.Min(refList.Count, tgtList.Count);
        for (int i = 0; i < count; i++)
        {
            var reference = refList[i];
            var target = tgtList[i];
            var result = Path.GetFileNameWithoutExtension(reference.OriginalName) + Path.GetExtension(target.OriginalName);
            var destination = Path.Combine(Path.GetDirectoryName(target.OriginalPath)!, result);
            AdvancedPairs.Add(new AdvancedRenamePair
            {
                ReferenceName = reference.OriginalName,
                ReferencePath = reference.OriginalPath,
                TargetName = target.OriginalName,
                TargetPath = target.OriginalPath,
                ResultName = result,
                Status = File.Exists(destination) && !string.Equals(result, target.OriginalName, StringComparison.OrdinalIgnoreCase) ? "Konflik" : "Siap"
            });
        }
        RefreshPairedFlags();
        StatusText = count > 0 ? $"Dipasangkan otomatis: {count} pasangan" : "Tidak ada file untuk dipasangkan";
    }
    [RelayCommand] private void ClearAdvancedPairs() { AdvancedPairs.Clear(); RefreshAdvancedLists(); }
    [RelayCommand] private void RefreshAutoPair() => AutoPairByOrder();
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
        var result = Path.GetFileNameWithoutExtension(SelectedAdvancedReference.OriginalName) + Path.GetExtension(SelectedAdvancedTarget.OriginalName);
        var destination = Path.Combine(Path.GetDirectoryName(SelectedAdvancedTarget.OriginalPath)!, result);
        AdvancedPairs.Add(new AdvancedRenamePair
        {
            ReferenceName = SelectedAdvancedReference.OriginalName,
            ReferencePath = SelectedAdvancedReference.OriginalPath,
            TargetName = SelectedAdvancedTarget.OriginalName,
            TargetPath = SelectedAdvancedTarget.OriginalPath,
            ResultName = result,
            Status = File.Exists(destination) && !string.Equals(result, SelectedAdvancedTarget.OriginalName, StringComparison.OrdinalIgnoreCase) ? "Konflik" : "Siap"
        });
        RefreshPairedFlags();
        StatusText = $"Pasangan ditambahkan: {SelectedAdvancedReference.OriginalName} -> {result}";
    }
 
    [RelayCommand] private void RemoveAdvancedPair(AdvancedRenamePair? pair) { if (pair != null) { AdvancedPairs.Remove(pair); RefreshAdvancedLists(); } }
 
    [RelayCommand]
    private async Task ExecuteAdvancedRenameAsync()
     {
         if (AdvancedPairs.Count == 0 || IsProcessing) return;
 
         IsProcessing = true;
         StatusText = "Memproses rename...";
 
         await Task.Run(() =>
         {
             try
             {
                int success = 0, skipped = 0, siblingRenamed = 0;
                var undoBatch = new List<AdvancedRenameUndo>();
                var toRemove = new List<AdvancedRenamePair>();

                foreach (var pair in AdvancedPairs.ToList())
                {
                    if (pair.Status == "Konflik") { skipped++; continue; }

                    var dest = Path.Combine(Path.GetDirectoryName(pair.TargetPath)!, pair.ResultName);
                    if (File.Exists(dest) && !string.Equals(pair.ResultName, pair.TargetName, StringComparison.OrdinalIgnoreCase))
                    {
                        pair.Status = "Konflik";
                        skipped++;
                        continue;
                    }

                    try
                    {
                        File.Move(pair.TargetPath, dest);
                        undoBatch.Add(new AdvancedRenameUndo(pair.TargetPath, dest));
                        pair.Status = "Selesai";
                        toRemove.Add(pair);
                        success++;

                        if (AdvancedRenameSiblings)
                        {
                            var dir = Path.GetDirectoryName(pair.TargetPath);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            {
                                var oldBase = Path.GetFileNameWithoutExtension(pair.TargetName);
                                var newBase = Path.GetFileNameWithoutExtension(pair.ResultName);
                                try
                                {
                                    var siblings = Directory.EnumerateFiles(dir, oldBase + ".*", SearchOption.TopDirectoryOnly)
                                        .Where(f => !string.Equals(f, dest, StringComparison.OrdinalIgnoreCase))
                                        .ToList();

                                    foreach (var sib in siblings)
                                    {
                                        if (string.Equals(Path.GetFileNameWithoutExtension(sib), oldBase, StringComparison.OrdinalIgnoreCase))
                                        {
                                            var sibExt = Path.GetExtension(sib);
                                            var newSibPath = Path.Combine(dir, newBase + sibExt);
                                            if (!File.Exists(newSibPath) && !string.Equals(sib, newSibPath, StringComparison.OrdinalIgnoreCase))
                                            {
                                                File.Move(sib, newSibPath);
                                                undoBatch.Add(new AdvancedRenameUndo(sib, newSibPath));
                                                siblingRenamed++;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        pair.Status = $"Error: {ex.Message}";
                        skipped++;
                    }
                }

                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var p in toRemove) AdvancedPairs.Remove(p);
                    if (undoBatch.Count > 0)
                    {
                        _undoStack.Push(undoBatch);
                        UndoLabel = $"Batalkan rename ({undoBatch.Count} file)";
                        OnPropertyChanged(nameof(CanUndo));
                    }
                    var sibMsg = siblingRenamed > 0 ? $" ({siblingRenamed} file kembar ikut direname)" : "";
                    StatusText = $"Selesai: {success} berhasil{sibMsg}, {skipped} dilewati";
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
            }
        });
        IsProcessing = false;
    }

    [RelayCommand]
    private async Task UndoAdvancedRenameAsync()
    {
        if (_undoStack.Count == 0 || IsProcessing) return;
        var batch = _undoStack.Pop();
        IsProcessing = true;
        StatusText = "Membatalkan rename...";
        await Task.Run(() =>
        {
            int ok = 0, fail = 0;
            foreach (var u in batch)
            {
                try
                {
                    if (File.Exists(u.NewPath) && !File.Exists(u.OldPath))
                    {
                        File.Move(u.NewPath, u.OldPath);
                        ok++;
                    }
                }
                catch { fail++; }
            }
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                UndoLabel = _undoStack.Count > 0 ? $"Batalkan rename ({_undoStack.Peek().Count} file)" : "";
                OnPropertyChanged(nameof(CanUndo));
                StatusText = $"Undo selesai: {ok} dikembalikan, {fail} gagal";
                ReloadAdvancedFiles();
            });
        });
        IsProcessing = false;
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

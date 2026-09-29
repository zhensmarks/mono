using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using NaturalSort.Extension;

namespace BMachine.UI.ViewModels;

public class RenamePreviewItem : ObservableObject
{
    public string OriginalName { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    
    private string _newName = "";
    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
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
    public IReadOnlyList<string> AdvancedFileFilters { get; } = new[] { "Semua gambar", "JPG", "JPEG", "PNG", "WEBP", "BMP", "GIF" };
    private List<RenamePreviewItem> _advancedReferenceMaster = new();
    private List<RenamePreviewItem> _advancedTargetMaster = new();
    partial void OnAdvancedReferenceSearchChanged(string value) => RefreshAdvancedLists();
    partial void OnAdvancedTargetSearchChanged(string value) => RefreshAdvancedLists();
    partial void OnAdvancedReferenceFilterChanged(string value) => ReloadAdvancedFiles();
    partial void OnAdvancedTargetFilterChanged(string value) => ReloadAdvancedFiles();
    partial void OnAdvancedIncludeSubfoldersChanged(bool value) => ReloadAdvancedFiles();
    public void SetAdvancedReferenceDirectory(string path) { AdvancedReferenceDirectory = path ?? string.Empty; ReloadAdvancedFiles(); }
    public void SetAdvancedTargetDirectory(string path) { AdvancedTargetDirectory = path ?? string.Empty; ReloadAdvancedFiles(); }
    public void SetAdvancedReferencePath(string path) => SetAdvancedPath(path, true);
    public void SetAdvancedTargetPath(string path) => SetAdvancedPath(path, false);
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
    private static bool SupportedImage(string path) => new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool MatchesFilter(string path, string filter) => filter == "Semua gambar" || string.Equals(Path.GetExtension(path).TrimStart('.'), filter, StringComparison.OrdinalIgnoreCase);
    private List<RenamePreviewItem> LoadAdvancedFiles(string directory, string filter)
    {
        if (!Directory.Exists(directory)) return new();
        try { var option = AdvancedIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly; return Directory.EnumerateFiles(directory, "*.*", option).Where(SupportedImage).Where(f => MatchesFilter(f, filter)).Take(10000).OrderBy(f => f, StringComparer.OrdinalIgnoreCase.WithNaturalSort()).Select(f => new RenamePreviewItem { OriginalName = Path.GetFileName(f), OriginalPath = f, NewName = Path.GetFileName(f) }).ToList(); }
        catch { return new(); }
    }
    private void ReloadAdvancedFiles() { _advancedReferenceMaster = LoadAdvancedFiles(AdvancedReferenceDirectory, AdvancedReferenceFilter); _advancedTargetMaster = LoadAdvancedFiles(AdvancedTargetDirectory, AdvancedTargetFilter); RefreshAdvancedLists(); }
    private void RefreshAdvancedLists()
    {
        AdvancedReferenceFiles.Clear(); AdvancedTargetFiles.Clear();
        foreach (var item in _advancedReferenceMaster.Where(x => string.IsNullOrWhiteSpace(AdvancedReferenceSearch) || x.OriginalName.Contains(AdvancedReferenceSearch, StringComparison.OrdinalIgnoreCase)).Take(500)) AdvancedReferenceFiles.Add(item);
        foreach (var item in _advancedTargetMaster.Where(x => string.IsNullOrWhiteSpace(AdvancedTargetSearch) || x.OriginalName.Contains(AdvancedTargetSearch, StringComparison.OrdinalIgnoreCase)).Take(500)) AdvancedTargetFiles.Add(item);
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
        StatusText = $"Pasangan ditambahkan: {SelectedAdvancedReference.OriginalName} -> {result}";
    }
    [RelayCommand] private void RemoveAdvancedPair(AdvancedRenamePair? pair) { if (pair != null) AdvancedPairs.Remove(pair); }

    [RelayCommand] private void ToggleAdvancedMode() => IsAdvancedMode = !IsAdvancedMode;

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

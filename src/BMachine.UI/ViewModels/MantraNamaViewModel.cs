using System;
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

public partial class MantraNamaViewModel : ObservableObject
{
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

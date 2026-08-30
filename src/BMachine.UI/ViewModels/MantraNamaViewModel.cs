using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    public ObservableCollection<RenamePreviewItem> PreviewList { get; } = new();

    partial void OnSourceDirectoryChanged(string value) => RefreshPreview();
    partial void OnFindTextChanged(string value) => RefreshPreview();
    partial void OnReplaceTextChanged(string value) => RefreshPreview();
    partial void OnUseRegexChanged(bool value) => RefreshPreview();
    partial void OnCaseSensitiveChanged(bool value) => RefreshPreview();
    partial void OnMatchAllOccurrencesChanged(bool value) => RefreshPreview();
    partial void OnExcludeExtensionChanged(bool value) => RefreshPreview();

    private void RefreshPreview()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            StatusText = "Pilih folder sumber terlebih dahulu";
            PreviewList.Clear();
            return;
        }

        if (PreviewList.Count == 0)
        {
            // Initial load of files
            var files = Directory.GetFiles(SourceDirectory);
            foreach (var f in files)
            {
                PreviewList.Add(new RenamePreviewItem
                {
                    OriginalName = Path.GetFileName(f),
                    OriginalPath = f,
                    NewName = Path.GetFileName(f)
                });
            }
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
                foreach (var item in PreviewList)
                {
                    if (item.OriginalName == item.NewName) continue;
                    
                    var oldPath = item.OriginalPath;
                    var newPath = Path.Combine(SourceDirectory, item.NewName);

                    if (!File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                        count++;
                    }
                }
                
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    StatusText = $"Selesai. {count} file berhasil direname.";
                    PreviewList.Clear();
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

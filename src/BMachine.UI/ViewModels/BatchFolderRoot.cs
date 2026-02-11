using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using Avalonia.Threading;
using BMachine.UI.Messages;

namespace BMachine.UI.ViewModels;

/// <summary>
/// Represents a folder item in the batch queue.
/// </summary>
public partial class BatchFolderRoot : ObservableObject
{
    public string SourcePath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OutputHeader { get; set; } = "";
    public string OutputPath { get; set; } = "";
    
    [ObservableProperty] private string _newSubFolderName = "";

    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand CreateSubFolderCommand { get; }

    public BatchFolderRoot()
    {
        DeleteCommand = new RelayCommand(DeleteFolder);
        CreateSubFolderCommand = new RelayCommand(CreateSubFolder);
        CopyPathCommand = new RelayCommand(async () => await CopyPath());
        ExpandCommand = new RelayCommand(ToggleExpand);
    }

    [ObservableProperty] private bool _isExpanded = true;

    // Root is essentially a folder node wrapper
    [ObservableProperty]
    private BatchNodeItem? _sourceRoot;
    
    [ObservableProperty]
    private BatchNodeItem? _outputRoot;

    // We only need one Refresh mechanism now which re-creates the Root Node
    public void RefreshSource()
    {
        SourceRoot = new BatchNodeItem(SourcePath, true);
        SourceRoot.IsExpanded = true; 
    }
    
    // Watcher specific to this root folder's Output
    private FileSystemWatcher? _watcher;
    
    public void SetupOutputWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        
        RefreshOutput(); // Initial Load

        if (!string.IsNullOrEmpty(OutputPath) && Directory.Exists(OutputPath))
        {
             try
            {
                _watcher = new FileSystemWatcher(OutputPath);
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
                _watcher.IncludeSubdirectories = true; 
                _watcher.EnableRaisingEvents = true;

                // Simple refresh on change 
                void OnChange(object s, FileSystemEventArgs e) => 
                    Dispatcher.UIThread.InvokeAsync(RefreshOutput);
                
                _watcher.Created += OnChange;
                _watcher.Deleted += OnChange;
                _watcher.Renamed += OnChange;
            }
            catch {}
        }
    }

    public void RefreshOutput()
    {
        if (Directory.Exists(OutputPath))
        {
            OutputRoot = new BatchNodeItem(OutputPath, true);
            OutputRoot.IsExpanded = true; 
        }
        else
        {
            OutputRoot = null;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void DeleteFolder()
    {
        try
        {
            if (Directory.Exists(SourcePath))
            {
                Directory.Delete(SourcePath, true);
                // Notify logic to remove self
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new FolderDeletedMessage(new BatchNodeItem(SourcePath, true)));
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Error deleting root folder: {ex.Message}");
        }
    }

    private void CreateSubFolder()
    {
        if (string.IsNullOrWhiteSpace(NewSubFolderName)) return;

        try
        {
            var newPath = Path.Combine(SourcePath, NewSubFolderName);
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                NewSubFolderName = "";
                RefreshSource(); // Reload children
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Error creating subfolder in root: {ex.Message}");
        }
    }

    public IRelayCommand CopyPathCommand { get; }
    public IRelayCommand ExpandCommand { get; }

    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    private async Task CopyPath()
    {
         if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
         {
             var window = desktop.MainWindow;
             if (window?.Clipboard is not null)
             {
                 await window.Clipboard.SetTextAsync(SourcePath);
             }
         }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Avalonia.Threading;

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
}

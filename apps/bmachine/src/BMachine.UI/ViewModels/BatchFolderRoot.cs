using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using Avalonia;
using Avalonia.Threading;
using BMachine.UI.Messages;

namespace BMachine.UI.ViewModels;

/// <summary>
/// Represents a source folder in the Batch queue and its matching output folder.
/// </summary>
public partial class BatchFolderRoot : ObservableObject
{
    public string SourcePath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OutputHeader { get; set; } = "";
    public string OutputPath { get; set; } = "";

    [ObservableProperty] private string _newSubFolderName = "";
    [ObservableProperty] private bool _isNewFolderInputVisible;
    [ObservableProperty] private string _outputNewSubFolderName = "";
    [ObservableProperty] private bool _isOutputNewFolderInputVisible;

    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand DeleteOutputCommand { get; }
    public IRelayCommand CreateSubFolderCommand { get; }
    public IRelayCommand CreateOutputSubFolderCommand { get; }
    public IRelayCommand ShowNewFolderInputCommand { get; }
    public IRelayCommand CancelNewFolderInputCommand { get; }
    public IRelayCommand ShowOutputNewFolderInputCommand { get; }
    public IRelayCommand CancelOutputNewFolderInputCommand { get; }
    public IRelayCommand CopyPathCommand { get; }
    public IRelayCommand CopyOutputPathCommand { get; }
    public IRelayCommand ExpandCommand { get; }

    public BatchFolderRoot()
    {
        DeleteCommand = new RelayCommand(DeleteFolder);
        DeleteOutputCommand = new RelayCommand(DeleteOutputFolder);
        CreateSubFolderCommand = new RelayCommand(CreateSubFolder);
        CreateOutputSubFolderCommand = new RelayCommand(CreateOutputSubFolder);
        ShowNewFolderInputCommand = new RelayCommand(() => IsNewFolderInputVisible = true);
        CancelNewFolderInputCommand = new RelayCommand(CancelNewFolderInput);
        ShowOutputNewFolderInputCommand = new RelayCommand(() => IsOutputNewFolderInputVisible = true);
        CancelOutputNewFolderInputCommand = new RelayCommand(CancelOutputNewFolderInput);
        CopyPathCommand = new RelayCommand(async () => await CopyPath(SourcePath));
        CopyOutputPathCommand = new RelayCommand(async () => await CopyPath(OutputPath));
        ExpandCommand = new RelayCommand(ToggleExpand);
    }

    [ObservableProperty] private bool _isExpanded = true;

    // Root is essentially a folder node wrapper.
    [ObservableProperty]
    private BatchNodeItem? _sourceRoot;

    [ObservableProperty]
    private BatchNodeItem? _outputRoot;

    public void RefreshSource()
    {
        SourceRoot = new BatchNodeItem(SourcePath, true);
        SourceRoot.IsExpanded = true;
    }

    // Watcher specific to this root folder's Output.
    private FileSystemWatcher? _watcher;

    public void SetupOutputWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        RefreshOutput();

        if (!string.IsNullOrEmpty(OutputPath) && Directory.Exists(OutputPath))
        {
            try
            {
                _watcher = new FileSystemWatcher(OutputPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                void OnChange(object s, FileSystemEventArgs e) => Dispatcher.UIThread.InvokeAsync(RefreshOutput);
                _watcher.Created += OnChange;
                _watcher.Deleted += OnChange;
                _watcher.Renamed += OnChange;
            }
            catch { }
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
            if (!Directory.Exists(SourcePath)) return;
            Directory.Delete(SourcePath, true);
            WeakReferenceMessenger.Default.Send(new FolderDeletedMessage(new BatchNodeItem(SourcePath, true)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting source folder: {ex.Message}");
        }
    }

    private void DeleteOutputFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OutputPath) || !Directory.Exists(OutputPath)) return;
            _watcher?.Dispose();
            _watcher = null;
            Directory.Delete(OutputPath, true);
            RefreshOutput();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting output folder: {ex.Message}");
        }
    }

    private void CreateSubFolder()
    {
        if (string.IsNullOrWhiteSpace(NewSubFolderName)) return;
        try
        {
            var newPath = Path.Combine(SourcePath, NewSubFolderName.Trim());
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                CancelNewFolderInput();
                RefreshSource();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating subfolder in source root: {ex.Message}");
        }
    }

    private void CreateOutputSubFolder()
    {
        if (string.IsNullOrWhiteSpace(OutputNewSubFolderName) || string.IsNullOrWhiteSpace(OutputPath)) return;
        try
        {
            Directory.CreateDirectory(OutputPath);
            var newPath = Path.Combine(OutputPath, OutputNewSubFolderName.Trim());
            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
            CancelOutputNewFolderInput();
            SetupOutputWatcher();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating subfolder in output root: {ex.Message}");
        }
    }

    private void CancelNewFolderInput()
    {
        NewSubFolderName = "";
        IsNewFolderInputVisible = false;
    }

    private void CancelOutputNewFolderInput()
    {
        OutputNewSubFolderName = "";
        IsOutputNewFolderInputVisible = false;
    }

    private void ToggleExpand() => IsExpanded = !IsExpanded;

    private async Task CopyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard is not null)
            await desktop.MainWindow.Clipboard.SetTextAsync(path);
    }
}

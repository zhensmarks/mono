using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using Avalonia; // For Application
using Avalonia.Controls; // For TopLevel

namespace BMachine.UI.ViewModels;

/// <summary>
/// Represents a node in the file system tree (Folder or File).
/// </summary>
public partial class BatchNodeItem : ObservableObject
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<BatchNodeItem> _children = new();

    // Dummy item for lazy loading indicator
    private static readonly BatchNodeItem Dummy = new BatchNodeItem(true);
    
    /// <summary>
    /// Global filter for allowed extensions (e.g. .jpg, .png). 
    /// If null or empty, all files are valid (except system files).
    /// </summary>
    public static HashSet<string>? AllowedExtensions { get; set; }

    public IRelayCommand CopyPathCommand { get; }
    public IRelayCommand OpenTextCommand { get; }
    public IRelayCommand ExpandCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand CreateSubFolderCommand { get; }
    
    // Inline Action Bar Commands
    public IRelayCommand ToggleActionBarCommand { get; }
    public IRelayCommand ShowNewFolderInputCommand { get; }
    public IRelayCommand CancelActionCommand { get; }
    public IRelayCommand ConfirmNewFolderCommand { get; } // For "Enter" key or explicit button if needed
    public IRelayCommand<string> ShowMasterBrowserCommand { get; } // Param: "Left" or "Right"

    [ObservableProperty]
    private string _newSubFolderName = "";

    [ObservableProperty]
    private bool _isActionBarOpen;

    [ObservableProperty]
    private bool _isNewFolderInputVisible;

    public BatchNodeItem(string path, bool isDirectory)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(path)) Name = path; // Drive root case
        
        IsDirectory = isDirectory;

        if (IsDirectory)
        {
            // Add dummy item to enable expander for folders with content
             Children.Add(Dummy);
        }

        CopyPathCommand = new RelayCommand(async () => await CopyPath());
        OpenTextCommand = new RelayCommand(OpenText);
        ExpandCommand = new RelayCommand(ToggleExpand);
        DeleteCommand = new RelayCommand(DeleteItem); // Renamed from DeleteFolder to cover both
        CreateSubFolderCommand = new RelayCommand(CreateSubFolder);
        
        // Inline Action Bar
        ToggleActionBarCommand = new RelayCommand(() => IsActionBarOpen = !IsActionBarOpen);
        ShowNewFolderInputCommand = new RelayCommand(() => 
        {
            IsNewFolderInputVisible = true;
            IsActionBarOpen = false; // Hide action bar when input is shown
        });
        CancelActionCommand = new RelayCommand(() => 
        {
            IsActionBarOpen = false;
            IsNewFolderInputVisible = false;
            NewSubFolderName = "";
        });

        ConfirmNewFolderCommand = new RelayCommand(CreateSubFolder);
        ShowMasterBrowserCommand = new RelayCommand<string>(ShowMasterBrowser);
    }
    
    private void ShowMasterBrowser(string? side)
    {
        if (string.IsNullOrEmpty(side)) side = "Left"; // Default
        WeakReferenceMessenger.Default.Send(new OpenMasterBrowserMessage(this, side));
        IsActionBarOpen = false; // Close action bar
    }

    
    // Constructor for Dummy
    private BatchNodeItem(bool isDummy)
    {
        Name = "Loading...";
        IsDirectory = false;
        FullPath = "";
        CopyPathCommand = new RelayCommand(() => { });
        OpenTextCommand = new RelayCommand(() => { });
        ExpandCommand = new RelayCommand(() => { });
        DeleteCommand = new RelayCommand(() => { });
        CreateSubFolderCommand = new RelayCommand(() => { });
        ToggleActionBarCommand = new RelayCommand(() => { });
        ShowNewFolderInputCommand = new RelayCommand(() => { });
        CancelActionCommand = new RelayCommand(() => { });
        ConfirmNewFolderCommand = new RelayCommand(() => { });
    }

    private void ToggleExpand()
    {
        if (IsDirectory)
        {
             IsExpanded = !IsExpanded;
        }
    }

    async partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            await LoadChildren();
        }
    }

    public async Task LoadChildren()
    {
        if (!IsDirectory) return;

        // Verify if we need to load (if only dummy exists)
        if (Children.Count == 1 && Children[0] == Dummy)
        {
             // CLEAR on UI Thread
             await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Children.Clear());
             
             try
             {
                 var items = await Task.Run(() => 
                 {
                     var list = new List<BatchNodeItem>();
                     if (!Directory.Exists(FullPath)) return list;

                     var opts = new EnumerationOptions { IgnoreInaccessible = true };
                     
                     // Directores
                     foreach(var d in Directory.EnumerateDirectories(FullPath, "*", opts).OrderBy(x => x))
                     {
                         list.Add(new BatchNodeItem(d, true));
                     }
                     // Files
                     // Filter extensions if AllowedExtensions is set
                     IEnumerable<string> fileEnum = Directory.EnumerateFiles(FullPath, "*", opts)
                         .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
                     
                     if (AllowedExtensions != null && AllowedExtensions.Count > 0)
                     {
                         fileEnum = fileEnum.Where(f => AllowedExtensions.Contains(Path.GetExtension(f).ToLower()));
                     }
                         
                     foreach(var f in fileEnum.OrderBy(x => x))
                     {
                         list.Add(new BatchNodeItem(f, false));
                     }
                     return list;
                 });

                 // ADD on UI Thread
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                 {
                     foreach(var item in items) Children.Add(item);
                 });
             }
             catch 
             {
                 // Handle permission errors etc
             }
        }
    }

    private async Task CopyPath()
    {
         if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
         {
             var window = desktop.MainWindow;
             if (window?.Clipboard is not null)
             {
                 await window.Clipboard.SetTextAsync(FullPath);
             }
         }
    }

    private void OpenText()
    {
        if (IsDirectory) return;
        
        // Supported text extensions
        string[] textExts = { ".txt", ".json", ".xml", ".log", ".md", ".py", ".cs", ".js", ".jsx", ".csv", ".ini" };
        string ext = Path.GetExtension(FullPath).ToLower();
        
        if (textExts.Contains(ext))
        {
             // Send message to open in Log Panel
             WeakReferenceMessenger.Default.Send(new OpenTextFileMessage(FullPath));
        }
    }

    private void DeleteItem()
    {
        try
        {
            if (IsDirectory)
            {
                if (Directory.Exists(FullPath))
                {
                    Directory.Delete(FullPath, true);
                    WeakReferenceMessenger.Default.Send(new FolderDeletedMessage(this));
                }
            }
            else
            {
                if (File.Exists(FullPath))
                {
                    File.Delete(FullPath);
                    WeakReferenceMessenger.Default.Send(new FolderDeletedMessage(this)); // Reusing message for refresh
                }
            }
        }
        catch (System.Exception ex)
        {
             // Log error?
             System.Diagnostics.Debug.WriteLine($"Error deleting item: {ex.Message}");
        }
    }

    private void CreateSubFolder()
    {
        if (string.IsNullOrWhiteSpace(NewSubFolderName)) return;

        try
        {
            var newPath = Path.Combine(FullPath, NewSubFolderName);
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                
                // Reset UI State
                NewSubFolderName = ""; 
                IsNewFolderInputVisible = false;
                IsActionBarOpen = false;
                
                // If Expanded, refresh children to show new folder
                if (IsExpanded) 
                {
                    _ = LoadChildren();
                }
                else
                {
                    IsExpanded = true; // This will trigger LoadChildren
                }
            }
        }
        catch (System.Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Error creating subfolder: {ex.Message}");
        }
    }
}

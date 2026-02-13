using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.SDK;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;

namespace BMachine.UI.ViewModels;

public partial class OutputExplorerViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly Services.FileOperationManager _fileManager;

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _items = new();
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private bool _canNavigateUp;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isRootPathMissing;
    
    // Task Monitor
    public ObservableCollection<Services.FileTaskItem> ActiveTasks => _fileManager.ActiveTasks;

    public string RootPath { get; private set; } = "";

    public OutputExplorerViewModel(IDatabase database, INotificationService notificationService, Services.FileOperationManager fileManager)
    {
        _database = database;
        _notificationService = notificationService;
        _fileManager = fileManager;
        
        LoadRootPath();
        
        // Listen for path changes
        WeakReferenceMessenger.Default.Register<MasterPathsChangedMessage>(this, (r, m) => 
        {
            // Ensure UI Thread
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(LoadRootPath);
        });
    }

    // Resilience
    [ObservableProperty] private Avalonia.Controls.GridLength _taskMonitorHeight = Avalonia.Controls.GridLength.Auto;

    partial void OnTaskMonitorHeightChanged(Avalonia.Controls.GridLength value)
    {
        if (value.IsAbsolute)
        {
            _database.SetAsync("Configs.Explorer.TaskMonitorHeight", value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
             _database.SetAsync("Configs.Explorer.TaskMonitorHeight", "Auto");
        }
    }

    [RelayCommand]
    public void CancelTask(Services.FileTaskItem task)
    {
        _fileManager.CancelTask(task);
    }

    [RelayCommand]
    public void ClearCompleted()
    {
        _fileManager.ClearCompletedTasks();
    }

    private async void LoadRootPath()
    {
        var path = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";
        var savedHeightStr = await _database.GetAsync<string>("Configs.Explorer.TaskMonitorHeight");
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (string.Equals(savedHeightStr, "Auto", System.StringComparison.OrdinalIgnoreCase))
            {
                TaskMonitorHeight = Avalonia.Controls.GridLength.Auto;
            }
            else if (double.TryParse(savedHeightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double savedHeight) && savedHeight > 0)
            {
                TaskMonitorHeight = new Avalonia.Controls.GridLength(savedHeight);
            }

            RootPath = path;
            if (string.IsNullOrEmpty(RootPath))
            {
                // Fallback or empty state
                CurrentPath = "";
                Items.Clear();
                IsRootPathMissing = true;
                IsEmpty = true;
                return;
            }
            if (!Directory.Exists(RootPath))
            {
                 // Directory not found
                 CurrentPath = "";
                 Items.Clear();
                 IsRootPathMissing = true;
                 IsEmpty = true;
                 return;
            }
    
            IsRootPathMissing = false;
            NavigateTo(RootPath);
        });
    }

    [RelayCommand]
    private void NavigateTo(string path)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            
            CurrentPath = path;
            UpdateBreadcrumbs();
            LoadItems();
            CanNavigateUp = IsSubfolderOf(CurrentPath, RootPath);
        });
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (!CanNavigateUp) return;
        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
        {
            NavigateTo(parent.FullName);
        }
    }

    public enum ExplorerSortOption { Name, Date }

    [ObservableProperty] private ExplorerSortOption _sortBy = ExplorerSortOption.Name;
    [ObservableProperty] private bool _isSortDescending = false;

    partial void OnSortByChanged(ExplorerSortOption value) => LoadItems();
    partial void OnIsSortDescendingChanged(bool value) => LoadItems();

    [RelayCommand]
    private void ToggleSort(ExplorerSortOption option)
    {
        if (SortBy == option)
        {
            IsSortDescending = !IsSortDescending;
        }
        else
        {
            SortBy = option;
            IsSortDescending = false; // Reset to Ascending for new sort
        }
    }

    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _selectedItems = new();

    partial void OnSelectedItemsChanged(ObservableCollection<ExplorerItemViewModel> value)
    {
        // Optional: Update commands state if needed
    }

    [RelayCommand]
    public void OpenItem(object parameter)
    {
        if (parameter is ExplorerItemViewModel item)
        {
            if (item.IsDirectory)
            {
                NavigateTo(item.FullPath);
            }
            else
            {
                OpenFileOrShortcut(item.FullPath);
            }
        }
    }

    private void OpenFileOrShortcut(string path)
    {
         // Check if .lnk
        if (Path.GetExtension(path).Equals(".lnk", System.StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                    // Use ShellExecute to let Windows handle the shortcut (open target)
                    var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
                    Process.Start(psi);
                    return;
            }
            catch { }
        }

        // Open file logic (default)
        try 
        {
            var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
            Process.Start(psi);
        }
        catch { }
    }

    [RelayCommand]
    public void MoveToOke(object? parameter)
    {
        var itemsToProcess = GetSelectedItems(parameter);
        if (!itemsToProcess.Any()) return;

        // Logic to resolve destination (e.g., #OKE shortcut or current month folder)
        // For now, let's assume we find a shortcut named "#OKE" in the current directory or parent
        // Or simply ask user? 
        // The requirement said: "Search for .lnk shortcuts matching #OKE"
        
        var destination = ResolveOkeDestination();
        if (string.IsNullOrEmpty(destination))
        {
            _notificationService.ShowError("Could not find #OKE destination.", "Error");
            return;
        }

        foreach (var item in itemsToProcess)
        {
            _fileManager.MoveFileBackground(item.FullPath, Path.Combine(destination, item.Name));
        }
    }

    [RelayCommand]
    public void CopyToOke(object? parameter)
    {
        var itemsToProcess = GetSelectedItems(parameter);
        if (!itemsToProcess.Any()) return;

        var destination = ResolveOkeDestination();
         if (string.IsNullOrEmpty(destination))
        {
            _notificationService.ShowError("Could not find #OKE destination.", "Error");
            return;
        }

        foreach (var item in itemsToProcess)
        {
            _fileManager.CopyFileBackground(item.FullPath, Path.Combine(destination, item.Name));
        }
    }

    private System.Collections.Generic.List<ExplorerItemViewModel> GetSelectedItems(object? parameter)
    {
        var list = new System.Collections.Generic.List<ExplorerItemViewModel>();
        
        // If parameter is a single item (context menu click)
        if (parameter is ExplorerItemViewModel singleItem)
        {
            // If the clicked item is NOT in the current selection, process ONLY this item
            if (!SelectedItems.Contains(singleItem))
            {
                list.Add(singleItem);
            }
            else
            {
                // If it IS in selection, process all selected
                list.AddRange(SelectedItems);
            }
        }
        // If parameter is null, assume button click -> process all selected
        else if (SelectedItems.Any())
        {
            list.AddRange(SelectedItems);
        }
        
        return list;
    }

    private string ResolveOkeDestination()
    {
        try
        {
            // 1. Search for #OKE*.lnk in current path (Priority)
            if (Directory.Exists(CurrentPath))
            {
                var lnk = Directory.GetFiles(CurrentPath, "#OKE*.lnk").FirstOrDefault();
                if (lnk != null)
                {
                    try 
                    {
                        var target = ResolveShortcutTarget(lnk);
                        if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                        {
                            return target;
                        }
                    } 
                    catch { /* Failed to resolve lnk */ }
                }
            }
            
            // 2. Search for #OKE*.lnk in PARENT path
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                 var lnk = Directory.GetFiles(parent.FullName, "#OKE*.lnk").FirstOrDefault();
                 if (lnk != null)
                 {
                    try 
                    {
                        var target = ResolveShortcutTarget(lnk);
                        if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                        {
                            return target;
                        }
                    } 
                    catch { /* Failed to resolve lnk */ }
                 }
            }
            
            // 3. Fallback: Search for folder #OKE in CURRENT path (Legacy/Backup)
            var dir = Directory.GetDirectories(CurrentPath, "#OKE*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dir != null) return dir;

            // 4. Fallback: Search for #OKE in PARENT path
            if (parent != null)
            {
                 var parentDir = Directory.GetDirectories(parent.FullName, "#OKE*", SearchOption.TopDirectoryOnly).FirstOrDefault();
                 if (parentDir != null) return parentDir;
            }

            // 5. Search in ROOT Path (Folder or Lnk)
            if (!string.IsNullOrEmpty(RootPath) && Directory.Exists(RootPath))
            {
                 // Check LNK in Root
                 var rootLnk = Directory.GetFiles(RootPath, "#OKE*.lnk").FirstOrDefault();
                 if (rootLnk != null)
                 {
                      var target = ResolveShortcutTarget(rootLnk);
                      if (!string.IsNullOrEmpty(target) && Directory.Exists(target)) return target;
                 }

                 // Check Folder in Root
                 var rootDir = Directory.GetDirectories(RootPath, "#OKE*", SearchOption.TopDirectoryOnly).FirstOrDefault();
                 if (rootDir != null) return rootDir;
            }

        } catch {}

        return "";
    }

    private string ResolveShortcutTarget(string shortcutPath)
    {
        // Resolving .lnk without COM reference (IWshRuntimeLibrary) using a temp VBScript
        // This is a robust way to avoid adding COM dependencies to the project
        try
        {
            if (!File.Exists(shortcutPath)) return "";

            string tempVbs = Path.Combine(Path.GetTempPath(), $"resolve_lnk_{System.Guid.NewGuid()}.vbs");
            string script = $@"
                Set wshShell = CreateObject(""WScript.Shell"")
                Set sc = wshShell.CreateShortcut(""{shortcutPath}"")
                WScript.Echo sc.TargetPath
            ";
            
            File.WriteAllText(tempVbs, script);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cscript",
                Arguments = $"//Nologo \"{tempVbs}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                string target = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                try { File.Delete(tempVbs); } catch {}
                return target;
            }
            
            try { File.Delete(tempVbs); } catch {}
        }
        catch { }
        return "";
    }

    private void LoadItems()
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Items.Clear();
            try
            {
                if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;

                var dirInfo = new DirectoryInfo(CurrentPath);
                var dirs = dirInfo.GetDirectories();
                var files = dirInfo.GetFiles();

                System.Collections.Generic.IEnumerable<ExplorerItemViewModel> dirItems = dirs.Select(d => new ExplorerItemViewModel 
                { 
                    Name = d.Name, 
                    FullPath = d.FullName, 
                    IsDirectory = true,
                    DateModified = d.LastWriteTime,
                    ItemsCount = 0 // Optimization: don't count items for speed
                });

                System.Collections.Generic.IEnumerable<ExplorerItemViewModel> fileItems = files.Select(f => new ExplorerItemViewModel 
                { 
                    Name = f.Name, 
                    FullPath = f.FullName, 
                    IsDirectory = false, // True for .lnk? No, treat as file, OpenItem handles it
                    DateModified = f.LastWriteTime,
                    Size = f.Length
                });

                // Sorting
                System.Func<ExplorerItemViewModel, object> keySelector = SortBy switch
                {
                    ExplorerSortOption.Date => x => x.DateModified,
                    _ => x => x.Name
                };

                if (IsSortDescending)
                {
                    dirItems = dirItems.OrderByDescending(keySelector);
                    fileItems = fileItems.OrderByDescending(keySelector);
                }
                else
                {
                    dirItems = dirItems.OrderBy(keySelector);
                    fileItems = fileItems.OrderBy(keySelector);
                }

                // Add Dirs then Files
                foreach (var i in dirItems) Items.Add(i);
                foreach (var i in fileItems) Items.Add(i);
            }
            catch { }
            
            IsEmpty = Items.Count == 0;
        });
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        // Simply split path? Or start from RootPath?
        // Let's show full path parts for now or relative to Root
        
        var parts = CurrentPath.Split(Path.DirectorySeparatorChar);
        var accumulatingPath = "";
        
        // Need to handle drive letter correctly on Windows
        if (CurrentPath.Contains(":"))
        {
             // Simple hack for now
             var drive = parts[0];
             accumulatingPath = drive;
             Breadcrumbs.Add(new BreadcrumbItem(drive, drive + Path.DirectorySeparatorChar));
             
             for (int i=1; i<parts.Length; i++)
             {
                 accumulatingPath = Path.Combine(accumulatingPath, parts[i]);
                 Breadcrumbs.Add(new BreadcrumbItem(parts[i], accumulatingPath));
             }
        }
    }

    private bool IsSubfolderOf(string path, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return p.StartsWith(r, System.StringComparison.OrdinalIgnoreCase) && p.Length > r.Length;
    }
}

public partial class ExplorerItemViewModel : ObservableObject
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public System.DateTime DateModified { get; set; }
    public long Size { get; set; } // Bytes
    public int ItemsCount { get; set; }
    
    public string IconKey => IsDirectory ? "IconFolder" : "IconFile";
    public string DisplaySize => IsDirectory ? $"{ItemsCount} items" : BytesToString(Size);
    
    // Helper
    private string BytesToString(long byteCount)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
        if (byteCount == 0)
            return "0" + suf[0];
        long bytes = System.Math.Abs(byteCount);
        int place = System.Convert.ToInt32(System.Math.Floor(System.Math.Log(bytes, 1024)));
        double num = System.Math.Round(bytes / System.Math.Pow(1024, place), 1);
        return (System.Math.Sign(byteCount) * num).ToString() + " " + suf[place];
    }
}

public record BreadcrumbItem(string Name, string Path);

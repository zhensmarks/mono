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

using BMachine.UI.Messages;
using Avalonia.Controls.ApplicationLifetimes; // Added for Clipboard access

namespace BMachine.UI.ViewModels;

public partial class OutputExplorerViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly Services.FileOperationManager _fileManager;

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private ObservableCollection<object> _items = new(); // Changed to object to support Headers
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private bool _canNavigateUp;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isRootPathMissing;

    private readonly System.Collections.Generic.Stack<string> _backStack = new();
    private readonly System.Collections.Generic.Stack<string> _forwardStack = new();
    private bool _isNavigatingHistory;
    
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

    // Sidebar Width Persistence
    [ObservableProperty] private Avalonia.Controls.GridLength _sidebarWidth = new Avalonia.Controls.GridLength(120);

    partial void OnSidebarWidthChanged(Avalonia.Controls.GridLength value)
    {
        if (value.IsAbsolute)
        {
            _database.SetAsync("Configs.Explorer.SidebarWidth", value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    
    // Quick Access / Sidebar Items
    [ObservableProperty] private ObservableCollection<SidebarItemViewModel> _quickAccessItems = new();
    
    [RelayCommand]
    public async Task AddToQuickAccess(object? parameter)
    {
        if (parameter is ExplorerItemViewModel item && item.IsDirectory)
        {
             // Determine if Dynamic (Relative) or Static (Absolute)
             string pathToAdd = item.FullPath;
             bool isDynamic = IsSubfolderOf(pathToAdd, RootPath);
             string storedPath = isDynamic ? Path.GetRelativePath(RootPath, pathToAdd) : pathToAdd;
             
             var newItem = new SidebarItemViewModel 
             { 
                 Name = item.Name, 
                 Path = storedPath, 
                 IsDynamic = isDynamic,
                 Icon = "IconFolder"
             };
             
             QuickAccessItems.Add(newItem);
             await SaveQuickAccessAsync();
             _notificationService.ShowSuccess($"Pinned {item.Name} to sidebar");
        }
    }

    [RelayCommand]
    public async Task RemoveFromQuickAccess(SidebarItemViewModel item)
    {
        if (QuickAccessItems.Contains(item))
        {
            QuickAccessItems.Remove(item);
            await SaveQuickAccessAsync();
        }
    }
    
    [RelayCommand]
    public void NavigateToQuickAccess(SidebarItemViewModel item)
    {
        string targetPath = item.IsDynamic ? Path.Combine(RootPath, item.Path) : item.Path;
        if (Directory.Exists(targetPath))
        {
            NavigateTo(targetPath);
        }
        else
        {
            _notificationService.ShowError($"Folder not found: {targetPath}");
        }
    }

    private async Task SaveQuickAccessAsync()
    {
        var data = QuickAccessItems.Select(x => new SidebarItemDto { Name = x.Name, Path = x.Path, IsDynamic = x.IsDynamic, Icon = x.Icon }).ToList();
        await _database.SetAsync("Configs.Explorer.QuickAccess", data);
    }
    
    private async Task LoadQuickAccessAsync()
    {
        var data = await _database.GetAsync<System.Collections.Generic.List<SidebarItemDto>>("Configs.Explorer.QuickAccess");
        if (data != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                QuickAccessItems.Clear();
                foreach(var d in data)
                {
                    QuickAccessItems.Add(new SidebarItemViewModel 
                    { 
                        Name = d.Name, 
                        Path = d.Path, 
                        IsDynamic = d.IsDynamic, 
                        Icon = d.Icon 
                    });
                }
            });
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
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (string.Equals(savedHeightStr, "Auto", System.StringComparison.OrdinalIgnoreCase))
            {
                TaskMonitorHeight = Avalonia.Controls.GridLength.Auto;
            }
            else if (double.TryParse(savedHeightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double savedHeight) && savedHeight > 0)
            {
                TaskMonitorHeight = new Avalonia.Controls.GridLength(savedHeight);
            }
            
            // Load Sidebar Width
            var savedSidebarWidthStr = await _database.GetAsync<string>("Configs.Explorer.SidebarWidth");
            if (double.TryParse(savedSidebarWidthStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double savedWidth) && savedWidth > 0)
            {
                SidebarWidth = new Avalonia.Controls.GridLength(savedWidth);
            }

            // Load Quick Access
            await LoadQuickAccessAsync();

            RootPath = path;
            if (string.IsNullOrEmpty(RootPath) || !Directory.Exists(RootPath))
            {
                // Fallback or empty state
                CurrentPath = "";
                Items.Clear();
                IsRootPathMissing = true;
                IsEmpty = true;
                return;
            }
    
            IsRootPathMissing = false;
            
            // Navigate with settings load
            CurrentPath = RootPath;
            UpdateBreadcrumbs();
            await LoadViewSettingsForPath(CurrentPath);
            
            CanNavigateUp = false; // Root
            CanGoBack = false; 
            CanGoForward = false;
        });
    }

    [RelayCommand]
    public async Task AddExternalFolder()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var folder = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Folder to Pin",
                AllowMultiple = false
            });

            if (folder != null && folder.Any())
            {
                var targetPath = folder[0].Path.LocalPath;
                
                // Reuse existing logic
                bool isDynamic = IsSubfolderOf(targetPath, RootPath);
                string storedPath = isDynamic ? Path.GetRelativePath(RootPath, targetPath) : targetPath;

                var newItem = new SidebarItemViewModel 
                { 
                    Name = Path.GetFileName(targetPath), 
                    Path = storedPath, 
                    IsDynamic = isDynamic,
                    Icon = "IconFolder"
                };

                QuickAccessItems.Add(newItem);
                await SaveQuickAccessAsync();
                _notificationService.ShowSuccess($"Pinned {newItem.Name}");
            }
        }
    }



    [RelayCommand]
    private void NavigateTo(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (CurrentPath == path) return;

        // Manual navigation: push current to back, clear forward
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
        }

        ExecuteNavigation(path);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_backStack.Count > 0)
        {
            var destination = _backStack.Pop();
            _forwardStack.Push(CurrentPath);
            ExecuteNavigation(destination);
        }
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_forwardStack.Count > 0)
        {
            var destination = _forwardStack.Pop();
            _backStack.Push(CurrentPath);
            ExecuteNavigation(destination);
        }
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (!CanNavigateUp) return;
        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
        {
            // Up is manual navigation: push back, clear forward
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
            ExecuteNavigation(parent.FullName);
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        LoadItems();
    }

    private void ExecuteNavigation(string path)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            CurrentPath = path;
            UpdateBreadcrumbs();
            
            // Load Settings for this specific path (or global defaults)
            await LoadViewSettingsForPath(CurrentPath);
            
            CanNavigateUp = IsSubfolderOf(CurrentPath, RootPath);
            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;
        });
    }

    public enum ExplorerSortOption { Name, Date }
    public enum ExplorerLayoutMode { Vertical, Horizontal }
    public enum ExplorerGroupBy { None, Date, Type }

    [ObservableProperty] private ExplorerSortOption _sortBy = ExplorerSortOption.Name;
    [ObservableProperty] private bool _isSortDescending = false;
    [ObservableProperty] private ExplorerLayoutMode _layoutMode = ExplorerLayoutMode.Vertical;
    [ObservableProperty] private ExplorerGroupBy _groupBy = ExplorerGroupBy.Date; // Default to Date per user request
    [ObservableProperty] private bool _isLocalSettings = false; // "Only" Checkbox

    // DTO for Local Settings
    public class FolderViewSettingsDto
    {
        public string SortBy { get; set; } = "Name";
        public bool IsSortDescending { get; set; }
        public string LayoutMode { get; set; } = "Vertical";
        public string GroupBy { get; set; } = "Date";
    }

    partial void OnSortByChanged(ExplorerSortOption value) 
    {
        LoadItems();
        SaveViewSettings();
    }

    partial void OnIsSortDescendingChanged(bool value) 
    {
        LoadItems();
        SaveViewSettings();
    }

    partial void OnGroupByChanged(ExplorerGroupBy value) 
    {
        LoadItems();
        SaveViewSettings();
    }
    
    partial void OnLayoutModeChanged(ExplorerLayoutMode value) 
    {
        OnLayoutChanged_UI();
        SaveViewSettings();
    }

    partial void OnIsLocalSettingsChanged(bool value)
    {
        SaveViewSettings();
    }

    private async void SaveViewSettings()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        if (IsLocalSettings)
        {
            // Save ONLY for this folder
            var settings = new FolderViewSettingsDto
            {
                SortBy = SortBy.ToString(),
                IsSortDescending = IsSortDescending,
                LayoutMode = LayoutMode.ToString(),
                GroupBy = GroupBy.ToString()
            };
            // Use a specific key for this folder's settings. Hashing path might be safer against special chars.
            // But simple key replacement works for now. 
            // We use a prefix "Configs.Explorer.FolderSettings." + Path
            // NOTE: Path can be long/invalid chars for some DB keys? 
            // Assuming DB can handle arbitrary string keys or we encode. 
            // Let's use Base64 of path to be safe.
            var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CurrentPath.ToLowerInvariant()));
            await _database.SetAsync($"Configs.Explorer.FolderSettings.{pathKey}", settings);
        }
        else
        {
            // Save as Global Defaults
            // Remove local setting if exists? Or just update globals? 
            // If user explicitly unchecked "Only", we should probably DELETE local setting so it reverts to global next time?
            // Logic: If transitioning from Local -> Global (Unchecked), delete local.
            // But here we are just saving whatever the current mode is. 
            
            // Check if we just unchecked it? value is already updated.
            // If IsLocalSettings is FALSE, we verify if successful deletion of local key is needed?
            // Actually, `ToggleLocalSettings` sets IsLocalSettings then calls this. 
            // So if FALSE, likely we want to remove local override.
            
            var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CurrentPath.ToLowerInvariant()));
            await _database.DeleteAsync($"Configs.Explorer.FolderSettings.{pathKey}");

            // Save Globals as Strings to satisfy generic constraint 'where T : class'
            await _database.SetAsync("Configs.Explorer.SortBy", SortBy.ToString());
            await _database.SetAsync("Configs.Explorer.IsSortDescending", IsSortDescending.ToString());
            await _database.SetAsync("Configs.Explorer.ViewMode", LayoutMode.ToString());
            await _database.SetAsync("Configs.Explorer.GroupBy", GroupBy.ToString());
        }
    }
    
    private async Task LoadViewSettingsForPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        var localSettings = await _database.GetAsync<FolderViewSettingsDto>($"Configs.Explorer.FolderSettings.{pathKey}");

        if (localSettings != null)
        {
            // Apply Local
            _isLocalSettings = true; 
            OnPropertyChanged(nameof(IsLocalSettings));
            
            if (System.Enum.TryParse<ExplorerSortOption>(localSettings.SortBy, out var s)) _sortBy = s;
            _isSortDescending = localSettings.IsSortDescending;
            if (System.Enum.TryParse<ExplorerLayoutMode>(localSettings.LayoutMode, out var l)) _layoutMode = l;
            if (System.Enum.TryParse<ExplorerGroupBy>(localSettings.GroupBy, out var g)) _groupBy = g;
            
            OnPropertyChanged(nameof(SortBy));
            OnPropertyChanged(nameof(IsSortDescending));
            OnPropertyChanged(nameof(LayoutMode));
            OnPropertyChanged(nameof(GroupBy));
            OnLayoutChanged_UI();
        }
        else
        {
            // Apply Global Defaults
            _isLocalSettings = false;
            OnPropertyChanged(nameof(IsLocalSettings));

            // Load Globals (Stored as Strings to satisfy 'where T : class')
            var gSort = await _database.GetAsync<string>("Configs.Explorer.SortBy");
            var gDescStr = await _database.GetAsync<string>("Configs.Explorer.IsSortDescending");
            var gMode = await _database.GetAsync<string>("Configs.Explorer.ViewMode");
            var gGroup = await _database.GetAsync<string>("Configs.Explorer.GroupBy");

            if (!string.IsNullOrEmpty(gSort) && System.Enum.TryParse<ExplorerSortOption>(gSort, out var s)) _sortBy = s;
            else _sortBy = ExplorerSortOption.Name;

            if (!string.IsNullOrEmpty(gDescStr) && bool.TryParse(gDescStr, out var d)) _isSortDescending = d;
            else _isSortDescending = false; // Default

            if (!string.IsNullOrEmpty(gMode) && System.Enum.TryParse<ExplorerLayoutMode>(gMode, out var l)) _layoutMode = l;
            else _layoutMode = ExplorerLayoutMode.Vertical;

            if (!string.IsNullOrEmpty(gGroup) && System.Enum.TryParse<ExplorerGroupBy>(gGroup, out var g)) _groupBy = g;
            else _groupBy = ExplorerGroupBy.Date;

            OnPropertyChanged(nameof(SortBy));
            OnPropertyChanged(nameof(IsSortDescending));
            OnPropertyChanged(nameof(LayoutMode));
            OnPropertyChanged(nameof(GroupBy));
            OnLayoutChanged_UI();
        }
        // Finally load items
        LoadItems();
    }

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
            IsSortDescending = false; 
        }
    }
    
    // Quick helpers for View Binding
    public bool IsVerticalLayout => LayoutMode == ExplorerLayoutMode.Vertical;
    public bool IsHorizontalLayout => LayoutMode == ExplorerLayoutMode.Horizontal;
    
    public bool IsVerticalLayoutAndNotEmpty => IsVerticalLayout && !IsEmpty;
    public bool IsHorizontalLayoutAndNotEmpty => IsHorizontalLayout && !IsEmpty;

    
    // partial void OnLayoutModeChanged(ExplorerLayoutMode value) // Removed duplicate partial method
    // {
    //    OnLayoutChanged_UI();
    // }
    
    private void OnLayoutChanged_UI()
    {
        OnPropertyChanged(nameof(IsVerticalLayout));
        OnPropertyChanged(nameof(IsHorizontalLayout));
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
        LoadItems_UIOnly();
    }
    
    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
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
                // If it's a shortcut to a folder, navigate to TargetPath
                var path = !string.IsNullOrEmpty(item.TargetPath) ? item.TargetPath : item.FullPath;
                NavigateTo(path);
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
        
        // Smart Photoshop Open for Images
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (new[] { ".jpg", ".jpeg", ".png", ".psd", ".tif", ".tiff", ".webp" }.Contains(ext))
        {
            OpenInPhotoshopSmart(path);
            return;
        }

        // Open file logic (default)
        try 
        {
            var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
            Process.Start(psi);
        }
        catch { }
    }

    private void OpenInPhotoshopSmart(string imagePath)
    {
        try
        {
             string tempJsx = Path.Combine(Path.GetTempPath(), $"bmachine_smart_open_{System.Guid.NewGuid()}.jsx");
             string escapedPath = imagePath.Replace("\\", "\\\\");
             
             // JSX Logic: check docs, if exists -> place, else -> open
             string script = $@"
try {{
    var fileRef = new File(""{escapedPath}"");
    if (app.documents.length > 0) {{
        try {{
            app.activeDocument.placeEmbedded(fileRef);
        }} catch(e) {{
            // Fallback if placement fails (e.g. no active layer/doc issue)
            app.open(fileRef);
        }}
    }} else {{
        app.open(fileRef);
    }}
}} catch(e) {{
    alert('BMachine Error: ' + e);
}}
";
             File.WriteAllText(tempJsx, script);
             
             var psi = new ProcessStartInfo { FileName = tempJsx, UseShellExecute = true };
             Process.Start(psi);
             
             // Cleanup handled by OS temp or next run... we can't delete immediately as PS needs to read it.
             // Maybe dispatch a delayed delete?
             Task.Delay(5000).ContinueWith(_ => 
             {
                 try { if(File.Exists(tempJsx)) File.Delete(tempJsx); } catch { }
             });
        }
        catch (System.Exception ex)
        {
             _notificationService.ShowError($"Failed to open in Photoshop: {ex.Message}", "Error");
             // Fallback to default open
             try { Process.Start(new ProcessStartInfo { FileName = imagePath, UseShellExecute = true }); } catch { }
        }
    }

    [RelayCommand]
    public void MoveToOke(object? parameter)
    {
        var itemsToProcess = GetSelectedItems(parameter);
        if (!itemsToProcess.Any()) return;

        // Logic to resolve destination (e.g., #OKE shortcut or current month folder)
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

    [RelayCommand]
    public async Task CopyItem(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;
        
        var paths = items.Select(x => x.FullPath).ToArray();
        var data = new Avalonia.Input.DataObject();
        data.Set(Avalonia.Input.DataFormats.FileNames, paths);
        
        // Fix: Access Clipboard via ApplicationLifetime
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
             var clipboard = desktop.MainWindow?.Clipboard;
             if (clipboard != null)
             {
                 await clipboard.SetDataObjectAsync(data);
                 _notificationService.ShowSuccess($"Copied {items.Count} items to clipboard");
             }
        }
    }

    [RelayCommand]
    public void DeleteItem(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        // Simple delete for now, maybe add confirmation dialog later if requested specifically
        int deletedCount = 0;
        foreach(var item in items)
        {
            try 
            {
                if(item.IsDirectory) Directory.Delete(item.FullPath, true);
                else File.Delete(item.FullPath);
                deletedCount++;
            }
            catch(System.Exception ex)
            {
                 _notificationService.ShowError($"Failed to delete {item.Name}: {ex.Message}");
            }
        }
        
        if (deletedCount > 0)
        {
            _notificationService.ShowSuccess($"Deleted {deletedCount} items");
            LoadItems(); // Refresh
        }
    }

    private System.Collections.Generic.List<ExplorerItemViewModel> GetSelectedItems(object? parameter)
    {
        var list = new System.Collections.Generic.List<ExplorerItemViewModel>();
        
        if (parameter is ExplorerItemViewModel singleItem)
        {
            if (!SelectedItems.Contains(singleItem))
            {
                list.Add(singleItem);
            }
            else
            {
                list.AddRange(SelectedItems);
            }
        }
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

    private void LoadItems_UIOnly()
    {
         // Placeholder
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
                    ItemsCount = 0 
                });

                // Files mapping with LNK check
                var fileItemList = new System.Collections.Generic.List<ExplorerItemViewModel>();
                foreach(var f in files)
                {
                    bool isLnk = f.Extension.Equals(".lnk", System.StringComparison.OrdinalIgnoreCase);
                    bool treatAsDir = false;
                    string target = "";

                    if (isLnk)
                    {
                        try 
                        {
                            target = ResolveShortcutTarget(f.FullName);
                            if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                            {
                                treatAsDir = true;
                            }
                        }
                        catch {}
                    }

                    fileItemList.Add(new ExplorerItemViewModel 
                    { 
                        Name = f.Name, 
                        FullPath = f.FullName, 
                        IsDirectory = treatAsDir,
                        TargetPath = target,
                        DateModified = f.LastWriteTime,
                        Size = f.Length
                    });
                }

                System.Collections.Generic.IEnumerable<ExplorerItemViewModel> baseItems = dirItems.Concat(fileItemList);

                // Grouping & Sorting logic
                if (GroupBy == ExplorerGroupBy.None)
                {
                     // Original Logic
                    System.Func<ExplorerItemViewModel, object> keySelector = SortBy switch
                    {
                        ExplorerSortOption.Date => x => x.DateModified,
                        _ => x => x.Name
                    };
                    
                    if (IsSortDescending) baseItems = baseItems.OrderByDescending(keySelector);
                    else baseItems = baseItems.OrderBy(keySelector);
                    
                    // Simple folder-first logic embedded? or strictly follow sort?
                    // Usually folders are first relative to files in Windows, unless sorted strictly by date mixed.
                    // Let's keep separate: Dirs, then Files (unless pure date sort requested?)
                    // Current request: Group By Modified is priority.
                    
                    // If SortBy is Name, keep Folders First.
                    if (SortBy == ExplorerSortOption.Name)
                    {
                         // Split and sort separately
                         var sortedDirs = IsSortDescending ? dirItems.OrderByDescending(x => x.Name) : dirItems.OrderBy(x => x.Name);
                         var sortedFiles = IsSortDescending ? fileItemList.Where(x => !x.IsDirectory).OrderByDescending(x => x.Name) : fileItemList.Where(x => !x.IsDirectory).OrderBy(x => x.Name);
                         // LNK treated as dir are in fileItems list but marked IsDirectory... wait.
                         // Correct: fileItemList contains lnk-dirs.
                         
                         var allDirs = baseItems.Where(x => x.IsDirectory);
                         var allFiles = baseItems.Where(x => !x.IsDirectory);
                         
                         if(IsSortDescending) 
                         {
                             allDirs = allDirs.OrderByDescending(x => x.Name);
                             allFiles = allFiles.OrderByDescending(x => x.Name);
                         }
                         else 
                         {
                             allDirs = allDirs.OrderBy(x => x.Name);
                             allFiles = allFiles.OrderBy(x => x.Name);
                         }
                         
                         foreach (var i in allDirs) Items.Add(i);
                         foreach (var i in allFiles) Items.Add(i);
                    }
                    else
                    {
                         // Sort by Date Mixed
                         foreach (var i in baseItems) Items.Add(i);
                    }
                }
                else if (GroupBy == ExplorerGroupBy.Date)
                {
                    // Date Grouping (Today, Yesterday, etc.)
                    // First, sort by Date Descending always for grouping usually, or match SortBy?
                    // User usually expects "Today" at top.
                    
                    var grouped = baseItems
                        .OrderByDescending(x => x.DateModified) // Primary sort for grouping
                        .GroupBy(x => GetDateGroupHeader(x.DateModified));
                        
                    foreach (var group in grouped)
                    {
                        Items.Add(new ExplorerGroupHeaderViewModel(group.Key));
                        
                        // Secondary Sort within group
                        System.Collections.Generic.IEnumerable<ExplorerItemViewModel> groupItems = group;
                         if (SortBy == ExplorerSortOption.Name)
                         {
                             groupItems = IsSortDescending ? group.OrderByDescending(x => x.Name) : group.OrderBy(x => x.Name);
                         }
                         // If Date, it's already sorted by Date Desc (from primary) or we re-sort?
                        
                        foreach (var item in groupItems) Items.Add(item);
                    }
                }
                else if (GroupBy == ExplorerGroupBy.Type)
                {
                     var grouped = baseItems
                        .OrderBy(x => x.IsDirectory ? 0 : 1) // Folders first?
                        .ThenBy(x => Path.GetExtension(x.FullPath))
                        .GroupBy(x => x.IsDirectory ? "Folders" : (string.IsNullOrEmpty(Path.GetExtension(x.FullPath)) ? "Files" : Path.GetExtension(x.FullPath).ToUpperInvariant() + " Files"));
                        
                     foreach (var group in grouped.OrderBy(g => g.Key))
                     {
                         Items.Add(new ExplorerGroupHeaderViewModel(group.Key));
                         foreach (var item in group.OrderBy(x => x.Name)) Items.Add(item);
                     }
                }

            }
            catch { }
            
            IsEmpty = Items.Count == 0;
        });
    }

    private string GetDateGroupHeader(System.DateTime date)
    {
        var now = System.DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek); // Sunday
        var startOfLastWeek = startOfWeek.AddDays(-7);
        var startOfMonth = new System.DateTime(today.Year, today.Month, 1);
        var startOfLastMonth = startOfMonth.AddMonths(-1);

        if (date.Date == today) return "Today";
        if (date.Date == yesterday) return "Yesterday";
        if (date.Date >= startOfWeek) return "Earlier this week";
        if (date.Date >= startOfLastWeek) return "Last week";
        if (date.Date >= startOfMonth) return "Earlier this month";
        if (date.Date >= startOfLastMonth) return "Last month";
        
        return "A long time ago";
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        
        if (string.IsNullOrEmpty(CurrentPath)) return;

        // Handle specific case for UNC paths or normal paths
        // We want to show from RootPath upwards if possible, or full path logic
        
        string root = Path.GetPathRoot(CurrentPath) ?? "";
        bool isUnc = CurrentPath.StartsWith(@"\\") || CurrentPath.StartsWith("//");

        var accumulatingPath = "";

        if (isUnc)
        {
             // For UNC: \\Server\Share is the "Root" mostly
             // Split by separator but ignore empty entries from start
             var parts = CurrentPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, System.StringSplitOptions.RemoveEmptyEntries);
             
             // First two parts are Server and Share
             if (parts.Length >= 2)
             {
                 accumulatingPath = $@"\\{parts[0]}\{parts[1]}";
                 Breadcrumbs.Add(new BreadcrumbItem($"{parts[0]}\\{parts[1]}", accumulatingPath));

                 for (int i = 2; i < parts.Length; i++)
                 {
                     accumulatingPath = Path.Combine(accumulatingPath, parts[i]);
                     Breadcrumbs.Add(new BreadcrumbItem(parts[i], accumulatingPath));
                 }
             }
             else
             {
                 // Fallback for weird UNC
                 Breadcrumbs.Add(new BreadcrumbItem(CurrentPath, CurrentPath));
             }
        }
        else
        {
            // Local Drive
            var parts = CurrentPath.Split(Path.DirectorySeparatorChar);
            if (parts.Length > 0)
            {
                 var drive = parts[0];
                 accumulatingPath = drive + Path.DirectorySeparatorChar; // Ensure slash for drive root
                 Breadcrumbs.Add(new BreadcrumbItem(drive, accumulatingPath));
                 
                 for (int i=1; i<parts.Length; i++)
                 {
                     if (string.IsNullOrEmpty(parts[i])) continue; // trailing slash check
                     accumulatingPath = Path.Combine(accumulatingPath, parts[i]);
                     Breadcrumbs.Add(new BreadcrumbItem(parts[i], accumulatingPath));
                 }
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
    public string TargetPath { get; set; } = ""; // For .lnk resolving
    public bool IsDirectory { get; set; }
    public System.DateTime DateModified { get; set; }
    public long Size { get; set; } // Bytes
    public int ItemsCount { get; set; }
    
    public string IconKey => IsDirectory ? "IconFolder" : "IconFile";
    public string DisplaySize => IsDirectory ? $"{ItemsCount} items" : BytesToString(Size);
    
    public bool IsSelectable => true;

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
public record ExplorerGroupHeaderViewModel(string Title)
{
    public bool IsSelectable => false;
}

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _icon = "IconFolder";
    [ObservableProperty] private bool _isDynamic; // True = Relative to Output, False = Absolute
}

public class SidebarItemDto
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsDynamic { get; set; }
}

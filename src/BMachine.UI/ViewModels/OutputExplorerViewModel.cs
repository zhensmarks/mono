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
using Avalonia.Controls.ApplicationLifetimes;
using System.Runtime.InteropServices;
using BMachine.UI.Models;
using BMachine.Core.Platform;
using BMachine.UI.Views;

namespace BMachine.UI.ViewModels;

public partial class OutputExplorerViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly Services.FileOperationManager _fileManager;
    private readonly IPlatformService _platformService;

    [ObservableProperty] private string _currentPath = "";

    /// <summary>Display name of current folder (for title bar).</summary>
    public string CurrentFolderName => string.IsNullOrEmpty(CurrentPath) ? "" : Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
    private bool _isLoadingSettings;
    
    // Task Monitor
    public ObservableCollection<Services.FileTaskItem> ActiveTasks => _fileManager.ActiveTasks;

    public string RootPath { get; private set; } = "";

    public OutputExplorerViewModel(IDatabase database, INotificationService notificationService, Services.FileOperationManager fileManager, IPlatformService platformService)
    {
        _database = database;
        _notificationService = notificationService;
        _fileManager = fileManager;
        _platformService = platformService;
        
        _fileManager.ActiveTasks.CollectionChanged += OnActiveTasksChanged;
        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            _ = UpdatePreviewAsync();
        };

        // Default Sort (async, no blocking)
        LoadRootPath();
        _ = LoadScriptsAsync();
        _ = LoadExplorerShortcutsAsync();
        
        // Listen for path changes
        WeakReferenceMessenger.Default.Register<MasterPathsChangedMessage>(this, (r, m) => 
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(LoadRootPath);
        });
        // When shortcuts are changed in Settings, reload gestures so they apply without restart
        WeakReferenceMessenger.Default.Register<ExplorerShortcutsChangedMessage>(this, (r, m) =>
        {
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await LoadExplorerShortcutsAsync();
                // Notify the View AFTER gesture properties have been updated
                WeakReferenceMessenger.Default.Send(new ExplorerShortcutsReadyMessage());
            });
        });
        // Auto-refresh when master browser sync copy completes
        WeakReferenceMessenger.Default.Register<ExplorerRefreshRequestMessage>(this, (r, m) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Refresh());
        });
    }

    // Resilience
    [ObservableProperty] private Avalonia.Controls.GridLength _taskMonitorHeight = Avalonia.Controls.GridLength.Auto;


    // Sidebar Width Persistence
    [ObservableProperty] private Avalonia.Controls.GridLength _sidebarWidth = new Avalonia.Controls.GridLength(120);

    /// <summary>Keyboard shortcut for New Folder (from Settings > Explorer). Used by the view to register KeyBinding.</summary>
    [ObservableProperty] private string _shortcutNewFolderGesture = "Ctrl+Shift+N";
    /// <summary>Keyboard shortcut for New File (from Settings > Explorer).</summary>
    [ObservableProperty] private string _shortcutNewFileGesture = "Ctrl+Shift+T";
    /// <summary>Keyboard shortcut for Focus Path Bar / Address Bar (from Settings > Explorer).</summary>
    [ObservableProperty] private string _shortcutFocusSearchGesture = "Ctrl+L";
    [ObservableProperty] private string _shortcutDeleteGesture = "Ctrl+D";
    [ObservableProperty] private string _shortcutNewWindowGesture = "Ctrl+N";
    [ObservableProperty] private string _shortcutNewTabGesture = "Ctrl+T";
    [ObservableProperty] private string _shortcutCloseTabGesture = "Ctrl+W";
    [ObservableProperty] private string _shortcutNavigateUpGesture = "Alt+Up";
    [ObservableProperty] private string _shortcutBackGesture = "Alt+Left";
    [ObservableProperty] private string _shortcutForwardGesture = "Alt+Right";
    [ObservableProperty] private string _shortcutRenameGesture = "F2";
    [ObservableProperty] private string _shortcutPermanentDeleteGesture = "Shift+Delete";
    [ObservableProperty] private string _shortcutFocusSearchBoxGesture = "Ctrl+F";
    [ObservableProperty] private string _shortcutAddressBarGesture = "Alt+D";
    [ObservableProperty] private string _shortcutSwitchTabGesture = "Ctrl+Tab";
    [ObservableProperty] private string _shortcutRefreshGesture = "F5";

    // Preview panel (right side): .txt content, .docx placeholder
    [ObservableProperty] private bool _isPreviewPanelVisible;
    [ObservableProperty] private string _previewPanelTitle = "";
    [ObservableProperty] private string _previewPanelContent = "";

    public Avalonia.Controls.GridLength PreviewPanelWidth => IsPreviewPanelVisible ? new Avalonia.Controls.GridLength(280) : new Avalonia.Controls.GridLength(0);

    partial void OnIsPreviewPanelVisibleChanged(bool value) => OnPropertyChanged(nameof(PreviewPanelWidth));

    [RelayCommand]
    private void ClosePreviewPanel() => IsPreviewPanelVisible = false;

    partial void OnCurrentPathChanged(string value) => OnPropertyChanged(nameof(CurrentFolderName));

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

    [RelayCommand]
    public void RemoveTask(Services.FileTaskItem? task)
    {
        if (task != null) _fileManager.RemoveTask(task);
    }

    [RelayCommand]
    public void RetryTask(Services.FileTaskItem? task)
    {
        if (task != null) _fileManager.RetryTask(task);
    }

    private async void LoadRootPath()
    {
        var path = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";
        var savedHeightStr = await _database.GetAsync<string>("Configs.Explorer.TaskMonitorHeight");
        // Load global view settings first so they apply before path (persist across restart)
        var gSort = await _database.GetAsync<string>("Configs.Explorer.SortBy");
        var gDescStr = await _database.GetAsync<string>("Configs.Explorer.IsSortDescending");
        var gMode = await _database.GetAsync<string>("Configs.Explorer.ViewMode");
        var gGroup = await _database.GetAsync<string>("Configs.Explorer.GroupBy");

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _isLoadingSettings = true;
            try
            {
                if (!string.IsNullOrEmpty(gSort) && System.Enum.TryParse<ExplorerSortOption>(gSort, out var s)) SortBy = s;
                if (!string.IsNullOrEmpty(gDescStr) && bool.TryParse(gDescStr, out var d)) IsSortDescending = d;
                if (!string.IsNullOrEmpty(gMode) && System.Enum.TryParse<ExplorerLayoutMode>(gMode, out var l)) LayoutMode = l;
                if (!string.IsNullOrEmpty(gGroup) && System.Enum.TryParse<ExplorerGroupBy>(gGroup, out var g)) GroupBy = g;
                OnLayoutChanged_UI();
            }
            finally { _isLoadingSettings = false; }

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
            
            // Load Show Path Bar
            var showPathStr = await _database.GetAsync<string>("Configs.Explorer.ShowPathBar");
            if (!string.IsNullOrEmpty(showPathStr) && bool.TryParse(showPathStr, out bool showPath))
            {
                IsShowPathBar = showPath;
            }

            await LoadExplorerShortcutsAsync();

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

    /// <summary>Load shortcut gesture strings from DB (used on init and when Settings change).</summary>
    private async Task LoadExplorerShortcutsAsync()
    {
        var shortcutNewFolder = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewFolder");
        if (!string.IsNullOrEmpty(shortcutNewFolder)) ShortcutNewFolderGesture = shortcutNewFolder;
        var shortcutNewFile = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewFile");
        if (!string.IsNullOrEmpty(shortcutNewFile)) ShortcutNewFileGesture = shortcutNewFile;
        var shortcutFocusSearch = await _database.GetAsync<string>("Configs.Explorer.ShortcutFocusSearch");
        if (!string.IsNullOrEmpty(shortcutFocusSearch)) ShortcutFocusSearchGesture = shortcutFocusSearch;
        var shortcutDelete = await _database.GetAsync<string>("Configs.Explorer.ShortcutDelete");
        if (!string.IsNullOrEmpty(shortcutDelete)) ShortcutDeleteGesture = shortcutDelete;
        var shortcutNewWindow = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewWindow");
        if (!string.IsNullOrEmpty(shortcutNewWindow)) ShortcutNewWindowGesture = shortcutNewWindow;
        var shortcutNewTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewTab");
        if (!string.IsNullOrEmpty(shortcutNewTab)) ShortcutNewTabGesture = shortcutNewTab;
        var shortcutCloseTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutCloseTab");
        if (!string.IsNullOrEmpty(shortcutCloseTab)) ShortcutCloseTabGesture = shortcutCloseTab;
        var shortcutNavigateUp = await _database.GetAsync<string>("Configs.Explorer.ShortcutNavigateUp");
        if (!string.IsNullOrEmpty(shortcutNavigateUp)) ShortcutNavigateUpGesture = shortcutNavigateUp;
        var shortcutBack = await _database.GetAsync<string>("Configs.Explorer.ShortcutBack");
        if (!string.IsNullOrEmpty(shortcutBack)) ShortcutBackGesture = shortcutBack;
        var shortcutForward = await _database.GetAsync<string>("Configs.Explorer.ShortcutForward");
        if (!string.IsNullOrEmpty(shortcutForward)) ShortcutForwardGesture = shortcutForward;
        var shortcutRename = await _database.GetAsync<string>("Configs.Explorer.ShortcutRename");
        if (!string.IsNullOrEmpty(shortcutRename)) ShortcutRenameGesture = shortcutRename;
        var shortcutPermanentDelete = await _database.GetAsync<string>("Configs.Explorer.ShortcutPermanentDelete");
        if (!string.IsNullOrEmpty(shortcutPermanentDelete)) ShortcutPermanentDeleteGesture = shortcutPermanentDelete;
        var shortcutFocusSearchBox = await _database.GetAsync<string>("Configs.Explorer.ShortcutFocusSearchBox");
        if (!string.IsNullOrEmpty(shortcutFocusSearchBox)) ShortcutFocusSearchBoxGesture = shortcutFocusSearchBox;
        var shortcutAddressBar = await _database.GetAsync<string>("Configs.Explorer.ShortcutAddressBar");
        if (!string.IsNullOrEmpty(shortcutAddressBar)) ShortcutAddressBarGesture = shortcutAddressBar;
        var shortcutSwitchTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutSwitchTab");
        if (!string.IsNullOrEmpty(shortcutSwitchTab)) ShortcutSwitchTabGesture = shortcutSwitchTab;
        var shortcutRefresh = await _database.GetAsync<string>("Configs.Explorer.ShortcutRefresh");
        if (!string.IsNullOrEmpty(shortcutRefresh)) ShortcutRefreshGesture = shortcutRefresh;
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

    public enum ExplorerSortOption { Name, Date, Type }
    public enum ExplorerLayoutMode { Vertical, Horizontal, Thumbnail }
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
        if (_isLoadingSettings) return;
        LoadItems();
        SaveViewSettings();
    }

    partial void OnIsSortDescendingChanged(bool value) 
    {
        if (_isLoadingSettings) return;
        LoadItems();
        SaveViewSettings();
    }

    partial void OnGroupByChanged(ExplorerGroupBy value) 
    {
        if (_isLoadingSettings) return;
        LoadItems();
        SaveViewSettings();
    }
    
    partial void OnLayoutModeChanged(ExplorerLayoutMode value) 
    {
        OnLayoutChanged_UI();
        if (_isLoadingSettings) return;
        SaveViewSettings();
    }

    partial void OnIsLocalSettingsChanged(bool value)
    {
         if (_isLoadingSettings) return;

         if (value)
         {
             // Switched TO Local -> Save current state as local settings immediately
             SaveViewSettings();
         }
         else
         {
             // Switched TO Global -> Delete local settings and Revert to Global Defaults
             if (!string.IsNullOrEmpty(CurrentPath))
             {
                 var normalizedPath = CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                 var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(normalizedPath.ToLowerInvariant()));
                 _database.DeleteAsync($"Configs.Explorer.FolderSettings.{pathKey}");
             }
             
             // Reload to apply global defaults
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LoadViewSettingsForPath(CurrentPath));
         }
    }

    partial void OnTaskMonitorHeightChanged(Avalonia.Controls.GridLength value)
    {
        // Settings for this are removed as it's now a separate window
    }

    private TaskMonitorWindow? _taskMonitorWindow;

    /// <summary>Single shared File Operations window for the whole app (all explorer windows).</summary>
    private static TaskMonitorWindow? s_sharedTaskMonitorWindow;

    private void OnActiveTasksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            ShowTaskMonitor();
            foreach (var item in e.NewItems)
            {
                if (item is Services.FileTaskItem task)
                {
                    task.PropertyChanged += OnFileTaskPropertyChanged;
                }
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is Services.FileTaskItem task)
                {
                    task.PropertyChanged -= OnFileTaskPropertyChanged;
                }
            }
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LoadItems());
        }
        // Do NOT close the window when ActiveTasks becomes empty - only the X button closes it.
        // "Clear All Completed" just clears history; window stays open.
    }

    private void OnFileTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Services.FileTaskItem.Status) && e.PropertyName != nameof(Services.FileTaskItem.IsCompleted) && e.PropertyName != nameof(Services.FileTaskItem.IsFailed))
            return;
        if (sender is Services.FileTaskItem task && (task.IsCompleted || task.IsFailed))
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LoadItems());
        }
    }

    [RelayCommand]
    public void ShowTaskMonitor()
    {
        // One File Operations window for the whole app (main + any New Explorer Window).
        if (s_sharedTaskMonitorWindow != null)
        {
            try
            {
                s_sharedTaskMonitorWindow.Activate();
                return;
            }
            catch { /* window may be closed */ }
            s_sharedTaskMonitorWindow = null;
        }

        _taskMonitorWindow = new TaskMonitorWindow();
        _taskMonitorWindow.DataContext = this;
        _taskMonitorWindow.Closed += (s, e) =>
        {
            _taskMonitorWindow = null;
            s_sharedTaskMonitorWindow = null;
        };
        s_sharedTaskMonitorWindow = _taskMonitorWindow;
        _taskMonitorWindow.Show();
    }
    private async void SaveViewSettings()
    {
        // Always persist global defaults (Sort/Group/View) so they survive restart
        if (string.IsNullOrEmpty(CurrentPath))
        {
            await _database.SetAsync("Configs.Explorer.SortBy", SortBy.ToString());
            await _database.SetAsync("Configs.Explorer.IsSortDescending", IsSortDescending.ToString());
            await _database.SetAsync("Configs.Explorer.ViewMode", LayoutMode.ToString());
            await _database.SetAsync("Configs.Explorer.GroupBy", GroupBy.ToString());
            return;
        }

        // Normalize path (no trailing slash) so "C:\A" and "C:\A\" are the same folder - settings apply to this folder only, not subfolders
        var normalizedPath = CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(normalizedPath.ToLowerInvariant()));

        if (IsLocalSettings)
        {
            // Save ONLY for this exact folder (subfolders will use their own or global settings)
            var settings = new FolderViewSettingsDto
            {
                SortBy = SortBy.ToString(),
                IsSortDescending = IsSortDescending,
                LayoutMode = LayoutMode.ToString(),
                GroupBy = GroupBy.ToString()
            };
            await _database.SetAsync($"Configs.Explorer.FolderSettings.{pathKey}", settings);
        }
        else
        {
            // Save as Global Defaults; remove this folder's override so it uses globals
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

        // Normalize so subfolders never inherit parent's "this folder only" settings
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathKey = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(normalizedPath.ToLowerInvariant()));
        var localSettings = await _database.GetAsync<FolderViewSettingsDto>($"Configs.Explorer.FolderSettings.{pathKey}");

        _isLoadingSettings = true;
        try
        {
            if (localSettings != null)
            {
                // Apply Local
                IsLocalSettings = true; 
                
                if (System.Enum.TryParse<ExplorerSortOption>(localSettings.SortBy, out var s)) SortBy = s;
                IsSortDescending = localSettings.IsSortDescending;
                if (System.Enum.TryParse<ExplorerLayoutMode>(localSettings.LayoutMode, out var l)) LayoutMode = l;
                if (System.Enum.TryParse<ExplorerGroupBy>(localSettings.GroupBy, out var g)) GroupBy = g;
                
                OnLayoutChanged_UI();
            }
            else
            {
                // Apply Global Defaults
                IsLocalSettings = false;

                // Load Globals (Stored as Strings to satisfy 'where T : class')
                var gSort = await _database.GetAsync<string>("Configs.Explorer.SortBy");
                var gDescStr = await _database.GetAsync<string>("Configs.Explorer.IsSortDescending");
                var gMode = await _database.GetAsync<string>("Configs.Explorer.ViewMode");
                var gGroup = await _database.GetAsync<string>("Configs.Explorer.GroupBy");

                if (!string.IsNullOrEmpty(gSort) && System.Enum.TryParse<ExplorerSortOption>(gSort, out var s)) SortBy = s;
                else SortBy = ExplorerSortOption.Name;

                if (!string.IsNullOrEmpty(gDescStr) && bool.TryParse(gDescStr, out var d)) IsSortDescending = d;
                else IsSortDescending = false; // Default

                if (!string.IsNullOrEmpty(gMode) && System.Enum.TryParse<ExplorerLayoutMode>(gMode, out var l)) LayoutMode = l;
                else LayoutMode = ExplorerLayoutMode.Vertical;

                if (!string.IsNullOrEmpty(gGroup) && System.Enum.TryParse<ExplorerGroupBy>(gGroup, out var g)) GroupBy = g;
                else GroupBy = ExplorerGroupBy.Date;

                OnLayoutChanged_UI();
            }
        }
        finally { _isLoadingSettings = false; }
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
    public bool IsThumbnailLayout => LayoutMode == ExplorerLayoutMode.Thumbnail;
    
    public bool IsVerticalLayoutAndNotEmpty => IsVerticalLayout && !IsEmpty;
    public bool IsHorizontalLayoutAndNotEmpty => IsHorizontalLayout && !IsEmpty;
    public bool IsThumbnailLayoutAndNotEmpty => IsThumbnailLayout && !IsEmpty;

    
    // partial void OnLayoutModeChanged(ExplorerLayoutMode value) // Removed duplicate partial method
    // {
    //    OnLayoutChanged_UI();
    // }
    
    private void OnLayoutChanged_UI()
    {
        OnPropertyChanged(nameof(IsVerticalLayout));
        OnPropertyChanged(nameof(IsHorizontalLayout));
        OnPropertyChanged(nameof(IsThumbnailLayout));
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsThumbnailLayoutAndNotEmpty));
        LoadItems_UIOnly();
    }
    
    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsThumbnailLayoutAndNotEmpty));
    }

    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _selectedItems = new();

    /// <summary>Paths that were cut (move on paste). Cleared on Copy or after Paste.</summary>
    private HashSet<string>? _cutPaths;

    /// <summary>True when at least one item is selected. For context menu: show Copy/Cut only when true.</summary>
    public bool HasSelection => SelectedItems?.Count > 0;

    /// <summary>True when clipboard contains file(s). For context menu: show/enable Paste only when true.</summary>
    [ObservableProperty] private bool _hasFileClipboard;

    partial void OnSelectedItemsChanged(ObservableCollection<ExplorerItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = UpdatePreviewAsync();
    }

    private async Task UpdatePreviewAsync()
    {
        var first = GetSelectedItems(null).FirstOrDefault();
        if (first == null || first.IsDirectory)
        {
            IsPreviewPanelVisible = false;
            return;
        }
        var ext = Path.GetExtension(first.FullPath).ToLowerInvariant();
        if (ext != ".txt" && ext != ".docx")
        {
            IsPreviewPanelVisible = false;
            return;
        }
        PreviewPanelTitle = first.Name;
        if (ext == ".txt")
        {
            try
            {
                var content = await Task.Run(() =>
                {
                    try { return File.ReadAllText(first.FullPath); }
                    catch { return ""; }
                });
                PreviewPanelContent = content;
            }
            catch
            {
                PreviewPanelContent = "(Could not read file.)";
            }
        }
        else
        {
            PreviewPanelContent = "(Preview for .docx: open with default app to view.)";
        }
        IsPreviewPanelVisible = true;
    }

    /// <summary>Call before showing context menu (e.g. on right-click) to refresh Paste visibility.</summary>
    public async void UpdateClipboardStateAsync()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow?.Clipboard is not { } clipboard)
            {
                HasFileClipboard = false;
                return;
            }
            var formats = await clipboard.GetFormatsAsync();
            var hasFiles = formats.Contains(Avalonia.Input.DataFormats.FileNames) ||
                          formats.Contains(Avalonia.Input.DataFormats.Files) ||
                          formats.Contains("Files") ||
                          (formats.Contains(Avalonia.Input.DataFormats.Text) && await HasFilePathsInClipboardTextAsync(clipboard));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HasFileClipboard = hasFiles);
        }
        catch { Avalonia.Threading.Dispatcher.UIThread.Post(() => HasFileClipboard = false); }
    }

    private static async Task<bool> HasFilePathsInClipboardTextAsync(Avalonia.Input.Platform.IClipboard clipboard)
    {
        try
        {
            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return false;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Any(l => File.Exists(l.Trim()) || Directory.Exists(l.Trim()));
        }
        catch { return false; }
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
        // Shortcuts: open with default app (Windows .lnk, macOS/Linux .desktop etc.)
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (IsShortcutExtension(ext))
        {
            try { _platformService.OpenWithDefaultApp(path); } catch { }
            return;
        }

        // Bitmap images: always open with system default viewer (cross-platform)
        if (new[] { ".jpg", ".jpeg", ".png" }.Contains(ext))
        {
            try { _platformService.OpenWithDefaultApp(path); } catch { }
            return;
        }

        // Other images (.psd, .tif, .webp): try Photoshop if available, else default app
        if (new[] { ".psd", ".tif", ".tiff", ".webp" }.Contains(ext))
        {
            OpenInPhotoshopSmart(path);
            return;
        }

        // Everything else: default app
        try { _platformService.OpenWithDefaultApp(path); } catch { }
    }

    /// <summary>"Open with…" — launches the OS application picker dialog.</summary>
    [RelayCommand]
    private void OpenWith(object? parameter)
    {
        var item = parameter as ExplorerItemViewModel ?? GetSelectedItems(null).FirstOrDefault();
        if (item == null || item.IsDirectory) return;
        try { _platformService.OpenWithDialog(item.FullPath); } catch { }
    }

    private static bool IsShortcutExtension(string ext)
    {
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)  // Windows
            || ext.Equals(".desktop", StringComparison.OrdinalIgnoreCase) // Linux
            || ext.Equals(".webloc", StringComparison.OrdinalIgnoreCase); // macOS (optional)
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
             try { _platformService.OpenWithDefaultApp(imagePath); } catch { }
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
        _cutPaths = null; // Copy clears cut intent
        var items = GetSelectedItems(parameter);
        if (!items.Any())
        {
            _notificationService.ShowError("No items selected.");
            return;
        }

        var paths = items.Select(x => x.FullPath).ToArray();
        var data = new Avalonia.Input.DataObject();
        data.Set(Avalonia.Input.DataFormats.FileNames, paths);
        data.Set(Avalonia.Input.DataFormats.Text, string.Join(Environment.NewLine, paths));

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetDataObjectAsync(data);
                UpdateClipboardStateAsync();
            }
            else
            {
                _notificationService.ShowError("Clipboard is null (CopyItem).");
            }
        }
        else
        {
            _notificationService.ShowError("Not a Desktop App (CopyItem).");
        }
    }

    [RelayCommand]
    public async Task CutItem(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any())
        {
            _notificationService.ShowError("No items selected.");
            return;
        }

        _cutPaths = new HashSet<string>(items.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
        var paths = items.Select(x => x.FullPath).ToArray();
        var data = new Avalonia.Input.DataObject();
        data.Set(Avalonia.Input.DataFormats.FileNames, paths);
        data.Set(Avalonia.Input.DataFormats.Text, string.Join(Environment.NewLine, paths));

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetDataObjectAsync(data);
                UpdateClipboardStateAsync();
            }
            else
            {
                _notificationService.ShowError("Clipboard is null (CutItem).");
            }
        }
        else
        {
            _notificationService.ShowError("Not a Desktop App (CutItem).");
        }
    }


    [RelayCommand]
    public async Task PasteItem()
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath))
        {
            _notificationService.ShowError("Tidak ada folder tujuan untuk paste.");
            return;
        }

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard == null)
                {
                    _notificationService.ShowError("Clipboard tidak tersedia.");
                    return;
                }

                var formats = await clipboard.GetFormatsAsync();
                List<string>? fileList = null;

                // Try FileNames first (what we set on Copy)
                if (fileList == null && formats.Contains(Avalonia.Input.DataFormats.FileNames))
                {
                    var fileData = await clipboard.GetDataAsync(Avalonia.Input.DataFormats.FileNames);
                    fileList = ClipboardDataToPathList(fileData);
                }
                // Try Files format (platform may expose file list under this)
                if (fileList == null && formats.Contains(Avalonia.Input.DataFormats.Files))
                {
                    var fileData = await clipboard.GetDataAsync(Avalonia.Input.DataFormats.Files);
                    fileList = ClipboardDataToPathList(fileData);
                }
                if (fileList == null && formats.Contains("Files"))
                {
                    var fileData = await clipboard.GetDataAsync("Files");
                    fileList = ClipboardDataToPathList(fileData);
                }
                // Fallback: text (we also set this on Copy so our own copy always works)
                if (fileList == null && formats.Contains(Avalonia.Input.DataFormats.Text))
                {
                    var text = await clipboard.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Any(l => File.Exists(l.Trim()) || Directory.Exists(l.Trim())))
                            fileList = lines.Select(l => l.Trim()).ToList();
                    }
                }

                if (fileList == null || !fileList.Any())
                {
                    _notificationService.ShowError("Tidak ada file atau folder di clipboard.");
                    return;
                }

                var normalizedList = fileList.Select(f => f.Trim()).ToList();
                bool isMove = _cutPaths != null && normalizedList.Count == _cutPaths.Count &&
                             normalizedList.All(p => _cutPaths.Contains(p));

                int count = 0;
                foreach (var file in normalizedList)
                {
                    var cleanPath = file;
                    if (!File.Exists(cleanPath) && !Directory.Exists(cleanPath)) continue;

                    string fileName = Path.GetFileName(cleanPath);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    string dest = Path.Combine(CurrentPath, fileName);

                    if (cleanPath.Equals(dest, StringComparison.OrdinalIgnoreCase))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        string newName = $"{nameNoExt} - Copy{ext}";
                        dest = Path.Combine(CurrentPath, newName);
                        int i = 2;
                        while (File.Exists(dest) || Directory.Exists(dest))
                        {
                            newName = $"{nameNoExt} - Copy ({i}){ext}";
                            dest = Path.Combine(CurrentPath, newName);
                            i++;
                        }
                    }
                    else if (File.Exists(dest) || Directory.Exists(dest))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int i = 1;
                        while (File.Exists(dest) || Directory.Exists(dest))
                        {
                            string suffix = i == 1 ? " - Copy" : $" - Copy ({i})";
                            dest = Path.Combine(CurrentPath, $"{nameNoExt}{suffix}{ext}");
                            i++;
                        }
                    }

                    if (isMove)
                        _fileManager.MoveFileBackground(cleanPath, dest);
                    else
                        _fileManager.CopyFileBackground(cleanPath, dest);
                    count++;
                }

                if (count > 0)
                {
                    if (isMove)
                    {
                        _cutPaths = null;
                        // Clear file data from clipboard so paste elsewhere doesn't move again
                        try
                        {
                            await clipboard.ClearAsync();
                        }
                        catch { /* ignore */ }
                        UpdateClipboardStateAsync();
                    }
                }
            }
            else
            {
                _notificationService.ShowError("Not a Desktop App (PasteItem).");
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Paste Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles files/folders dropped from external applications (e.g. Windows Explorer).
    /// Copies them to the current folder.
    /// </summary>
    public void HandleDroppedFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;

        int count = 0;
        foreach (var sourcePath in paths)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) continue;

            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)) continue;

            string dest = Path.Combine(CurrentPath, fileName);

            // Handle duplicate names
            if (sourcePath.Equals(dest, StringComparison.OrdinalIgnoreCase)) continue; // Skip same file
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                int i = 1;
                while (File.Exists(dest) || Directory.Exists(dest))
                {
                    string suffix = i == 1 ? " - Copy" : $" - Copy ({i})";
                    dest = Path.Combine(CurrentPath, $"{nameNoExt}{suffix}{ext}");
                    i++;
                }
            }

            _fileManager.CopyFileBackground(sourcePath, dest);
            count++;
        }

        if (count > 0) ShowTaskMonitor();
    }

    /// <summary>
    /// Returns full paths of currently selected items (for drag-out to external apps).
    /// </summary>
    public System.Collections.Generic.List<string> GetSelectedFilePaths()
    {
        var paths = new System.Collections.Generic.List<string>();
        foreach (var item in SelectedItems)
        {
            if (item is ExplorerItemViewModel evm && !string.IsNullOrEmpty(evm.FullPath))
                paths.Add(evm.FullPath);
        }
        return paths;
    }

    /// <summary>
    /// Converts clipboard data (various formats) to a list of local file/folder paths.
    /// </summary>
    private static List<string>? ClipboardDataToPathList(object? fileData)
    {
        if (fileData == null) return null;

        if (fileData is IEnumerable<string> enumStr)
            return enumStr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        if (fileData is string[] arr)
            return arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        if (fileData is System.Collections.IEnumerable enumerable)
        {
            var list = new List<string>();
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                if (item is Avalonia.Platform.Storage.IStorageItem storageItem)
                {
                    var path = storageItem.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path)) list.Add(path);
                }
                else if (item is FileInfo fi)
                    list.Add(fi.FullName);
                else if (item is DirectoryInfo di)
                    list.Add(di.FullName);
                else
                {
                    var str = item.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(str)) list.Add(str);
                }
            }
            if (list.Count > 0) return list;
        }

        return null;
    }

    [RelayCommand]
    public void RenameItem(object? parameter)
    {
        ExplorerItemViewModel? toEdit = null;
        if (parameter is ExplorerItemViewModel item)
            toEdit = item;
        else if (parameter is System.Collections.IList list && list.Count > 0 && list[0] is ExplorerItemViewModel first)
            toEdit = first;
        else
            toEdit = GetSelectedItems(null).FirstOrDefault();
        if (toEdit != null)
            toEdit.IsEditing = true;
    }

    [RelayCommand]
    public void CommitRename(object? parameter)
    {
        if (parameter is ExplorerItemViewModel item)
        {
            item.IsEditing = false;
            if (string.IsNullOrWhiteSpace(item.Name)) return; // Revert?
            
            // Perform Rename
            try
            {
                string oldPath = item.FullPath;
                string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, item.Name);
                
                if (oldPath.Equals(newPath, System.StringComparison.OrdinalIgnoreCase)) return;

                if (item.IsDirectory) Directory.Move(oldPath, newPath);
                else File.Move(oldPath, newPath);
                
                // Update Model
                item.FullPath = newPath;
                // Refresh list or just item? Refresh is safer to resort
                LoadItems(); 
            }
            catch (System.Exception ex)
            {
                _notificationService.ShowError($"Rename failed: {ex.Message}");
                LoadItems(); // Revert UI
            }
        }
    }

    [RelayCommand]
    public void CancelRename(object? parameter)
    {
         if (parameter is ExplorerItemViewModel item)
        {
            item.IsEditing = false;
            // Name binding is TwoWay, so it might have changed. 
            // Ideally we should revert to original name.  
            // Since we reload items on LoadItems, maybe just Refresh.
            LoadItems();
        }
    }

    [RelayCommand]
    public void DeleteItem(object? parameter)
    {
        DeleteItemsInternal(parameter, useRecycleBin: true);
    }

    /// <summary>Permanent delete (bypass Recycle Bin). Shift+Delete.</summary>
    [RelayCommand]
    public void PermanentDeleteItem(object? parameter)
    {
        DeleteItemsInternal(parameter, useRecycleBin: false);
    }

    private void DeleteItemsInternal(object? parameter, bool useRecycleBin)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        int deletedCount = 0;
        foreach (var item in items)
        {
            try
            {
                if (useRecycleBin)
                {
                    if (_platformService.MoveToRecycleBin(item.FullPath))
                        deletedCount++;
                    else
                        _notificationService.ShowError($"Could not move {item.Name} to Recycle Bin.");
                }
                else
                {
                    if (item.IsDirectory) Directory.Delete(item.FullPath, true);
                    else File.Delete(item.FullPath);
                    deletedCount++;
                }
            }
            catch (System.Exception ex)
            {
                _notificationService.ShowError($"Failed to delete {item.Name}: {ex.Message}");
            }
        }

        if (deletedCount > 0)
            LoadItems();
    }

    private System.Collections.Generic.List<ExplorerItemViewModel> GetSelectedItems(object? parameter)
    {
        var list = new System.Collections.Generic.List<ExplorerItemViewModel>();
        var validSelection = SelectedItems.OfType<ExplorerItemViewModel>().ToList();

        if (parameter is ExplorerItemViewModel singleItem)
        {
            if (!validSelection.Contains(singleItem))
                list.Add(singleItem);
            else
                list.AddRange(validSelection);
        }
        else if (validSelection.Count > 0)
            list.AddRange(validSelection);

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

                // Natural sort comparer for Windows Explorer-style ordering (1, 2, 3, 10 not 1, 10, 11, 2)
                var naturalComparer = new NaturalSortComparer();

                // Shortcut files (.lnk, .desktop) kept separate from physical folders in sort order
                static bool IsShortcutFile(ExplorerItemViewModel x) => !x.IsDirectory && IsShortcutExtension(Path.GetExtension(x.FullPath));

                // Grouping & Sorting logic
                if (GroupBy == ExplorerGroupBy.None)
                {
                    if (SortBy == ExplorerSortOption.Name)
                    {
                         var allDirs = baseItems.Where(x => x.IsDirectory).ToList();
                         var shortcutFiles = baseItems.Where(IsShortcutFile).ToList();
                         var otherFiles = baseItems.Where(x => !x.IsDirectory && !IsShortcutFile(x)).ToList();
                         
                         allDirs.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         shortcutFiles.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         otherFiles.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         
                         if (IsSortDescending) { allDirs.Reverse(); shortcutFiles.Reverse(); otherFiles.Reverse(); }
                         
                         foreach (var i in allDirs) Items.Add(i);
                         foreach (var i in shortcutFiles) Items.Add(i);
                         foreach (var i in otherFiles) Items.Add(i);
                    }
                    else if (SortBy == ExplorerSortOption.Type)
                    {
                         // Folders first, then shortcut files, then other files by type
                         var allDirs = baseItems.Where(x => x.IsDirectory).ToList();
                         var shortcutFiles = baseItems.Where(IsShortcutFile).ToList();
                         var otherFiles = baseItems.Where(x => !x.IsDirectory && !IsShortcutFile(x)).ToList();
                         allDirs.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         shortcutFiles.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         otherFiles.Sort((a, b) =>
                         {
                             string typeA = GetSortTypeKey(a.Name);
                             string typeB = GetSortTypeKey(b.Name);
                             int typeCmp = string.Compare(typeA, typeB, StringComparison.OrdinalIgnoreCase);
                             return typeCmp != 0 ? typeCmp : naturalComparer.Compare(a.Name, b.Name);
                         });
                         if (IsSortDescending) { allDirs.Reverse(); shortcutFiles.Reverse(); otherFiles.Reverse(); }
                         foreach (var i in allDirs) Items.Add(i);
                         foreach (var i in shortcutFiles) Items.Add(i);
                         foreach (var i in otherFiles) Items.Add(i);
                    }
                    else
                    {
                         // Sort by Date Mixed
                         var sorted = IsSortDescending 
                             ? baseItems.OrderByDescending(x => x.DateModified) 
                             : baseItems.OrderBy(x => x.DateModified);
                         foreach (var i in sorted) Items.Add(i);
                    }
                }
                else if (GroupBy == ExplorerGroupBy.Date)
                {
                    // Date Grouping (OS Explorer order): Today, Yesterday, Earlier this week, Last week, Earlier this month, Last month, A long time ago
                    var dateGroupOrder = new[] { "Today", "Yesterday", "Earlier this week", "Last week", "Earlier this month", "Last month", "A long time ago" };
                    var grouped = baseItems
                        .GroupBy(x => GetDateGroupHeader(x.DateModified))
                        .OrderBy(g => Array.IndexOf(dateGroupOrder, g.Key) >= 0 ? Array.IndexOf(dateGroupOrder, g.Key) : dateGroupOrder.Length);
                    foreach (var group in grouped)
                    {
                        Items.Add(new ExplorerGroupHeaderViewModel(group.Key));
                        var groupItemsList = group.ToList();
                        if (SortBy == ExplorerSortOption.Name || SortBy == ExplorerSortOption.Type)
                        {
                            groupItemsList.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                            if (IsSortDescending) groupItemsList.Reverse();
                        }
                        else
                        {
                            groupItemsList.Sort((a, b) => IsSortDescending
                                ? b.DateModified.CompareTo(a.DateModified)
                                : a.DateModified.CompareTo(b.DateModified));
                        }
                        foreach (var item in groupItemsList) Items.Add(item);
                    }
                }
                else if (GroupBy == ExplorerGroupBy.Type)
                {
                     string TypeGroupKey(ExplorerItemViewModel x)
                     {
                         if (x.IsDirectory) return "Folders";
                         if (IsShortcutFile(x)) return "Shortcut";
                         var ext = Path.GetExtension(x.FullPath);
                         return string.IsNullOrEmpty(ext) ? "Files" : ext.TrimStart('.').ToUpperInvariant() + " Files";
                     }
                     int TypeGroupOrder(string key)
                     {
                         if (key == "Folders") return 0;
                         if (key == "Shortcut") return 1;
                         return 2;
                     }
                     var grouped = baseItems.GroupBy(TypeGroupKey);
                     foreach (var group in grouped.OrderBy(g => TypeGroupOrder(g.Key)).ThenBy(g => g.Key))
                     {
                         Items.Add(new ExplorerGroupHeaderViewModel(group.Key));
                         var sortedGroupItems = group.ToList();
                         sortedGroupItems.Sort((a, b) => naturalComparer.Compare(a.Name, b.Name));
                         if (IsSortDescending) sortedGroupItems.Reverse();
                         foreach (var item in sortedGroupItems) Items.Add(item);
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

    /// <summary>Sort-by-type key (Explorer-style: Shortcut, extension groups, etc.).</summary>
    private static string GetSortTypeKey(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return " ";
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return "Shortcut";
        return ext.TrimStart('.').ToUpperInvariant();
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

    public string ShowInExplorerHeader => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Show in Finder" : "Show in Explorer";

    private bool IsSubfolderOf(string path, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return p.StartsWith(r, System.StringComparison.OrdinalIgnoreCase) && p.Length > r.Length;
    }

    // --- CONTEXT MENU EXTENSIONS & SCRIPT LOGIC ---


    [RelayCommand]
    public async Task CopyPath(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        var text = string.Join(Environment.NewLine, items.Select(x => x.FullPath));
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
             var clipboard = desktop.MainWindow?.Clipboard;
             if (clipboard != null)
             {
                 await clipboard.SetTextAsync(text);
                 _notificationService.ShowSuccess("Path copied to clipboard.");
             }
        }
    }

    [RelayCommand]
    public async Task CopyName(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        var text = string.Join(Environment.NewLine, items.Select(x => x.Name));
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
             var clipboard = desktop.MainWindow?.Clipboard;
             if (clipboard != null)
             {
                 await clipboard.SetTextAsync(text);
                 _notificationService.ShowSuccess("Name copied to clipboard.");
             }
        }
    }

    [RelayCommand]
    public void BatchAction(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        var folders = items.Where(x => x.IsDirectory).Select(x => x.FullPath).ToArray();
        
        if (folders.Length > 0)
        {
             WeakReferenceMessenger.Default.Send(new BatchAddFoldersMessage(folders));
             WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("Batch"));
        }
    }

    [RelayCommand]
    public void BatchScript(object? parameter)
    {
         if (parameter is not BatchScriptOption script) return;
         
         var items = GetSelectedItems(null); 
         var folders = items.Where(x => x.IsDirectory).ToList();
         
         if (folders.Count == 0) return;

         var paths = folders.Select(x => x.FullPath).ToArray();
         var scriptFileName = Path.GetFileName(script.Path);
         WeakReferenceMessenger.Default.Send(new BatchAddFoldersMessage(paths, scriptFileName));
         WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("Batch"));
    }
    
    // UI Toggles
    [ObservableProperty] private bool _isShowPathBar = true;
    
    [RelayCommand]
    public void ToggleShowPath()
    {
        IsShowPathBar = !IsShowPathBar;
    }

    partial void OnIsShowPathBarChanged(bool value)
    {
        _database.SetAsync("Configs.Explorer.ShowPathBar", value.ToString());
    }

    [RelayCommand]
    public void ToggleLocalSettings()
    {
        IsLocalSettings = !IsLocalSettings;
    }

    [RelayCommand]
    public void NewExplorerWindow()
    {
        string logPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "BMachine_ExplorerWindow.log");
        void Log(string msg) 
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now}: {msg}\n"); } catch { }
            System.Console.WriteLine(msg);
        }

        try 
        {
             Log("[NewExplorerWindow] Command Executed.");
             // Create a new instance of the ViewModel for the new window
             // Reuse the SAME services to share state (except maybe navigation state which is new)
             var vm = new OutputExplorerViewModel(_database, _notificationService, _fileManager, _platformService);
             Log("[NewExplorerWindow] ViewModel Created.");

             var windowVm = new ExplorerWindowViewModel(_database, _notificationService, _fileManager, _platformService, vm);
             var win = new BMachine.UI.Views.ExplorerWindow
             {
                 DataContext = windowVm
             };
             win.Init(_database);
             Log("[NewExplorerWindow] Window Created. Calling Show().");
             win.Show();
             Log("[NewExplorerWindow] Window Shown.");
        }
        catch (System.Exception ex)
        {
            Log($"[NewExplorerWindow] ERROR: {ex.Message}");
            Log(ex.StackTrace ?? "");
            _notificationService.ShowError($"Failed to open new window: {ex.Message}");
        }
    }

    // --- BATCH SCRIPT SUB-MENU LOGIC ---

    [ObservableProperty]
    private ObservableCollection<BatchScriptOption> _scriptOptions = new();

    private Dictionary<string, ScriptConfig> _scriptAliases = new();
    private string _metadataPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");

    private void LoadMetadata()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    _scriptAliases = new(); 
                }
            }
        }
        catch { _scriptAliases = new(); }
    }

    public async Task LoadScriptsAsync()
    {
        try
        {
            LoadMetadata();
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
            var customScripts = await _database.GetAsync<string>("Configs.System.ScriptsPath");
            if (!string.IsNullOrEmpty(customScripts) && Directory.Exists(customScripts))
                baseDir = customScripts;

            var masterDir = Path.Combine(baseDir, "Master");
            if (!Directory.Exists(masterDir)) return;

            var pyFiles = Directory.GetFiles(masterDir, "*.py");
            var list = new List<(BatchScriptOption Option, int Order)>();

            foreach (var f in pyFiles)
            {
                var fname = Path.GetFileName(f);
                string display = Path.GetFileNameWithoutExtension(fname);
                int order = 9999;

                if (_scriptAliases.ContainsKey(fname))
                {
                    var config = _scriptAliases[fname];
                    display = config.Name;
                    order = config.Order;
                }

                list.Add((new BatchScriptOption
                {
                    Name = display,
                    OriginalName = fname,
                    Path = f
                }, order));
            }

            var sortedList = list.OrderBy(x => x.Order).ThenBy(x => x.Option.Name).Select(x => x.Option).ToList();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ScriptOptions = new ObservableCollection<BatchScriptOption>(sortedList);
            });
        }
        catch { }
    }

    private async Task ExecuteFolderScript(BatchScriptOption script)
    {
        if (script == null) return;
        
        var items = GetSelectedItems(null); 
        var folders = items.Where(x => x.IsDirectory).ToList();
        
        if (!folders.Any()) return;

        var scriptName = Path.GetFileName(script.Path);
        _notificationService.ShowSuccess($"Executing {script.Name} on {folders.Count} folders...");
        
        try
        {
            string masterPrimary = ""; 
            string masterSecondary = ""; 
            string okeBasePath = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? "";
            string userName = await _database.GetAsync<string>("User.Name") ?? "USER";
            string outputBasePath = RootPath; 

            string lowerScript = scriptName.ToLower();

            if (lowerScript.Contains("wisuda"))
            {
                masterPrimary = await _database.GetAsync<string>("Configs.Master.Wisuda10RP") ?? "";
                masterSecondary = await _database.GetAsync<string>("Configs.Master.Wisuda8R") ?? "";
            }
            else if (lowerScript.Contains("manasik"))
            {
                masterPrimary = await _database.GetAsync<string>("Configs.Master.Manasik10RP") ?? "";
                masterSecondary = await _database.GetAsync<string>("Configs.Master.Manasik8R") ?? "";
            }
            else if (lowerScript.Contains("profesi"))
            {
                 masterPrimary = await _database.GetAsync<string>("Configs.Master.Profesi") ?? "";
                 masterSecondary = await _database.GetAsync<string>("Configs.Master.Sporty") ?? "";
            }
            else if (lowerScript.Contains("pasfoto") || lowerScript.Contains("pas_foto"))
            {
                 masterPrimary = await _database.GetAsync<string>("Configs.Master.PasFoto") ?? "";
            }
            else
            {
                 masterPrimary = await _database.GetAsync<string>("Configs.Master.LastTemplatePath") ?? "";
            }

            foreach (var folder in folders)
            {
                string CleanPath(string p) => string.IsNullOrEmpty(p) ? "" : p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var args = new List<string>
                {
                    "Scripts/batch_wrapper.py",
                    "--target", scriptName,
                    "--pilihan", CleanPath(folder.FullPath), 
                    "--master", CleanPath(masterPrimary),
                    "--master2", CleanPath(masterSecondary),
                    "--output", CleanPath(outputBasePath),
                    "--okebase", CleanPath(okeBasePath)
                };

                var envVars = new Dictionary<string, string> { { "BMACHINE_USER_NAME", userName } };

                await _platformService.RunPythonScriptAsync(
                    "Scripts/batch_wrapper.py",
                    args.Skip(1).ToList(), 
                    envVars,
                    onOutput: (s) => {},
                    onError: (s) => {}
                );
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Script error: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task CreateNewFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(CurrentPath, name);
        try 
        {
            Directory.CreateDirectory(path);
            Refresh();
        } 
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to create folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task CreateNewFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.Contains(".")) name += ".txt";
        
        var path = Path.Combine(CurrentPath, name);
        try 
        {
            await File.WriteAllTextAsync(path, "");
            Refresh();
        } 
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to create file: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DuplicateItem(int count)
    {
        var items = GetSelectedItems(null);
        if (!items.Any()) return;

        foreach (var item in items)
        {
            try
            {
                string dir = Path.GetDirectoryName(item.FullPath) ?? "";
                string name = Path.GetFileNameWithoutExtension(item.FullPath);
                string ext = Path.GetExtension(item.FullPath);

                for(int i=1; i<=count; i++)
                {
                     string newName = $"{name} - Copy ({i}){ext}";
                     string newPath = Path.Combine(dir, newName);
                     if (item.IsDirectory)
                     {
                         // Directory Copy (Recursive) - simplified for now
                         // Directory.CreateDirectory(newPath);
                         // CopyRecursive(item.FullPath, newPath);
                     }
                     else
                     {
                         File.Copy(item.FullPath, newPath);
                     }
                }
            }
            catch {}
        }
        Refresh();
    }
    
    [RelayCommand]
    public async Task BatchRenameFromClipboard()
    {
        var items = GetSelectedItems(null);
        if (!items.Any()) return;
        
        // order selection by name or original order?
        // usually user wants to rename in the order they see.
        // items is ObservableCollection or similar, usually preserving UI order if bound coupled with Sort.
        // But SelectedItems might be arbitrary order of selection.
        // We should sort them by current display order (SortBy).
        // For now, sorting by Name is safe default or by Index if available.
        // Let's sort by Name to be deterministic.
        var roundedItems = items.OrderBy(x => x.Name).ToList();

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
             var clipboard = desktop.MainWindow?.Clipboard;
             if (clipboard != null)
             {
                 var text = await clipboard.GetTextAsync();
                 if (string.IsNullOrWhiteSpace(text)) return;
                 
                 var names = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                 
                 if (names.Length == 0) return;
                 
                 int count = Math.Min(names.Length, roundedItems.Count);
                 
                 for (int i=0; i<count; i++)
                 {
                     var item = roundedItems[i];
                     var newName = names[i].Trim();
                     if (string.IsNullOrWhiteSpace(newName)) continue;
                     
                     // Keep extension if item is file and newName doesn't have it?
                     // "Extreme Logic" usually implies full replacement or smart replacement.
                     // If targeting files, usually we keep extension unless newName has one.
                     if (!item.IsDirectory)
                     {
                         string originalExt = Path.GetExtension(item.FullPath);
                         if (!newName.EndsWith(originalExt, StringComparison.OrdinalIgnoreCase))
                         {
                             newName += originalExt;
                         }
                     }
                     
                     // Rename
                     try
                     {
                         var dir = Path.GetDirectoryName(item.FullPath);
                         if (dir != null)
                         {
                             var newPath = Path.Combine(dir, newName);
                             if (item.IsDirectory) Directory.Move(item.FullPath, newPath);
                             else File.Move(item.FullPath, newPath);
                         }
                     }
                     catch (Exception ex) 
                     {
                         _notificationService.ShowError($"Failed to rename {item.Name}: {ex.Message}");
                     }
                 }
                 Refresh();
                 _notificationService.ShowSuccess($"Batch Renamed {count} items.");
             }
        }
    }

    [RelayCommand]
    public async Task CreateFolderShortcut()
    {
        string baseName = "New Folder";
        string name = baseName;
        int i = 2;
        while (Directory.Exists(Path.Combine(CurrentPath, name)))
        {
            name = $"{baseName} ({i++})";
        }
        
        await CreateNewFolder(name);
        
        // Find the new item
        // Items is ObservableCollection<object> (header/item), so filter
        var item = Items.OfType<ExplorerItemViewModel>().FirstOrDefault(x => x.Name == name);
        if (item != null)
        {
            item.IsEditing = true;
        }
    }

    [RelayCommand]
    public void CopyMasterBrowserHere(object? parameter)
    {
        string? folderPath = null;
        if (parameter is ExplorerItemViewModel item && item.IsDirectory)
            folderPath = item.FullPath;
        else
        {
            var first = GetSelectedItems(null).FirstOrDefault(x => x.IsDirectory);
            folderPath = first?.FullPath;
            if (string.IsNullOrEmpty(folderPath) && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
                folderPath = CurrentPath;
        }
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;
        WeakReferenceMessenger.Default.Send(new MasterBrowserSyncFromExplorerMessage(folderPath));
    }

    // --- POPUP STATE ---
    [ObservableProperty] private bool _isNewFolderVisible;
    [ObservableProperty] private bool _isNewFileVisible;
    [ObservableProperty] private bool _isDuplicateVisible;
    
    [ObservableProperty] private string _newItemName = "";
    [ObservableProperty] private int _duplicateCount = 1;

    [RelayCommand]
    public void OpenNewFolderPopup()
    {
        NewItemName = "New Folder";
        IsNewFolderVisible = true;
    }

    [RelayCommand]
    public void OpenNewFilePopup()
    {
        NewItemName = "New File.txt";
        IsNewFileVisible = true;
    }

    [RelayCommand]
    public void FocusPathBar()
    {
        WeakReferenceMessenger.Default.Send(new FocusExplorerPathBarMessage());
    }

    /// <summary>Focus search box (currently same as address bar until search UI exists).</summary>
    [RelayCommand]
    public void FocusSearchBox()
    {
        WeakReferenceMessenger.Default.Send(new FocusExplorerPathBarMessage());
    }

    /// <summary>Close current tab or window (Ctrl+W). Sends message; the window (or view) passes itself so the right window handles it.</summary>
    [RelayCommand]
    public void CloseTabOrWindow()
    {
        WeakReferenceMessenger.Default.Send(new RequestCloseExplorerWindowMessage(null));
    }

    /// <summary>New tab (stub: not implemented yet).</summary>
    [RelayCommand]
    private void NewTab()
    {
        // TODO: implement tabbed explorer
    }

    /// <summary>Switch to next tab (Ctrl+Tab). Sends message; the window handles cycling.</summary>
    [RelayCommand]
    private void SwitchTab()
    {
        WeakReferenceMessenger.Default.Send(new SwitchExplorerTabMessage());
    }

    /// <summary>Cycle view mode (List/Grid/Thumbnail) for Ctrl+Mouse Wheel.</summary>
    [RelayCommand]
    public void CycleViewMode(bool? wheelUp)
    {
        if (wheelUp == true)
        {
            LayoutMode = LayoutMode switch
            {
                ExplorerLayoutMode.Vertical => ExplorerLayoutMode.Horizontal,
                ExplorerLayoutMode.Horizontal => ExplorerLayoutMode.Thumbnail,
                ExplorerLayoutMode.Thumbnail => ExplorerLayoutMode.Vertical,
                _ => ExplorerLayoutMode.Vertical
            };
        }
        else if (wheelUp == false)
        {
            LayoutMode = LayoutMode switch
            {
                ExplorerLayoutMode.Vertical => ExplorerLayoutMode.Thumbnail,
                ExplorerLayoutMode.Thumbnail => ExplorerLayoutMode.Horizontal,
                ExplorerLayoutMode.Horizontal => ExplorerLayoutMode.Vertical,
                _ => ExplorerLayoutMode.Vertical
            };
        }
    }

    /// <summary>Select all items in the current folder (Ctrl+A).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        SelectedItems.Clear();
        foreach (var item in Items.OfType<ExplorerItemViewModel>())
            SelectedItems.Add(item);
    }

    [RelayCommand]
    public void OpenDuplicatePopup()
    {
        DuplicateCount = 1;
        IsDuplicateVisible = true;
    }

    [RelayCommand]
    public void ClosePopups()
    {
        IsNewFolderVisible = false;
        IsNewFileVisible = false;
        IsDuplicateVisible = false;
        NewItemName = "";
    }
    
    [RelayCommand]
    public async Task ConfirmNewFolder()
    {
        await CreateNewFolder(NewItemName);
        ClosePopups();
    }

    [RelayCommand]
    public async Task ConfirmNewFile()
    {
        await CreateNewFile(NewItemName);
        ClosePopups();
    }

    [RelayCommand]
    public async Task ConfirmDuplicate()
    {
        await DuplicateItem(DuplicateCount);
        ClosePopups();
    }
}

public partial class ExplorerItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    public string FullPath { get; set; } = "";
    public string TargetPath { get; set; } = ""; // For .lnk resolving
    public bool IsDirectory { get; set; }
    public System.DateTime DateModified { get; set; }
    public long Size { get; set; } // Bytes
    public int ItemsCount { get; set; }
    
    public string IconKey => IsDirectory ? "IconFolder" : "IconFile";
    public string DisplaySize => IsDirectory ? $"{ItemsCount} items" : BytesToString(Size);
    
    public bool IsSelectable => true;

    [ObservableProperty] private bool _isEditing;

    // --- Thumbnail support ---
    private static readonly string[] _imageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
    public bool IsImageFile => !IsDirectory && _imageExts.Contains(Path.GetExtension(FullPath).ToLowerInvariant());

    private Avalonia.Media.Imaging.Bitmap? _thumbnailImage;
    private bool _thumbnailLoaded;

    /// <summary>Lazy-loaded thumbnail bitmap (150×150 decode), null for non-images or if load fails.</summary>
    public Avalonia.Media.Imaging.Bitmap? ThumbnailImage
    {
        get
        {
            if (!_thumbnailLoaded)
            {
                _thumbnailLoaded = true;
                if (IsImageFile)
                    LoadThumbnailAsync();
            }
            return _thumbnailImage;
        }
    }

    private async void LoadThumbnailAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                using var stream = System.IO.File.OpenRead(FullPath);
                _thumbnailImage = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 150);
            });
            OnPropertyChanged(nameof(ThumbnailImage));
        }
        catch { /* Corrupt or inaccessible image: leave null */ }
    }

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

/// <summary>
/// Natural sort comparer that mimics Windows Explorer ordering.
/// Uses StrCmpLogicalW on Windows for correct "1, 2, 3, 10" ordering.
/// </summary>
public class NaturalSortComparer : System.Collections.Generic.IComparer<string?>
{
    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        try
        {
            return StrCmpLogicalW(x, y);
        }
        catch
        {
            // Fallback for non-Windows platforms
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}

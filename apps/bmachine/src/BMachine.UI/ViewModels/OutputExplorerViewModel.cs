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
    private readonly Services.IExplorerClipboard _clipboard;

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private bool _isAddressBarEditing;
    [ObservableProperty] private string _addressBarText = "";

    /// <summary>Display name of current folder (for title bar).</summary>
    public string CurrentFolderName => string.IsNullOrEmpty(CurrentPath) ? "" : Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    [ObservableProperty] private ObservableCollection<object> _items = new(); // Changed to object to support Headers
    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _splitLeftItems = new();
    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _splitRightItems = new();
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private bool _canNavigateUp;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isRootPathMissing;

    private readonly System.Collections.Generic.Stack<string> _backStack = new();
    private readonly System.Collections.Generic.Stack<string> _forwardStack = new();
    private bool _isLoadingSettings;

    private string? _pendingSelectName;
    private bool _pendingEdit;
    /// <summary>Guards the pending selection so a reload that lands BEFORE the file exists cannot eat it.</summary>
    private string? _pendingSelectWatchName;
    private int _pendingSelectAttempts;

    // Task Monitor
    public ObservableCollection<Services.FileTaskItem> ActiveTasks => _fileManager.ActiveTasks;

    public string RootPath { get; private set; } = "";

    public OutputExplorerViewModel(IDatabase database, INotificationService notificationService, Services.FileOperationManager fileManager, IPlatformService platformService, Services.IExplorerClipboard? clipboard = null)
    {
        _database = database;
        _notificationService = notificationService;
        _fileManager = fileManager;
        _platformService = platformService;
        _clipboard = clipboard ?? new Services.AvaloniaExplorerClipboard();

        _fileManager.ActiveTasks.CollectionChanged += OnActiveTasksChanged;
        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(StatusSummary));
            _ = UpdatePreviewAsync();
        };

        ExplorerSettingsVM = new ExplorerSettingsViewModel(database);

        // Default Sort (async, no blocking)
        LoadRootPath();
        _ = LoadScriptsAsync();
        _ = LoadExplorerShortcutsAsync();
        _ = LoadExplorerSettingsAsync();

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
        // Apply appearance changes (zoom / system icons) live without restart
        WeakReferenceMessenger.Default.Register<ExplorerSettingsChangedMessage>(this, (r, m) =>
        {
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var zoomStr = await _database.GetAsync<string>("Configs.Explorer.ContentZoom");
                if (double.TryParse(zoomStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cz) && cz >= 50 && cz <= 250)
                    ContentScale = cz / 100.0;

                var useStr = await _database.GetAsync<string>("Configs.Explorer.UseSystemIcons");
                var use = bool.TryParse(useStr, out var u) && u;
                if (use != UseSystemIcons) UseSystemIcons = use;

                var customIconPathStr = await _database.GetAsync<string>("Configs.Explorer.CustomIconPath") ?? "";
                if (customIconPathStr != CustomIconPath) CustomIconPath = customIconPathStr;
                var explorerFontFamily = await _database.GetAsync<string>("Configs.Explorer.FontFamily") ?? "";
                if (explorerFontFamily != ExplorerFontFamily) ExplorerFontFamily = explorerFontFamily;
                foreach (var item in QuickAccessItems) item.RefreshIcon();
            });
        });
    }

    // Resilience
    [ObservableProperty] private Avalonia.Controls.GridLength _taskMonitorHeight = Avalonia.Controls.GridLength.Auto;

    // Explorer Settings
    public ExplorerSettingsViewModel ExplorerSettingsVM { get; }

    [ObservableProperty] private bool _isSettingsVisible;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsVisible = !IsSettingsVisible;

    [RelayCommand]
    private void CloseSettings() => IsSettingsVisible = false;
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
    // Changed from "Back" to "Alt+Left" to prevent interfering with TextBox editing
    [ObservableProperty] private string _shortcutBackGesture = "Alt+Left";
    [ObservableProperty] private string _shortcutForwardGesture = "Alt+Right";
    [ObservableProperty] private string _shortcutRenameGesture = "F2";
    [ObservableProperty] private string _shortcutPermanentDeleteGesture = "Shift+Delete";
    [ObservableProperty] private string _shortcutFocusSearchBoxGesture = "Ctrl+F";
    [ObservableProperty] private string _shortcutAddressBarGesture = "Alt+D";
    [ObservableProperty] private string _shortcutSwitchTabGesture = "Ctrl+Tab";
    [ObservableProperty] private string _shortcutRefreshGesture = "F5";
    [ObservableProperty] private string _shortcutCopyGesture = "Ctrl+C";
    [ObservableProperty] private string _shortcutCutGesture = "Ctrl+X";
    [ObservableProperty] private string _shortcutPasteGesture = "Ctrl+V";
    [ObservableProperty] private string _shortcutCopyPathGesture = "Ctrl+Shift+C";
    [ObservableProperty] private string _shortcutPastePathGesture = "Ctrl+Shift+V";

    // View Layout Shortcuts (1-8)
    [ObservableProperty] private string _shortcutLayout1Gesture = "Ctrl+Shift+D1"; // Extra Large
    [ObservableProperty] private string _shortcutLayout2Gesture = "Ctrl+Shift+D2"; // Large
    [ObservableProperty] private string _shortcutLayout3Gesture = "Ctrl+Shift+D3"; // Medium
    [ObservableProperty] private string _shortcutLayout4Gesture = "Ctrl+Shift+D4"; // Split (was Small)
    [ObservableProperty] private string _shortcutLayout5Gesture = "Ctrl+Shift+D5"; // List
    [ObservableProperty] private string _shortcutLayout6Gesture = "Ctrl+Shift+D6"; // Details
    [ObservableProperty] private string _shortcutLayout7Gesture = "Ctrl+Shift+D7"; // Tiles
    [ObservableProperty] private string _shortcutLayout8Gesture = "Ctrl+Shift+D8"; // Content

    // Preview panel (right side): .txt content, .docx placeholder
    [ObservableProperty] private bool _isPreviewPanelVisible;
    [ObservableProperty] private string _previewPanelTitle = "";
    [ObservableProperty] private string _previewPanelContent = "";

    public Avalonia.Controls.GridLength PreviewPanelWidth => IsPreviewPanelVisible ? new Avalonia.Controls.GridLength(280) : new Avalonia.Controls.GridLength(0);

    partial void OnIsPreviewPanelVisibleChanged(bool value) => OnPropertyChanged(nameof(PreviewPanelWidth));

    [RelayCommand]
    private void ClosePreviewPanel() => IsPreviewPanelVisible = false;

    // Search Functionality
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchVisible;

    // Type Ahead (Linux Mint style)
    [ObservableProperty] private string _typeAheadText = "";
    [ObservableProperty] private bool _isTypeAheadVisible = false;
    /// <summary>
    /// The search input lives in the floating pill (bottom centre) in every host - window and inline
    /// panel - so it is visible whenever the search is open, with or without the path bar.
    /// </summary>
    public bool ShowFloatingSearch => IsSearchVisible;

    /// <summary>True while a query is typed (drives the search icon in the empty state).</summary>
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Enter in the search pill opens the first hit - "type a few letters, press Enter", the fastest
    /// way through a deep tree and the reflex Explorer trains.
    /// </summary>
    [RelayCommand]
    private void OpenFirstSearchResult()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        var first = Items.OfType<ExplorerItemViewModel>().FirstOrDefault();
        if (first != null) OpenItem(first);
    }

    /// <summary>Hard cap so a search across a huge tree cannot run away.</summary>
    private const int SearchResultCap = 5000;

    /// <summary>True while the background walk of the recursive search is still running.</summary>
    [ObservableProperty] private bool _isSearching;

    /// <summary>True when the search stopped at <see cref="SearchResultCap"/> hits.</summary>
    [ObservableProperty] private bool _searchTruncated;

    /// <summary>Cancels the walk of the previous query when a new one starts.</summary>
    private System.Threading.CancellationTokenSource? _searchRunCts;

    /// <summary>Status next to the search input: "Mencari...", "N hasil", "Tidak ada hasil".</summary>
    public string SearchResultSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return string.Empty;
            if (IsSearching) return "Mencari...";

            int count = Items.OfType<ExplorerItemViewModel>().Count();
            if (count == 0) return "Tidak ada hasil";

            var text = count == 1 ? "1 hasil" : $"{count} hasil";
            return SearchTruncated ? text + " (dibatasi)" : text;
        }
    }

    /// <summary>Empty-state text: a search with no hits says so instead of "folder kosong".</summary>
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText)
        ? "Folder kosong."
        : $"Tidak ada hasil untuk \"{SearchText}\" di folder ini atau subfoldernya.";


    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(SearchResultSummary));

    partial void OnSearchTruncatedChanged(bool value) => OnPropertyChanged(nameof(SearchResultSummary));

    partial void OnIsSearchVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFloatingSearch));
        OnPropertyChanged(nameof(SearchResultSummary));
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchText = "";
            // Focus returns to list? Handled by View if needed.
        }
        else
        {
            // Request Focus on SearchBox via Messenger or View implementation
            WeakReferenceMessenger.Default.Send(new FocusSearchBoxMessage());
        }
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchVisible = false;
        SearchText = "";
    }

    /// <summary>Cancels the previous debounce wait when a new keystroke arrives.</summary>
    private System.Threading.CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    partial void OnSearchTextChanged(string value)
    {
        // Abort the walk of the previous query (no point finishing a stale search).
        try { _searchRunCts?.Cancel(); } catch { }
        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(SearchResultSummary));

        // Debounced: a full (recursive) reload per keystroke froze the UI on large folders.
        // Task.Delay (not DispatcherTimer) so the wait also works in headless test hosts.
        _searchDebounceCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedSearchReloadAsync(cts);
    }

    private async System.Threading.Tasks.Task DebouncedSearchReloadAsync(System.Threading.CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            await System.Threading.Tasks.Task.Delay(SearchDebounceMs, token);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!token.IsCancellationRequested) LoadItems();
        });
    }

    partial void OnCurrentPathChanged(string value) => OnPropertyChanged(nameof(CurrentFolderName));

    partial void OnSidebarWidthChanged(Avalonia.Controls.GridLength value)
    {
        if (value.IsAbsolute)
        {
            _database.SetAsync("Configs.Explorer.SidebarWidth", value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [ObservableProperty] private double _contentScale = 1.0; // 1.0 = 100% zoom
    [ObservableProperty] private bool _useSystemIcons;       // Windows shell icons (7tsp themes)
    [ObservableProperty] private string _customIconPath = ""; // Custom 7tsp icon pack path
    [ObservableProperty] private string _explorerFontFamily = "";

    public string ExplorerFontFamilyOrDefault => ExplorerFontFamily;

    partial void OnUseSystemIconsChanged(bool value)
    {
        ExplorerItemViewModel.PushSystemIconsSetting(value);
        foreach (var item in Items.OfType<ExplorerItemViewModel>()) item.ApplySystemIconMode(value);
        foreach (var item in SplitLeftItems) item.ApplySystemIconMode(value);
        foreach (var item in SplitRightItems) item.ApplySystemIconMode(value);
    }

    partial void OnCustomIconPathChanged(string value)
    {
        ExplorerItemViewModel.PushCustomIconPathSetting(value);
        foreach (var item in Items.OfType<ExplorerItemViewModel>()) item.ReloadSystemIcon();
        foreach (var item in SplitLeftItems) item.ReloadSystemIcon();
        foreach (var item in SplitRightItems) item.ReloadSystemIcon();
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
                    var name = d.Name;
                    if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(d.Path))
                    {
                        name = System.IO.Path.GetFileName(d.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                    }

                    QuickAccessItems.Add(new SidebarItemViewModel
                    {
                        Name = name ?? "",
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
        var explorerPath = await _database.GetAsync<string>("Configs.Explorer.DefaultPath");
        var localOutput = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";
        var path = !string.IsNullOrWhiteSpace(explorerPath) ? explorerPath : localOutput;

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

            // Load Appearance (zoom + system icons)
            var zoomStr = await _database.GetAsync<string>("Configs.Explorer.ContentZoom");
            if (double.TryParse(zoomStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double zoom) && zoom >= 50 && zoom <= 250)
                ContentScale = zoom / 100.0;
            var useIconsStr = await _database.GetAsync<string>("Configs.Explorer.UseSystemIcons");
            UseSystemIcons = bool.TryParse(useIconsStr, out var useI) && useI;

            var customIconPathStr = await _database.GetAsync<string>("Configs.Explorer.CustomIconPath");
            CustomIconPath = customIconPathStr ?? "";
            ExplorerFontFamily = await _database.GetAsync<string>("Configs.Explorer.FontFamily") ?? "";
            ExplorerItemViewModel.PushCustomIconPathSetting(CustomIconPath);

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

        var shortcutCopy = await _database.GetAsync<string>("Configs.Explorer.ShortcutCopy");
        if (!string.IsNullOrEmpty(shortcutCopy)) ShortcutCopyGesture = shortcutCopy;
        var shortcutCut = await _database.GetAsync<string>("Configs.Explorer.ShortcutCut");
        if (!string.IsNullOrEmpty(shortcutCut)) ShortcutCutGesture = shortcutCut;
        var shortcutPaste = await _database.GetAsync<string>("Configs.Explorer.ShortcutPaste");
        if (!string.IsNullOrEmpty(shortcutPaste)) ShortcutPasteGesture = shortcutPaste;
        var shortcutCopyPath = await _database.GetAsync<string>("Configs.Explorer.ShortcutCopyPath");
        if (!string.IsNullOrEmpty(shortcutCopyPath)) ShortcutCopyPathGesture = shortcutCopyPath;
        var shortcutPastePath = await _database.GetAsync<string>("Configs.Explorer.ShortcutPastePath");
        if (!string.IsNullOrEmpty(shortcutPastePath)) ShortcutPastePathGesture = shortcutPastePath;
    }

    private async Task LoadExplorerSettingsAsync()
    {
        var zoomStr = await _database.GetAsync<string>("Configs.Explorer.ContentZoom");
        if (double.TryParse(zoomStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cz) && cz >= 50 && cz <= 250)
            ContentScale = cz / 100.0;

        var useStr = await _database.GetAsync<string>("Configs.Explorer.UseSystemIcons");
        var use = bool.TryParse(useStr, out var u) && u;
        UseSystemIcons = use;

        var customIconPathStr = await _database.GetAsync<string>("Configs.Explorer.CustomIconPath") ?? "";
        CustomIconPath = customIconPathStr;

        var explorerFontFamily = await _database.GetAsync<string>("Configs.Explorer.FontFamily") ?? "";
        ExplorerFontFamily = explorerFontFamily;

        // Notify view that font is loaded so it can re-apply styling
        OnPropertyChanged(nameof(ExplorerFontFamily));
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
    private void ActivateAddressBar()
    {
        AddressBarText = CurrentPath;
        IsAddressBarEditing = true;
    }

    [RelayCommand]
    private void CancelAddressBar()
    {
        IsAddressBarEditing = false;
        AddressBarText = CurrentPath;
    }

    [RelayCommand]
    private void CommitAddressBar()
    {
        var input = AddressBarText.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            CancelAddressBar();
            return;
        }

        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
            if (!Directory.Exists(path))
            {
                _notificationService.ShowError("The folder path does not exist.", "Address Bar");
                CancelAddressBar();
                return;
            }

            NavigateTo(path);
            IsAddressBarEditing = false;
        }
        catch
        {
            _notificationService.ShowError("The folder path is invalid.", "Address Bar");
            CancelAddressBar();
        }
    }

    [RelayCommand]
    public void NavigateTo(string path)
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
        // Opening a folder (breadcrumb / double-click / Up / Back) leaves search mode, exactly like
        // Explorer: the new folder is listed normally instead of being filtered by the old query.
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
        }

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
    public enum ExplorerLayoutMode { Vertical, Horizontal, Thumbnail, Tiles, Split }
    public enum ExplorerGroupBy { None, Date, Type }

    [ObservableProperty] private ExplorerSortOption _sortBy = ExplorerSortOption.Name;
    [ObservableProperty] private bool _isSortDescending = false;
    [ObservableProperty] private ExplorerLayoutMode _layoutMode = ExplorerLayoutMode.Vertical;
    [ObservableProperty] private double _thumbnailSize = 140; // Default Medium
    public double ThumbnailHeight => ThumbnailSize + 40;
    partial void OnThumbnailSizeChanged(double value) => OnPropertyChanged(nameof(ThumbnailHeight));

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
        // TeraCopy-style: show it without stealing focus from the current app/window.
        if (s_sharedTaskMonitorWindow != null)
        {
            try
            {
                // Rebind: the window is shared process-wide and must follow the manager that
                // is actually running the tasks — never a stale tab's view model.
                s_sharedTaskMonitorWindow.DataContext = new FileOperationsMonitorViewModel(_fileManager);
                // Do NOT Activate() — that would steal focus every time a new task starts.
                // Just make sure the window is visible.
                if (!s_sharedTaskMonitorWindow.IsVisible)
                    s_sharedTaskMonitorWindow.Show();
                return;
            }
            catch { /* window may be closed */ }
            s_sharedTaskMonitorWindow = null;
        }

        _taskMonitorWindow = new TaskMonitorWindow();
        _taskMonitorWindow.DataContext = new FileOperationsMonitorViewModel(_fileManager);
        _taskMonitorWindow.LoadPositionAsync = LoadTaskMonitorPositionAsync;
        _taskMonitorWindow.SavePositionAsync = SaveTaskMonitorPositionAsync;
        _taskMonitorWindow.Closed += (s, e) =>
        {
            _taskMonitorWindow = null;
            s_sharedTaskMonitorWindow = null;
        };
        s_sharedTaskMonitorWindow = _taskMonitorWindow;
        _ = RestoreTaskMonitorPositionAsync(_taskMonitorWindow);
        _taskMonitorWindow.Show();

        // SizeToContent="Height": tinggi aktual baru diketahui setelah layout pertama.
        // Hitung ulang posisi dock kanan-bawah sekali, hanya bila user belum sempat memindah.
        var shownWindow = _taskMonitorWindow;
        var shownPos = shownWindow.Position;
        System.EventHandler? relayout = null;
        relayout = (_, _) =>
        {
            shownWindow.LayoutUpdated -= relayout;
            if (shownWindow.Position == shownPos) PositionTaskMonitorWindow(shownWindow);
        };
        shownWindow.LayoutUpdated += relayout;
    }

    private const string TaskMonitorPositionKey = "Configs.FileOperations.WindowPosition";

    /// <summary>Simpan posisi terakhir window File Operations (x,y) ke database.</summary>
    private async System.Threading.Tasks.Task SaveTaskMonitorPositionAsync(Avalonia.PixelPoint pos)
    {
        try { await _database.SetAsync(TaskMonitorPositionKey, $"{pos.X},{pos.Y}"); }
        catch { /* best-effort */ }
    }

    /// <summary>Baca posisi tersimpan. Null jika belum ada atau format rusak.</summary>
    private async System.Threading.Tasks.Task<Avalonia.PixelPoint?> LoadTaskMonitorPositionAsync()
    {
        try
        {
            var raw = await _database.GetAsync<string>(TaskMonitorPositionKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split(',');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
            if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var y)) return null;
            return new Avalonia.PixelPoint(x, y);
        }
        catch { return null; }
    }

    /// <summary>
    /// Pakai posisi tersimpan bila masih ada di salah satu layar; kalau tidak, pojok kanan-bawah.
    /// </summary>
    private async System.Threading.Tasks.Task RestoreTaskMonitorPositionAsync(TaskMonitorWindow window)
    {
        var saved = await LoadTaskMonitorPositionAsync();
        if (saved is { } p && IsPointOnAnyScreen(window, p))
        {
            window.Position = p;
            return;
        }
        PositionTaskMonitorWindow(window);
    }

    private static bool IsPointOnAnyScreen(TaskMonitorWindow window, Avalonia.PixelPoint p)
    {
        var screens = window.Screens?.All;
        if (screens == null) return false;
        foreach (var s in screens)
        {
            var a = s.WorkingArea;
            if (p.X >= a.X && p.X + 40 <= a.X + a.Width && p.Y >= a.Y && p.Y + 40 <= a.Y + a.Height) return true;
        }
        return false;
    }

    /// <summary>Place the File Operations window at the bottom-right of the primary screen (TeraCopy-style).</summary>
    private static void PositionTaskMonitorWindow(TaskMonitorWindow window)
    {
        try
        {
            var screen = window.Screens?.Primary ?? window.Screens?.All?.FirstOrDefault();
            if (screen == null) return;

            var area = screen.WorkingArea;
            int w = (int)(window.Width > 0 ? window.Width : 400);
            int h = (int)(window.Height > 0 ? window.Height : 200);
            int margin = 16;

            int x = area.X + area.Width - w - margin;
            int y = area.Y + area.Height - h - margin;

            window.Position = new Avalonia.PixelPoint(x, y);
        }
        catch { /* positioning is best-effort */ }
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
            await _database.SetAsync("Configs.Explorer.DefaultViewMode", LayoutMode.ToString());
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
                var gMode = await _database.GetAsync<string>("Configs.Explorer.DefaultViewMode");
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
    public bool IsVerticalLayout
    {
        get => LayoutMode == ExplorerLayoutMode.Vertical;
        set { if (value) LayoutMode = ExplorerLayoutMode.Vertical; }
    }

    public bool IsHorizontalLayout
    {
        get => LayoutMode == ExplorerLayoutMode.Horizontal;
        set { if (value) LayoutMode = ExplorerLayoutMode.Horizontal; }
    }

    public bool IsThumbnailLayout
    {
        get => LayoutMode == ExplorerLayoutMode.Thumbnail;
        set { if (value) LayoutMode = ExplorerLayoutMode.Thumbnail; }
    }

    public bool IsTilesLayout
    {
        get => LayoutMode == ExplorerLayoutMode.Tiles;
        set { if (value) LayoutMode = ExplorerLayoutMode.Tiles; }
    }

    public bool IsSplitLayout
    {
        get => LayoutMode == ExplorerLayoutMode.Split;
        set { if (value) LayoutMode = ExplorerLayoutMode.Split; }
    }

    public bool IsSplitLayoutAndNotEmpty => IsSplitLayout && !IsEmpty;

    public bool IsVerticalLayoutAndNotEmpty => IsVerticalLayout && !IsEmpty;
    public bool IsHorizontalLayoutAndNotEmpty => IsHorizontalLayout && !IsEmpty;
    public bool IsThumbnailLayoutAndNotEmpty => IsThumbnailLayout && !IsEmpty;
    public bool IsTilesLayoutAndNotEmpty => IsTilesLayout && !IsEmpty;


    // partial void OnLayoutModeChanged(ExplorerLayoutMode value) // Removed duplicate partial method
    // {
    //    OnLayoutChanged_UI();
    // }

    private void OnLayoutChanged_UI()
    {
        OnPropertyChanged(nameof(IsVerticalLayout));
        OnPropertyChanged(nameof(IsHorizontalLayout));
        OnPropertyChanged(nameof(IsThumbnailLayout));
        OnPropertyChanged(nameof(IsTilesLayout));
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsThumbnailLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsTilesLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsSplitLayout));
        OnPropertyChanged(nameof(IsSplitLayoutAndNotEmpty));
        LoadItems_UIOnly();
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVerticalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsHorizontalLayoutAndNotEmpty));
        OnPropertyChanged(nameof(IsThumbnailLayoutAndNotEmpty));
    }

    [ObservableProperty] private ObservableCollection<ExplorerItemViewModel> _selectedItems = new();

    /// <summary>
    /// Paths that were cut (move on paste). Process-wide: a cut in one tab must be pasteable
    /// as a MOVE in another tab/window (per-VM state silently downgraded it to a copy).
    /// Cleared on Copy or after a successful move-paste.
    /// </summary>
    private static readonly object CutStateLock = new();
    private static System.Collections.Generic.HashSet<string>? s_cutPaths;

    private static System.Collections.Generic.HashSet<string>? CutPaths
    {
        get { lock (CutStateLock) return s_cutPaths; }
        set { lock (CutStateLock) s_cutPaths = value; }
    }

    /// <summary>Dims the cut items (and only those) so Cut is distinguishable from Copy.</summary>
    private void MarkCutItems(System.Collections.Generic.HashSet<string>? cutSet)
    {
        foreach (var item in Items.OfType<ExplorerItemViewModel>())
        {
            item.IsCut = cutSet != null && cutSet.Contains(item.FullPath);
        }
    }

    /// <summary>True when the given clipboard payload is exactly the set that was cut.</summary>
    private static bool IsCutPayload(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        var cut = CutPaths;
        return cut != null && paths.Count == cut.Count && paths.All(p => cut.Contains(p));
    }

    // ---- Undo (Ctrl+Z) -------------------------------------------------------------------
    // Reverses the last reversible operation started FROM this explorer: new folder/file,
    // rename, batch rename, copy-paste/duplicate (delete the created copies) and move
    // (move it back). A recycle-bin delete cannot be restored programmatically here, so its
    // entry explains that instead of pretending to undo it.

    private readonly System.Collections.Generic.List<ExplorerUndoEntry> _undoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    private sealed class ExplorerUndoEntry
    {
        public string Kind { get; init; } = "";
        public string Path { get; init; } = "";
        public string OtherPath { get; init; } = "";
        public string Description { get; init; } = "";
        public System.Collections.Generic.List<(string Old, string New)> Pairs { get; init; } = new();
    }

    private void PushUndo(ExplorerUndoEntry entry)
    {
        _undoStack.Add(entry);
        while (_undoStack.Count > 50) _undoStack.RemoveAt(0);
        OnPropertyChanged(nameof(CanUndo));
    }

    /// <summary>Ctrl+Z — undo the last reversible file operation.</summary>
    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            _notificationService.ShowInfo("Nothing to undo.");
            return;
        }

        var entry = _undoStack[^1];

        try
        {
            switch (entry.Kind)
            {
                case "new":
                    if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, true);
                    else if (File.Exists(entry.Path)) File.Delete(entry.Path);
                    break;

                case "rename":
                    if (Directory.Exists(entry.Path) && !Directory.Exists(entry.OtherPath))
                        Directory.Move(entry.Path, entry.OtherPath);
                    else if (File.Exists(entry.Path) && !File.Exists(entry.OtherPath))
                        File.Move(entry.Path, entry.OtherPath);
                    else
                    {
                        _notificationService.ShowError("Undo failed: the item is no longer where the rename left it.");
                        return;
                    }
                    break;

                case "batchrename":
                    for (int i = entry.Pairs.Count - 1; i >= 0; i--)
                    {
                        var (oldPath, newPath) = entry.Pairs[i];
                        if (Directory.Exists(newPath) && !Directory.Exists(oldPath)) Directory.Move(newPath, oldPath);
                        else if (File.Exists(newPath) && !File.Exists(oldPath)) File.Move(newPath, oldPath);
                    }
                    break;

                case "copy":
                    foreach (var created in entry.Pairs)
                    {
                        var dest = created.New;
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        else if (File.Exists(dest)) File.Delete(dest);
                        else
                        {
                            _notificationService.ShowError("Undo failed: the copy is still running.");
                            return;
                        }
                    }
                    break;

                case "move":
                    foreach (var moved in entry.Pairs)
                    {
                        var (src, dst) = (moved.Old, moved.New);
                        if (Directory.Exists(dst) && !Directory.Exists(src)) Directory.Move(dst, src);
                        else if (File.Exists(dst) && !File.Exists(src)) File.Move(dst, src);
                        else
                        {
                            _notificationService.ShowError("Undo failed: the move is still running.");
                            return;
                        }
                    }
                    break;

                case "recycle":
                    _undoStack.RemoveAt(_undoStack.Count - 1);
                    OnPropertyChanged(nameof(CanUndo));
                    _notificationService.ShowInfo("Deleted to the Recycle Bin — restore it from there.");
                    return;
            }

            _undoStack.RemoveAt(_undoStack.Count - 1);
            OnPropertyChanged(nameof(CanUndo));
            _notificationService.ShowSuccess($"Undo: {entry.Description}");
            LoadItems();
        }
        catch (System.Exception ex)
        {
            _notificationService.ShowError($"Undo failed: {ex.Message}");
        }
    }

    /// <summary>True when at least one item is selected. For context menu: show Copy/Cut only when true.</summary>
    public bool HasSelection => SelectedItems?.Count > 0;

    /// <summary>Explorer-style status line: "N items" (plus ", M selected" for a multi-selection).</summary>
    public string StatusSummary
    {
        get
        {
            int count = Items.OfType<ExplorerItemViewModel>().Count();
            if (count == 0) return string.Empty;

            int selected = SelectedItems?.Count ?? 0;
            return selected > 1 ? $"{count} items, {selected} selected" : $"{count} items";
        }
    }

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
    /// <summary>Call before showing context menu (e.g. on right-click) to refresh Paste visibility.</summary>
    public async void UpdateClipboardStateAsync()
    {
        try
        {
            var hasFiles = await _clipboard.HasFilePayloadAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HasFileClipboard = hasFiles);
        }
        catch { Avalonia.Threading.Dispatcher.UIThread.Post(() => HasFileClipboard = false); }
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

    public event Action<string>? RequestOpenNewTab;

    [RelayCommand]
    public void OpenItemInNewTab(object parameter)
    {
        if (parameter is ExplorerItemViewModel item && item.IsDirectory)
        {
            var path = !string.IsNullOrEmpty(item.TargetPath) ? item.TargetPath : item.FullPath;
            RequestOpenNewTab?.Invoke(path);
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
    public async Task CopyItem(object? parameter)
    {
        CutPaths = null; // Copy clears cut intent
        MarkCutItems(null);
        var items = GetSelectedItems(parameter);
        if (!items.Any())
        {
            _notificationService.ShowError("No items selected.");
            return;
        }

        var paths = items.Select(x => x.FullPath).ToArray();
        if (await _clipboard.SetFilePathsAsync(paths))
            UpdateClipboardStateAsync();
        else
            _notificationService.ShowError("Clipboard is not available (CopyItem).");
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

        var paths = items.Select(x => x.FullPath).ToArray();
        CutPaths = new System.Collections.Generic.HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        MarkCutItems(new System.Collections.Generic.HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));

        if (await _clipboard.SetFilePathsAsync(paths))
            UpdateClipboardStateAsync();
        else
            _notificationService.ShowError("Clipboard is not available (CutItem).");
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
            var fileList = await _clipboard.GetFilePathsAsync();
            if (fileList == null || fileList.Count == 0)
            {
                _notificationService.ShowError("Tidak ada file atau folder di clipboard.");
                return;
            }

            var normalizedList = fileList.Select(f => f.Trim()).ToList();
            bool isMove = IsCutPayload(normalizedList);

            // Reserved names keep a multi-item paste from computing the same destination twice
            // while the background copies have not hit the disk yet.
            var reserved = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentNormalized = CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var queued = new System.Collections.Generic.List<(string Old, string New)>();
            int count = 0;

            foreach (var cleanRaw in normalizedList)
            {
                var cleanPath = cleanRaw.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(cleanPath)) continue;
                if (!File.Exists(cleanPath) && !Directory.Exists(cleanPath)) continue;

                // Cut + paste into the folder the item already lives in is a no-op in Windows
                // Explorer (before: the original was renamed to a "- Copy" name).
                var sourceFolder = (Path.GetDirectoryName(cleanPath) ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (isMove && string.Equals(sourceFolder, currentNormalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Pasting a folder into its own subtree would recurse forever (copy) or throw (move).
                if (Directory.Exists(cleanPath) && IsPathInside(cleanPath, CurrentPath))
                {
                    _notificationService.ShowError($"Cannot paste '{Path.GetFileName(cleanPath)}' into its own folder.");
                    continue;
                }

                // ONE shared naming rule for paste / drop / duplicate: "x - Copy", "x - Copy (2)", ...
                var dest = Services.FileNameDeduplicator.BuildCopyPath(CurrentPath, cleanPath, reserved);
                reserved.Add(Path.GetFileName(dest));

                if (isMove)
                    _fileManager.MoveFileBackground(cleanPath, dest);
                else
                    _fileManager.CopyFileBackground(cleanPath, dest);
                queued.Add((cleanPath, dest));
                count++;
            }

            if (queued.Count > 0)
            {
                PushUndo(new ExplorerUndoEntry
                {
                    Kind = isMove ? "move" : "copy",
                    Description = isMove ? $"move {queued.Count} item(s)" : $"paste {queued.Count} item(s)",
                    Pairs = queued
                });

                // Select the pasted item once the background operation refreshes the list.
                var last = queued[^1];
                var lastFolder = (Path.GetDirectoryName(last.New) ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(lastFolder, currentNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingSelectName = Path.GetFileName(last.New);
                    _pendingEdit = false;
                }
            }

            if (count > 0 && isMove)
            {
                CutPaths = null;
                MarkCutItems(null);
                // Clear file data from clipboard so paste elsewhere doesn't move again
                await _clipboard.ClearAsync();
                UpdateClipboardStateAsync();
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
    public void HandleDroppedFiles(System.Collections.Generic.IEnumerable<string> paths, bool isMove = false)
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;

        var reservedDropNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var sourcePathRaw in paths)
        {
            // Folder dari drag sering punya trailing backslash -> GetFileName() kosong -> skip.
            var sourcePath = sourcePathRaw.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(sourcePath)) continue;

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) continue;

            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)) continue;

            // Never copy/move a folder into its own subtree: that recurses until the path limit
            // and fills the disk.
            if (Directory.Exists(sourcePath) && IsPathInside(sourcePath, CurrentPath))
            {
                _notificationService.ShowError($"Cannot copy '{fileName}' into its own folder.");
                continue;
            }

            string dest = Path.Combine(CurrentPath, fileName);

            // Handle duplicate names (same rule as paste / duplicate)
            if (sourcePath.Equals(dest, StringComparison.OrdinalIgnoreCase)) continue; // Skip same file
            dest = Services.FileNameDeduplicator.BuildCopyPath(CurrentPath, sourcePath, reservedDropNames);

            reservedDropNames.Add(Path.GetFileName(dest));
            if (isMove) _fileManager.MoveFileBackground(sourcePath, dest);
            else _fileManager.CopyFileBackground(sourcePath, dest);
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

                PushUndo(new ExplorerUndoEntry
                {
                    Kind = "rename",
                    Path = newPath,
                    OtherPath = oldPath,
                    Description = $"rename to '{item.Name}'"
                });

                // Update Model
                item.FullPath = newPath;
                // Keep the renamed item selected after the reload (Explorer does the same)
                _pendingSelectName = item.Name;
                _pendingEdit = false;
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

    /// <summary>
    /// Permanent delete (bypass Recycle Bin). Shift+Delete.
    /// Irreversible, so it always asks first — Explorer does the same. A "no" (or a missing
    /// confirm UI) does nothing at all.
    /// </summary>
    [RelayCommand]
    public async Task PermanentDeleteItem(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (items.Count == 0) return;

        var label = items.Count == 1 ? $"'{items[0].Name}'" : $"{items.Count} item";
        var confirmed = await _notificationService.ShowConfirmAsync(
            $"Hapus permanen {label}? Tindakan ini tidak bisa dibatalkan.",
            "Hapus Permanen");

        if (!confirmed) return;

        DeleteItemsInternal(parameter, useRecycleBin: false);
    }

    private void DeleteItemsInternal(object? parameter, bool useRecycleBin)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        // Safety guard: never delete the folder we are standing in, nor one of its ancestors.
        // That is what protects against a stale selection of the folder we just navigated INTO.
        // Anything else is allowed — including a search hit that lives in a subfolder, which the
        // old "parent must equal CurrentPath" rule silently refused to delete.
        var currentNormalized = (CurrentPath ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var safeItems = items.Where(item =>
        {
            var itemPath = item.FullPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(itemPath)) return false;

            if (string.Equals(itemPath, currentNormalized, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsPathInside(itemPath, CurrentPath)) return false;

            return true;
        }).ToList();

        if (!safeItems.Any()) return;

        int deletedCount = 0;
        foreach (var item in safeItems)
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
                    _fileManager.DeleteFileBackground(item.FullPath);
                    deletedCount++;
                }
            }
            catch (System.Exception ex)
            {
                _notificationService.ShowError($"Failed to delete {item.Name}: {ex.Message}");
            }
        }

        if (deletedCount > 0)
        {
            if (useRecycleBin)
                PushUndo(new ExplorerUndoEntry { Kind = "recycle", Description = $"delete {deletedCount} item(s)" });
            LoadItems();
        }
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


    private static string ResolveShortcutTarget(string shortcutPath)
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

    private void UpdateSplitView(System.Collections.Generic.IEnumerable<ExplorerItemViewModel> items)
    {
        SplitLeftItems.Clear();
        SplitRightItems.Clear();

        var list = items.ToList();
        if (list.Count == 0) return;

        // "Beda Extension" Logic
        var extensions = list.Where(x => !x.IsDirectory)
                             .Select(x => Path.GetExtension(x.FullPath).ToLowerInvariant())
                             .Distinct()
                             .ToList();

        if (extensions.Count >= 2)
        {
            // We have varied extensions.
            // Strategy: Group by extension, Sort by count.
            // Take the most frequent extension (or primary grouping) for Left, rest for Right.
            // Or if user meant "Split by type", maybe just 2 types?
            // Let's try: Left = most common extension, Right = everything else.

            var groups = list.GroupBy(x => x.IsDirectory ? "Folder" : Path.GetExtension(x.FullPath).ToLowerInvariant())
                             .OrderByDescending(g => g.Count())
                             .ToList();

            // Put first group in Left
            foreach(var item in groups.First()) SplitLeftItems.Add(item);

            // Put rest in Right
            foreach(var group in groups.Skip(1))
            {
                foreach(var item in group) SplitRightItems.Add(item);
            }
        }
        else
        {
            // Single extension or just folders. Split in half.
            int mid = (int)Math.Ceiling(list.Count / 2.0);
            for(int i=0; i<mid; i++) SplitLeftItems.Add(list[i]);
            for(int i=mid; i<list.Count; i++) SplitRightItems.Add(list[i]);
        }
    }

    // --- FileSystemWatcher for Auto-Refresh ---
    private FileSystemWatcher? _folderWatcher;
    private System.Threading.CancellationTokenSource? _watcherDebounceCts;
    private const int WatcherDebounceMs = 400;

    /// <summary>
    /// Keeps ONE watcher per folder. The old code disposed and recreated the watcher on every
    /// LoadItems(), which reset the debounce timestamp and made the guard meaningless.
    /// </summary>
    private void SetupFolderWatcher(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            DisposeFolderWatcher();
            return;
        }

        if (_folderWatcher != null && string.Equals(_folderWatcher.Path, path, System.StringComparison.OrdinalIgnoreCase))
            return; // same folder: keep the live watcher (do NOT recreate)

        DisposeFolderWatcher();
        try
        {
            _folderWatcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false
            };
            _folderWatcher.Created += OnFolderChanged;
            _folderWatcher.Deleted += OnFolderChanged;
            _folderWatcher.Renamed += OnFolderChanged;
            _folderWatcher.EnableRaisingEvents = true;
        }
        catch { /* Ignore access exceptions */ }
    }

    private void DisposeFolderWatcher()
    {
        try { _watcherDebounceCts?.Cancel(); } catch { }

        if (_folderWatcher != null)
        {
            try
            {
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Created -= OnFolderChanged;
                _folderWatcher.Deleted -= OnFolderChanged;
                _folderWatcher.Renamed -= OnFolderChanged;
                _folderWatcher.Dispose();
            }
            catch { }
            _folderWatcher = null;
        }
    }

    /// <summary>
    /// Restart-timer debounce: EVERY change reschedules a single reload, so no change event is
    /// silently dropped (the old "if (now - last) < 500ms return;" threw away later events,
    /// e.g. Deleted, leaving a stale list until the next event or F5).
    /// </summary>
    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        _watcherDebounceCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _watcherDebounceCts = cts;
        _ = DebouncedWatcherReloadAsync(cts);
    }

    private async System.Threading.Tasks.Task DebouncedWatcherReloadAsync(System.Threading.CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            await System.Threading.Tasks.Task.Delay(WatcherDebounceMs, token);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested) return;
            // Still looking at the same folder, and not searching (a search must not be reset).
            if (_folderWatcher == null) return;
            if (!string.Equals(CurrentPath, _folderWatcher.Path, System.StringComparison.OrdinalIgnoreCase)) return;
            if (!string.IsNullOrWhiteSpace(SearchText)) return;

            LoadItems();
        });
    }


    private void LoadItems_UIOnly()
    {
         // Placeholder
    }

    /// <summary>Incremented on every load so a slow background enumeration can detect it was superseded.</summary>
    private int _loadGeneration;

    /// <summary>Top-level listing of one folder (folders + files, .lnk targets resolved). No UI state touched.</summary>
    private static System.Collections.Generic.List<ExplorerItemViewModel> BuildTopLevelItems(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var dirs = dirInfo.GetDirectories();
        var files = dirInfo.GetFiles();

        var result = new System.Collections.Generic.List<ExplorerItemViewModel>(dirs.Length + files.Length);

        foreach (var d in dirs)
        {
            result.Add(new ExplorerItemViewModel
            {
                Name = d.Name,
                FullPath = d.FullName,
                IsDirectory = true,
                DateModified = d.LastWriteTime,
                ItemsCount = 0,
                LocationDisplay = d.Parent?.FullName ?? ""
            });
        }

        foreach (var f in files)
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
                catch { }
            }

            result.Add(new ExplorerItemViewModel
            {
                Name = f.Name,
                FullPath = f.FullName,
                IsDirectory = treatAsDir,
                TargetPath = target,
                DateModified = f.LastWriteTime,
                Size = f.Length,
                LocationDisplay = f.DirectoryName ?? ""
            });
        }

        return result;
    }

    /// <summary>Sorting/grouping pass used by every load (folder listing and search results).</summary>
    /// <summary>
    /// Copies the paths that are selected RIGHT NOW into <paramref name="into"/>.
    /// Called immediately before the list is swapped, so whichever reload finishes last restores
    /// the selection the user can actually see.
    /// </summary>
    private void CaptureLiveSelection(System.Collections.Generic.List<string> into)
    {
        into.Clear();
        foreach (var item in SelectedItems.OfType<ExplorerItemViewModel>())
        {
            if (!string.IsNullOrEmpty(item.FullPath))
                into.Add(item.FullPath);
        }
    }

    private void PopulateItems(System.Collections.Generic.IEnumerable<ExplorerItemViewModel> baseItems)
    {
                // Natural sort comparer for Windows Explorer-style ordering (1, 2, 3, 10 not 1, 10, 11, 2)
                var naturalComparer = new NaturalSortComparer();

                // Shortcut files (.lnk, .desktop) kept separate from physical folders in sort order
                // CHANGED: Include ALL shortcuts here (even if they point to folders) to satisfy "Folders -> Links -> Files"
                static bool IsShortcutFile(ExplorerItemViewModel x) => IsShortcutExtension(Path.GetExtension(x.FullPath));

                // Grouping & Sorting logic
                if (LayoutMode == ExplorerLayoutMode.Split)
                {
                    UpdateSplitView(baseItems);
                    // Add all to Items so functionality like SelectAll works
                    foreach(var i in baseItems) Items.Add(i);
                }
                else if (GroupBy == ExplorerGroupBy.None)
                {
                    if (SortBy == ExplorerSortOption.Name)
                    {
                         var allDirs = baseItems.Where(x => x.IsDirectory && !IsShortcutFile(x)).ToList();
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
                         var allDirs = baseItems.Where(x => x.IsDirectory && !IsShortcutFile(x)).ToList();
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
                         // Sort by Date — folders STILL come first, like Windows Explorer keeps the
                         // folders-then-files split for every sort column (was: date-mixed).
                         var dateDirs = baseItems.Where(x => x.IsDirectory && !IsShortcutFile(x)).ToList();
                         var dateShortcuts = baseItems.Where(IsShortcutFile).ToList();
                         var dateFiles = baseItems.Where(x => !x.IsDirectory && !IsShortcutFile(x)).ToList();

                         System.Comparison<ExplorerItemViewModel> byDate = IsSortDescending
                             ? (a, b) => b.DateModified.CompareTo(a.DateModified)
                             : (a, b) => a.DateModified.CompareTo(b.DateModified);

                         dateDirs.Sort(byDate);
                         dateShortcuts.Sort(byDate);
                         dateFiles.Sort(byDate);

                         foreach (var i in dateDirs) Items.Add(i);
                         foreach (var i in dateShortcuts) Items.Add(i);
                         foreach (var i in dateFiles) Items.Add(i);
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

    /// <summary>
    /// Recursive search that reports results in BATCHES while it walks the tree, so a big folder
    /// shows hits immediately instead of freezing until the whole subtree is scanned.
    /// Matching is literal: '*'/'?' typed by the user are normal characters, not wildcards.
    /// Runs on a worker thread and stops as soon as the token is cancelled (new keystroke).
    /// </summary>
    private static void SearchStream(
        string root,
        string searchText,
        int cap,
        System.Threading.CancellationToken token,
        System.Action<System.Collections.Generic.List<ExplorerItemViewModel>> onBatch,
        SearchSink sink)
    {
        var hasWildcard = searchText.IndexOf('*') >= 0 || searchText.IndexOf('?') >= 0;
        var fsPattern = hasWildcard ? "*" : "*" + searchText + "*";

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false
        };

        var batch = new System.Collections.Generic.List<ExplorerItemViewModel>(64);
        var lastFlush = System.Diagnostics.Stopwatch.StartNew();

        bool Matches(string name) => hasWildcard && name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) < 0;

        void Flush()
        {
            if (batch.Count == 0) return;
            onBatch(new System.Collections.Generic.List<ExplorerItemViewModel>(batch));
            batch.Clear();
            lastFlush.Restart();
        }

        try
        {
            foreach (var d in Directory.EnumerateDirectories(root, fsPattern, options))
            {
                if (token.IsCancellationRequested) { Flush(); return; }
                var info = new DirectoryInfo(d);
                if (Matches(info.Name)) continue;

                var item = new ExplorerItemViewModel
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    DateModified = info.LastWriteTime,
                    ItemsCount = 0,
                    LocationDisplay = info.Parent?.FullName ?? "",
                    ShowLocation = true
                };
                sink.Items.Add(item);
                batch.Add(item);
                if (sink.Items.Count >= cap) { sink.Truncated = true; Flush(); return; }
                if (batch.Count >= 64 || lastFlush.ElapsedMilliseconds > 120) Flush();
            }

            foreach (var f in Directory.EnumerateFiles(root, fsPattern, options))
            {
                if (token.IsCancellationRequested) { Flush(); return; }
                var info = new FileInfo(f);
                if (Matches(info.Name)) continue;

                var item = new ExplorerItemViewModel
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    DateModified = info.LastWriteTime,
                    Size = info.Length,
                    LocationDisplay = info.DirectoryName ?? "",
                    ShowLocation = true
                };
                sink.Items.Add(item);
                batch.Add(item);
                if (sink.Items.Count >= cap) { sink.Truncated = true; Flush(); return; }
                if (batch.Count >= 64 || lastFlush.ElapsedMilliseconds > 120) Flush();
            }
        }
        catch (System.Exception)
        {
            // Inaccessible branch mid-walk: keep what we already reported.
        }

        Flush();
    }

    /// <summary>Accumulator handed to <see cref="SearchStream"/>.</summary>
    private sealed class SearchSink
    {
        public System.Collections.Generic.List<ExplorerItemViewModel> Items { get; } = new();
        public bool Truncated { get; set; }
    }

    private static System.Collections.Generic.List<ExplorerItemViewModel> BuildSearchResults(string path, string searchText)
    {
        var sink = new SearchSink();
        SearchStream(path, searchText, SearchResultCap, System.Threading.CancellationToken.None, _ => { }, sink);
        return sink.Items;
    }

    private void LoadItems()
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Items.OfType<ExplorerItemViewModel>().Any(x => x.IsEditing))
            {
                return;
            }

            var path = CurrentPath;
            var searchText = SearchText;

            // Remember the selection so it survives a refresh (F5 / auto-refresh), like Explorer.
            // The list is NOT cleared here anymore: it stays on screen while the background
            // enumeration runs, so big folders no longer blink to an empty list.
            var reselectPaths = SelectedItems.OfType<ExplorerItemViewModel>()
                .Select(x => x.FullPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                SelectedItems.Clear();
                Items.Clear();
                IsEmpty = true;
                return;
            }

            SetupFolderWatcher(path);

            var generation = ++_loadGeneration;

            System.Collections.Generic.List<ExplorerItemViewModel> baseItems;
            var isSearchRun = !string.IsNullOrWhiteSpace(searchText);

            try
            {
                if (isSearchRun)
                {
                    // STREAMING search: batches of hits are shown while the walk continues, so a big
                    // tree never shows an empty list for seconds. Cancelled by the next keystroke.
                    var sink = new SearchSink();
                    var runCts = new System.Threading.CancellationTokenSource();
                    _searchRunCts = runCts;
                    var token = runCts.Token;
                    var shownFirstBatch = false;

                    IsSearching = true;
                    SearchTruncated = false;

                    await System.Threading.Tasks.Task.Run(() => SearchStream(path, searchText, SearchResultCap, token, batch =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (generation != _loadGeneration || token.IsCancellationRequested) return;
                            if (!shownFirstBatch)
                            {
                                shownFirstBatch = true;
                                CaptureLiveSelection(reselectPaths);

                                SelectedItems.Clear();
                                Items.Clear();
                            }
                            foreach (var hit in batch) Items.Add(hit);
                            OnPropertyChanged(nameof(SearchResultSummary));
                        });
                    }, sink));

                    // let the posted batches run before the final sorted pass
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

                    if (generation != _loadGeneration) return;

                    IsSearching = false;
                    SearchTruncated = sink.Truncated;
                    baseItems = sink.Items;
                }
                else
                {
                    // Enumeration (and .lnk target resolution) ran on the UI thread before: a slow
                    // network share or a folder full of shortcuts froze the window.
                    IsSearching = false;
                    SearchTruncated = false;
                    baseItems = await System.Threading.Tasks.Task.Run(() => BuildTopLevelItems(path));
                }
            }
            catch
            {
                IsSearching = false;
                return;
            }

            // A newer load started while this one was enumerating: drop the stale result set.
            if (generation != _loadGeneration) return;

            // The snapshot is taken from the LIVE selection right before the swap (see below).

            // Snapshot the LIVE selection right here, not at load start: with two overlapping
            // reloads the one that finishes last used to snapshot early (empty) and wiped the
            // selection the user had in the meantime — the next Delete/Copy then did nothing.
            CaptureLiveSelection(reselectPaths);

            // Swap the listing in one UI-thread pass (clear + refill = a single render frame).
            SelectedItems.Clear();
            Items.Clear();

            try
            {
                PopulateItems(baseItems);

            }
            catch { }

            IsEmpty = Items.Count == 0;
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(SearchResultSummary));
            OnPropertyChanged(nameof(EmptyStateText));

            // Keep the cut items dimmed across refresh: the item objects are new, the cut
            // clipboard state is not (Explorer keeps cut items faded until you paste).
            MarkCutItems(CutPaths);

            // Restore the previous selection for items that still exist.
            if (string.IsNullOrEmpty(_pendingSelectName) && reselectPaths.Count > 0)
            {
                foreach (var p in reselectPaths)
                {
                    var match = Items.OfType<ExplorerItemViewModel>()
                        .FirstOrDefault(i => string.Equals(i.FullPath, p, System.StringComparison.OrdinalIgnoreCase));
                    if (match != null && !SelectedItems.Contains(match))
                        SelectedItems.Add(match);
                }
            }

            if (!string.IsNullOrEmpty(_pendingSelectName))
            {
                // A watcher-driven reload can arrive before a pasted/created file is on disk
                // (e.g. the background copy is still running). Previously that reload consumed
                // the pending name and the item was never selected — retry a few reloads instead.
                if (!string.Equals(_pendingSelectWatchName, _pendingSelectName, System.StringComparison.Ordinal))
                {
                    _pendingSelectWatchName = _pendingSelectName;
                    _pendingSelectAttempts = 0;
                }

                var itemToSelect = Items.OfType<ExplorerItemViewModel>().FirstOrDefault(x => x.Name == _pendingSelectName);
                if (itemToSelect != null)
                {
                    SelectedItems.Add(itemToSelect);
                    WeakReferenceMessenger.Default.Send(new Messages.ScrollToExplorerItemMessage(itemToSelect));
                    if (_pendingEdit)
                    {
                        itemToSelect.IsEditing = true;
                    }
                    _pendingSelectName = null;
                    _pendingEdit = false;
                    _pendingSelectWatchName = null;
                    _pendingSelectAttempts = 0;
                }
                else if (++_pendingSelectAttempts >= 3)
                {
                    // Give up so a name that never materialises cannot linger and select later.
                    _pendingSelectName = null;
                    _pendingEdit = false;
                    _pendingSelectWatchName = null;
                    _pendingSelectAttempts = 0;
                }
            }
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

    /// <summary>
    /// True when <paramref name="candidate"/> is strictly inside <paramref name="parent"/>.
    /// Separator-aware: "C:\Out2" is NOT inside "C:\Out" (a plain StartsWith() said it was,
    /// which mis-enabled Up navigation and stored sibling Quick-Access entries as relative).
    /// </summary>
    private static bool IsPathInside(string parent, string candidate)
    {
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(candidate)) return false;

        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return c.Length > p.Length
            && c.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)
            && c[p.Length] == Path.DirectorySeparatorChar;
    }

    private bool IsSubfolderOf(string path, string root) => IsPathInside(root, path);

    // --- CONTEXT MENU EXTENSIONS & SCRIPT LOGIC ---


    [RelayCommand]
    public async Task CopyPath(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any())
        {
            // No selection: copy current folder path
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                await SetClipboardTextAsync(CurrentPath);
                _notificationService.ShowSuccess("Path copied!");
            }
            return;
        }

        var text = string.Join(Environment.NewLine, items.Select(x => x.FullPath));
        await SetClipboardTextAsync(text);
        _notificationService.ShowSuccess($"{items.Count()} path(s) copied!");
    }

    [RelayCommand]
    public async Task CopyName(object? parameter)
    {
        var items = GetSelectedItems(parameter);
        if (!items.Any()) return;

        var text = string.Join(Environment.NewLine, items.Select(x => x.Name));
        if (await _clipboard.SetTextAsync(text))
            _notificationService.ShowSuccess("Name copied to clipboard.");
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
        OnPropertyChanged(nameof(ShowFloatingSearch));
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
    private string _metadataPath => Path.Combine(BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory(), "scripts.json");

    private void LoadMetadata()
    {
        try
        {
            var folder = Path.GetDirectoryName(_metadataPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (!File.Exists(_metadataPath))
            {
                var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
                if (File.Exists(defaultPath))
                {
                    File.Copy(defaultPath, _metadataPath);
                }
            }

            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);

                try
                {    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
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
                // Resolve icons on UI thread (TryGetResource requires it)
                foreach (var opt in sortedList)
                {
                    if (!string.IsNullOrEmpty(opt.OriginalName) && _scriptAliases.TryGetValue(opt.OriginalName, out var config) && !string.IsNullOrEmpty(config.IconKey))
                    {
                        if (Avalonia.Application.Current?.TryGetResource(config.IconKey, null, out var res) == true && res is Avalonia.Media.StreamGeometry g)
                            opt.IconGeometry = g;
                    }
                }
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

            _ = Task.Run(async () =>
            {
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
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Script error: {ex.Message}");
        }
    }

    [RelayCommand]
    public Task CreateNewFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        var path = Path.Combine(CurrentPath, name);
        try
        {
            Directory.CreateDirectory(path);
            PushUndo(new ExplorerUndoEntry { Kind = "new", Path = path, Description = $"create folder '{name}'" });
            Refresh();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to create folder: {ex.Message}");
        }
        return Task.CompletedTask;
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
            PushUndo(new ExplorerUndoEntry { Kind = "new", Path = path, Description = $"create file '{name}'" });
            Refresh();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to create file: {ex.Message}");
        }
    }

    [RelayCommand]
    public Task DuplicateItem(int count)
    {
        var items = GetSelectedItems(null);
        if (!items.Any()) return Task.CompletedTask;

        var reserved = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdCopies = new System.Collections.Generic.List<(string Old, string New)>();
        foreach (var item in items)
        {
            try
            {
                string dir = Path.GetDirectoryName(item.FullPath) ?? "";

                for(int i=1; i<=count; i++)
                {
                     // Same naming rule as paste/drop: "x - Copy", "x - Copy (2)", ... (was "x - Copy (1)")
                     string newPath = Services.FileNameDeduplicator.BuildCopyPath(dir, item.FullPath, reserved);
                     reserved.Add(Path.GetFileName(newPath));
                     // Background queue handles both files and folders (recursive copy).
                     _fileManager.CopyFileBackground(item.FullPath, newPath);
                     createdCopies.Add((item.FullPath, newPath));
                }
            }
            catch {}
        }

        if (createdCopies.Count > 0)
            PushUndo(new ExplorerUndoEntry
            {
                Kind = "copy",
                Description = $"duplicate {createdCopies.Count} item(s)",
                Pairs = createdCopies
            });
        Refresh();
        return Task.CompletedTask;
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
                 var renamePairs = new System.Collections.Generic.List<(string Old, string New)>();

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
                             renamePairs.Add((item.FullPath, newPath));
                         }
                     }
                     catch (Exception ex)
                     {
                         _notificationService.ShowError($"Failed to rename {item.Name}: {ex.Message}");
                     }
                 }
                 if (renamePairs.Count > 0)
                     PushUndo(new ExplorerUndoEntry
                     {
                         Kind = "batchrename",
                         Description = $"batch rename {renamePairs.Count} item(s)",
                         Pairs = renamePairs
                     });

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

        _pendingSelectName = name;
        _pendingEdit = true;
        await CreateNewFolder(name);
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

    /// <summary>Ctrl+F membuka input pencarian di header Explorer.</summary>
    [RelayCommand]
    public void FocusSearchBox()
    {
        if (!IsSearchVisible)
            IsSearchVisible = true;

        WeakReferenceMessenger.Default.Send(new FocusSearchBoxMessage());
    }


    /// <summary>Navigate to the path currently in the clipboard.</summary>
    [RelayCommand]
    public async Task PastePath()
    {
        {
            var text = await _clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            var path = text.Trim().Trim('"');
            if (Directory.Exists(path))
            {
                NavigateTo(path);
            }
            else if (File.Exists(path))
            {
                // Navigate to the parent folder and try to select the file
                var dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir))
                {
                    NavigateTo(dir);
                }
            }
            else
            {
                _notificationService.ShowError($"Path not found: {path}");
            }
        }
    }

    private async Task SetClipboardTextAsync(string text)
    {
        await _clipboard.SetTextAsync(text);
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

    [RelayCommand]
    private void SwitchTab()
    {
        WeakReferenceMessenger.Default.Send(new SwitchExplorerTabMessage());
    }

    [RelayCommand]
    public void SetViewLayout(int mode)
    {
         switch (mode)
         {
             case 1: // Extra Large
                 LayoutMode = ExplorerLayoutMode.Thumbnail;
                 ThumbnailSize = 256;
                 break;
             case 2: // Large
                 LayoutMode = ExplorerLayoutMode.Thumbnail;
                 ThumbnailSize = 128;
                 break;
             case 3: // Medium
                 LayoutMode = ExplorerLayoutMode.Thumbnail;
                 ThumbnailSize = 96;
                 break;
             case 4: // Split (Ctrl+Shift+4)
                 LayoutMode = ExplorerLayoutMode.Split;
                 UpdateSplitView(Items.OfType<ExplorerItemViewModel>());
                 break;
             case 5: // List (Horizontal but Columnar)
                 LayoutMode = ExplorerLayoutMode.Horizontal;
                 break;
             case 6: // Details (Vertical)
                 LayoutMode = ExplorerLayoutMode.Vertical;
                 break;
             case 7: // Tiles (Tiles with medium icon)
                 LayoutMode = ExplorerLayoutMode.Tiles;
                 ThumbnailSize = 48;
                 break;
             case 8: // Content
                 LayoutMode = ExplorerLayoutMode.Vertical;
                 break;
         }
    }

    /// <summary>
    /// Ctrl+Mouse Wheel — Windows Explorer behaviour: if you are in a list/details view it first
    /// switches to an icon view, then each notch scales the icons up/down.
    /// </summary>
    [RelayCommand]
    public void ZoomIcons(bool? wheelUp)
    {
        if (LayoutMode != ExplorerLayoutMode.Thumbnail && LayoutMode != ExplorerLayoutMode.Tiles)
        {
            LayoutMode = ExplorerLayoutMode.Thumbnail;
            ThumbnailSize = 96;
            return;
        }

        var step = wheelUp == true ? 16.0 : -16.0;
        ThumbnailSize = System.Math.Clamp(ThumbnailSize + step, 48, 512);
    }

    /// <summary>Select all items in the current folder (Ctrl+A).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        // If any item is in rename mode, do NOT select all items.
        // Let the TextBox handle Ctrl+A for text selection.
        if (Items.OfType<ExplorerItemViewModel>().Any(x => x.IsEditing)) return;

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
        _pendingSelectName = NewItemName;
        _pendingEdit = false;
        await CreateNewFolder(NewItemName);
        ClosePopups();
    }

    [RelayCommand]
    public async Task ConfirmNewFile()
    {
        _pendingSelectName = NewItemName;
        if (!_pendingSelectName.Contains(".")) _pendingSelectName += ".txt";
        _pendingEdit = false;
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

    /// <summary>Parent folder of this item. Shown as the "Folder" column while a search is active.</summary>
    public string LocationDisplay { get; set; } = "";

    /// <summary>True for items produced by a recursive search (location column visible).</summary>
    public bool ShowLocation { get; set; }

    /// <summary>Explorer dims an item that was cut, so the user can tell Cut from Copy before pasting.</summary>
    [ObservableProperty] private bool _isCut;

    /// <summary>Tooltip that appends the folder while searching (so identical names stay distinguishable).</summary>
    public string DisplayTooltip => ShowLocation && !string.IsNullOrEmpty(LocationDisplay)
        ? Name + System.Environment.NewLine + LocationDisplay
        : Name;

    public double RowOpacity => IsCut ? 0.45 : 1.0;

    partial void OnIsCutChanged(bool value) => OnPropertyChanged(nameof(RowOpacity));

    public string IconKey => IsDirectory ? "IconFolder" : "IconFile";
    public string DisplaySize => IsDirectory ? $"{ItemsCount} items" : BytesToString(Size);

    public bool IsSelectable => true;

    // --- System icon support (matches OS / 7tsp theme) ---
    private static volatile bool s_useSystemIcons;
    private static volatile string s_customIconPath = "";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Avalonia.Media.Imaging.Bitmap> s_iconCache = new();

    public static bool SystemIconsEnabled => s_useSystemIcons;
    public static string CustomIconPathSetting => s_customIconPath;

    public static void PushSystemIconsSetting(bool enabled) => s_useSystemIcons = enabled;

    public static void PushCustomIconPathSetting(string path)
    {
        s_customIconPath = path ?? "";
        s_iconCache.Clear();
    }

    public bool UseSystemIcons => s_useSystemIcons;

    private Avalonia.Media.Imaging.Bitmap? _systemIcon;
    private bool _systemIconLoaded;

    /// <summary>Lazy-loaded Windows shell / 7tsp icon for this item. Null until requested / if extraction fails.</summary>
    public Avalonia.Media.Imaging.Bitmap? SystemIcon
    {
        get
        {
            if (!_systemIconLoaded && UseSystemIcons)
            {
                _systemIconLoaded = true;
                LoadSystemIconAsync();
            }
            return _systemIcon;
        }
    }

    public bool ShowSystemIcon => UseSystemIcons && _systemIcon != null;
    public bool ShowFolderFallback => !ShowSystemIcon && IsDirectory;
    public bool ShowFileFallback => !ShowSystemIcon && !IsDirectory;
    /// <summary>File fallback for icon views that preview real images separately (Thumbnail/Tiles).</summary>
    public bool ShowIconViewFileFallback => ShowFileFallback && !IsImageFile;

    /// <summary>Reapplies the global system-icon setting on already created items.</summary>
    public void ApplySystemIconMode(bool enabled)
    {
        s_useSystemIcons = enabled;
        NotifyIconStateChanged();
    }

    /// <summary>Forces reload of the icon when the custom 7tsp icon path is changed.</summary>
    public void ReloadSystemIcon()
    {
        _systemIconLoaded = false;
        _systemIcon = null;
        NotifyIconStateChanged();
    }

    private void NotifyIconStateChanged()
    {
        OnPropertyChanged(nameof(SystemIcon));
        OnPropertyChanged(nameof(ShowSystemIcon));
        OnPropertyChanged(nameof(ShowFolderFallback));
        OnPropertyChanged(nameof(ShowFileFallback));
        OnPropertyChanged(nameof(ShowIconViewFileFallback));
    }

    private async void LoadSystemIconAsync()
    {
        try
        {
            var key = IsDirectory
                ? "folder"
                : Path.GetExtension(FullPath).ToLowerInvariant();

            // Shortcuts (.lnk) can carry their own resolved icon: cache per path.
            if (Path.GetExtension(FullPath).Equals(".lnk", System.StringComparison.OrdinalIgnoreCase))
                key = "lnk:" + FullPath.ToLowerInvariant();

            var customPath = s_customIconPath;
            var cacheKey = string.IsNullOrEmpty(customPath) ? key : $"{customPath}:{key}";

            if (s_iconCache.TryGetValue(cacheKey, out var cached))
            {
                _systemIcon = cached;
                NotifyIconStateChanged();
                return;
            }

            const int iconSize = 32;
            var isDir = IsDirectory;
            var fullPath = FullPath;
            var bmp = await System.Threading.Tasks.Task.Run(() => Services.SystemIconService.ResolveIcon(fullPath, isDir, customPath, iconSize));
            if (bmp == null) return;

            s_iconCache[cacheKey] = bmp;
            _systemIcon = bmp;
            NotifyIconStateChanged();
        }
        catch
        {
            // Extraction failed: keep fallback glyph icons.
        }
    }

    public override string ToString() => Name ?? "";

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
    [ObservableProperty] private bool _isDynamic;

    private Avalonia.Media.Imaging.Bitmap? _systemIcon;
    private bool _systemIconLoaded;

    public bool ShowSystemIcon => ExplorerItemViewModel.SystemIconsEnabled && _systemIcon != null;
    public bool ShowFolderFallback => !ShowSystemIcon;
    public Avalonia.Media.Imaging.Bitmap? SystemIcon
    {
        get
        {
            if (!_systemIconLoaded && ExplorerItemViewModel.SystemIconsEnabled)
            {
                _systemIconLoaded = true;
                LoadSystemIconAsync();
            }
            return _systemIcon;
        }
    }

    public void RefreshIcon()
    {
        _systemIconLoaded = false;
        _systemIcon = null;
        OnPropertyChanged(nameof(SystemIcon));
        OnPropertyChanged(nameof(ShowSystemIcon));
        OnPropertyChanged(nameof(ShowFolderFallback));
    }

    private async void LoadSystemIconAsync()
    {
        try
        {
            var targetPath = IsDynamic ? Path : Path;
            var bitmap = await Task.Run(() => Services.SystemIconService.ResolveIcon(
                targetPath, true, ExplorerItemViewModel.CustomIconPathSetting, 32));
            if (bitmap == null) return;
            _systemIcon = bitmap;
            OnPropertyChanged(nameof(SystemIcon));
            OnPropertyChanged(nameof(ShowSystemIcon));
            OnPropertyChanged(nameof(ShowFolderFallback));
        }
        catch { }
    }
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
    /// <summary>Shared singleton instance.</summary>
    public static readonly NaturalSortComparer Instance = new();

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

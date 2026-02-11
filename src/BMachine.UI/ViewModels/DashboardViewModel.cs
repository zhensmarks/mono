using System.Collections.ObjectModel;
using System.Linq;
using BMachine.SDK;
// using BMachine.SDK.Interfaces; // Removed
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia;

using Avalonia.Media;
using Avalonia.Controls;
using BMachine.UI.Models;
using BMachine.UI.Messages;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace BMachine.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject, IRecipient<OpenTextFileMessage>, IRecipient<AppFocusChangedMessage>, IRecipient<NavigateBackMessage>
{
    private readonly IActivityService _activityService;
    private readonly IDatabase _database;
    private readonly ILanguageService? _languageService;
    private readonly Services.IProcessLogService? _logService;

    public IDatabase Database => _database;
    public ILanguageService? Language => _languageService;





    [ObservableProperty] private bool _isLogPanelOpen;
    [ObservableProperty] private bool _isOnline = true; // Connection status

    partial void OnIsLogPanelOpenChanged(bool value)
    {
         _database?.SetAsync("Dashboard.IsLogPanelOpen", value.ToString());
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    // --- Dashboard Visibility Properties ---
    [ObservableProperty] private bool _isGdriveVisible = true;
    [ObservableProperty] private bool _isPixelcutVisible = true;
    [ObservableProperty] private bool _isBatchVisible = true;
    [ObservableProperty] private bool _isLockerVisible = true; // Use Locker to match Tab name 'LockerTab'
    [ObservableProperty] private bool _isPointVisible = true;


    // Activity Panel
    [ObservableProperty] private bool _isActivityPanelOpen;

    [RelayCommand]
    private void ToggleActivityPanel()
    {
        IsActivityPanelOpen = !IsActivityPanelOpen;
    }

    [RelayCommand]
    private void CloseActivityPanel()
    {
        IsActivityPanelOpen = false;
    }

    // UI Customization
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(NavButtonEffectiveWidth))]
    private double _navButtonWidth = 40; // Reduced to 40 (icon width only)

    [ObservableProperty] private double _navButtonHeight = 40;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(NavCornerRadiusStruct))]
    private double _navCornerRadius = 20;

    public CornerRadius NavCornerRadiusStruct => new CornerRadius(NavCornerRadius);
    
    // Auto width for Text mode, Fixed for Icon mode
    public double NavButtonEffectiveWidth => IsNavIconMode ? NavButtonWidth : double.NaN;

    [ObservableProperty] private double _navFontSize = 14;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsNavIconMode))]
    [NotifyPropertyChangedFor(nameof(NavButtonEffectiveWidth))]
    private int _navStyleIndex = 0; // 0=Icon, 1=Text
    
    public bool IsNavIconMode => NavStyleIndex == 0;

    [ObservableProperty] private string _navCustomText = "Dashboard";
    [ObservableProperty] private string _navGdriveText = "Driver";
    [ObservableProperty] private string _navPixelcutText = "Pixelcut";
    [ObservableProperty] private string _navBatchText = "Batch";
    [ObservableProperty] private string _navLockerText = "Locker";

    partial void OnNavButtonWidthChanged(double value) => _database?.SetAsync("Dashboard.Nav.Width", value.ToString());
    partial void OnNavButtonHeightChanged(double value) => _database?.SetAsync("Dashboard.Nav.Height", value.ToString());
    partial void OnNavCornerRadiusChanged(double value) => _database?.SetAsync("Dashboard.Nav.Radius", value.ToString());
    
    partial void OnNavCustomTextChanged(string value) => _database?.SetAsync("Dashboard.Nav.Text.Dash", value);
    partial void OnNavGdriveTextChanged(string value) => _database?.SetAsync("Dashboard.Nav.Text.Gdrive", value);
    partial void OnNavPixelcutTextChanged(string value) => _database?.SetAsync("Dashboard.Nav.Text.Pixelcut", value);
    partial void OnNavBatchTextChanged(string value) => _database?.SetAsync("Dashboard.Nav.Text.Batch", value);
    partial void OnNavLockerTextChanged(string value) => _database?.SetAsync("Dashboard.Nav.Text.Locker", value);
    partial void OnNavFontSizeChanged(double value) => _database?.SetAsync("Dashboard.Nav.FontSize", value.ToString());

    public async Task LoadNavSettings()
    {
        if (_database == null) return;
        var w = await _database.GetAsync<string>("Dashboard.Nav.Width");
        
        if (double.TryParse(w, out double dW)) 
        {
            // Migration: Force old defaults to new compact size (40)
            NavButtonWidth = dW > 45 ? 40 : dW;
        }

        var h = await _database.GetAsync<string>("Dashboard.Nav.Height");
        if (double.TryParse(h, out double dH)) NavButtonHeight = dH;

        var r = await _database.GetAsync<string>("Dashboard.Nav.Radius");
        if (double.TryParse(r, out double dR)) NavCornerRadius = dR;
        
        var f = await _database.GetAsync<string>("Dashboard.Nav.FontSize");
        if (double.TryParse(f, out double dF)) NavFontSize = dF;
        
        var s = await _database.GetAsync<string>("Dashboard.Nav.Style");
        if (int.TryParse(s, out int dS)) NavStyleIndex = dS;
        
        var navDash = await _database.GetAsync<string>("Dashboard.Nav.Text.Dash");
        if (!string.IsNullOrEmpty(navDash)) NavCustomText = navDash;
        
        var navGdrive = await _database.GetAsync<string>("Dashboard.Nav.Text.Gdrive");
        if (!string.IsNullOrEmpty(navGdrive)) NavGdriveText = navGdrive;
        
        var navPixel = await _database.GetAsync<string>("Dashboard.Nav.Text.Pixelcut");
        if (!string.IsNullOrEmpty(navPixel)) NavPixelcutText = navPixel;
        
        var navBatch = await _database.GetAsync<string>("Dashboard.Nav.Text.Batch");
        if (!string.IsNullOrEmpty(navBatch)) NavBatchText = navBatch;
        
        var navLocker = await _database.GetAsync<string>("Dashboard.Nav.Text.Locker");
        if (!string.IsNullOrEmpty(navLocker)) NavLockerText = navLocker;
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logService?.Clear();
        LogItems.Clear(); // Ensure UI is cleared too
        ProcessStatusText = "Console cleared";
        StatusColor = Brushes.Gray;
    }
    
    [RelayCommand]
    private async Task CopyAllLogs()
    {
        if (string.IsNullOrEmpty(LogText)) return;
        
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow : null);
            
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(LogText);
        }
    }

    public class ActivityItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string TimeDisplay { get; set; } = "";
        public bool IsLast { get; set; } = false;
    }

    [ObservableProperty]
    private ObservableCollection<ActivityItem> _activities = new();

    [ObservableProperty]
    private string _userName = "USER";

    [ObservableProperty]
    private string _greeting = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _userAvatar;



    // Primary Constructor
    public event Action? OpenSettingsRequested;
    public event Action? OpenEditingListRequested;
    public event Action? OpenRevisionListRequested;
    public event Action? OpenLateListRequested;
    
    [ObservableProperty]
    private FolderLockerViewModel _folderLockerVM; // Add this logic
    
    // Pixelcut ViewModel
    [ObservableProperty]
    private PixelcutViewModel _pixelcutVM;

    // Navigation Tab Selection
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsBatchTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsLockerTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsPixelcutTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsGdriveTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsPointsTabSelected))] // Added for Points Tab
    private int _selectedTabIndex = 0; // 0=Home, 1=Grid, 2=Locker, 3=Pixelcut, 4=GDrive, 5=Points

    public bool IsDashboardTabSelected 
    { 
        get => SelectedTabIndex == 0; 
        set { if (value) SelectedTabIndex = 0; } 
    }

    public bool IsBatchTabSelected 
    { 
        get => SelectedTabIndex == 1; 
        set { if (value) SelectedTabIndex = 1; } 
    }

    public bool IsLockerTabSelected 
    { 
        get => SelectedTabIndex == 2; 
        set { if (value) SelectedTabIndex = 2; } 
    }

    public bool IsPixelcutTabSelected 
    { 
        get => SelectedTabIndex == 3; 
        set { if (value) SelectedTabIndex = 3; } 
    }

    public bool IsGdriveTabSelected 
    { 
        get => SelectedTabIndex == 4; 
        set { if (value) SelectedTabIndex = 4; } 
    }

    public bool IsPointsTabSelected
    {
        get => SelectedTabIndex == 5;
        set { if (value) SelectedTabIndex = 5; }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // When Tab changes, close the Embedded View Overlay if open
        if (IsEmbeddedViewOpen)
        {
            NavigateBack(); // Tries to pop stack. If specific logic needed:
            // Force clear:
            IsEmbeddedViewOpen = false;
            CurrentEmbeddedView = null;
            _viewStack.Clear();
        }
    }
    
    // Gdrive ViewModel
    [ObservableProperty]
    private GdriveViewModel _gdriveVM;

    // Batch Master ViewModel
    [ObservableProperty]
    private BatchViewModel _batchVM;



    // Leaderboard ViewModel
    [ObservableProperty]
    private PointLeaderboardViewModel _pointLeaderboardVM;

    [ObservableProperty]
    private SpreadsheetViewModel _spreadsheetVM;


    // --- Embedded View Navigation System ---
    [ObservableProperty] private object? _currentEmbeddedView;
    [ObservableProperty] private bool _isEmbeddedViewOpen;
    
    private readonly System.Collections.Generic.Stack<object> _viewStack = new();

    public void NavigateToView(object view)
    {
        if (CurrentEmbeddedView != null)
        {
            _viewStack.Push(CurrentEmbeddedView);
        }
        CurrentEmbeddedView = view;
        IsEmbeddedViewOpen = true;
    }

    public void NavigateBack()
    {
        if (_viewStack.Count > 0)
        {
            CurrentEmbeddedView = _viewStack.Pop();
        }
        else
        {
            IsEmbeddedViewOpen = false;
            CurrentEmbeddedView = null;
        }
    }


    // Persistent List ViewModels (Single Source for Stats & Lists)
    private EditingCardListViewModel _editingListVM;
    private RevisionCardListViewModel _revisionListVM;
    private LateCardListViewModel _lateListVM;

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }
    
    [RelayCommand]
    private void OpenEditingList()
    {
        _editingListVM.Title = "Editing List"; // Ensure title is set
        _editingListVM.StartAutoRefresh();
        var view = new BMachine.UI.Views.EditingCardListView { DataContext = _editingListVM };
        NavigateToView(view);
    }

    [RelayCommand]
    private void OpenRevisionList()
    {
        _revisionListVM.Title = "Revision List";
        _revisionListVM.StartAutoRefresh();
        var view = new BMachine.UI.Views.RevisionCardListView { DataContext = _revisionListVM };
        NavigateToView(view);
    }

    [RelayCommand]
    private void OpenLateList()
    {
        _lateListVM.Title = "Late List";
        _lateListVM.StartAutoRefresh();
        var view = new BMachine.UI.Views.LateCardListView { DataContext = _lateListVM };
        NavigateToView(view);
    }

    // Keep Window commands for fallback or if user specifically wants window? 
    // For now user requested replacement. We can keep them or separate.
    // The previous implementation had separte "Open...Window" commands bound to specific buttons?
    // Let's check logic:
    // User click Widget -> Command="{Binding OpenEditingListCommand}"
    // So modifying OpenEditingListCommand is correct.

    [RelayCommand]
    private void OpenEditingWindow() => OpenListWindow(_editingListVM, "Editing List");

    [RelayCommand]
    private void OpenRevisionWindow() => OpenListWindow(_revisionListVM, "Revision List");

    [RelayCommand]
    private void OpenLateWindow() => OpenListWindow(_lateListVM, "Late List");



    [RelayCommand]
    private void OpenLeaderboardWindow()
    {
         // Refresh Data
         _pointLeaderboardVM.LoadDataCommand.Execute(null);
         
         // Embedded version
         var view = new BMachine.UI.Views.LeaderboardView { DataContext = _pointLeaderboardVM };
         NavigateToView(view);
    }

    [RelayCommand]
    private async Task OpenSpreadsheetWindow()
    {
        // Refresh Data - DISABLED by user request (load manually)
        // _spreadsheetVM.LoadDataCommand.Execute(null);

        var window = new BMachine.UI.Views.SpreadsheetWindow
        {
            DataContext = _spreadsheetVM
        };

        // Restore Position & Size
        var strX = await _database.GetAsync<string>("SpreadsheetWindow.X");
        var strY = await _database.GetAsync<string>("SpreadsheetWindow.Y");
        var strW = await _database.GetAsync<string>("SpreadsheetWindow.Width");
        var strH = await _database.GetAsync<string>("SpreadsheetWindow.Height");

        if (int.TryParse(strX, out int x) && int.TryParse(strY, out int y))
        {
            window.Position = new Avalonia.PixelPoint(x, y);
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
        }

        if (double.TryParse(strW, out double w) && double.TryParse(strH, out double h))
        {
            window.Width = w;
            window.Height = h;
        }

        // Save Position & Size on Close
        window.Closing += (s, e) =>
        {
            if (s is Avalonia.Controls.Window w)
            {
                _database.SetAsync("SpreadsheetWindow.X", w.Position.X.ToString());
                _database.SetAsync("SpreadsheetWindow.Y", w.Position.Y.ToString());
                _database.SetAsync("SpreadsheetWindow.Width", w.Width.ToString());
                _database.SetAsync("SpreadsheetWindow.Height", w.Height.ToString());
            }
        };

        window.Show();
    }

    private void OpenListWindow(BaseTrelloListViewModel vm, string title)
    {
        vm.Title = title;
        // Start AutoRefresh if available (Editing/Revision/Late usually have it public)
        if (vm is EditingCardListViewModel evm) evm.StartAutoRefresh();
        else if (vm is RevisionCardListViewModel rvm) rvm.StartAutoRefresh();
        else if (vm is LateCardListViewModel lvm) lvm.StartAutoRefresh();
        
        var win = new BMachine.UI.Views.CardListWindow();
        win.DataContext = vm;
        win.Show();
    }

    [ObservableProperty]
    private bool _isFloatingWidgetVisible;

    partial void OnIsFloatingWidgetVisibleChanged(bool value)
    {
        // 1. Save to DB (Fire and Forget or Task.Run)
        // We can't await here directly, so we run off-thread safely?
        // Actually, _database operations are async.
        Task.Run(async () => 
        {
             try { await _database.SetAsync("Dashboard.IsFloatingWidgetVisible", value.ToString()); }
             catch (Exception ex) { Console.WriteLine($"DB Save Error: {ex.Message}"); }
        });

        // 2. Broadcast
        WeakReferenceMessenger.Default.Send(new FloatingWidgetMessage(value));
    }

    [RelayCommand]
    private void ToggleFloatingWidget()
    {
        IsFloatingWidgetVisible = !IsFloatingWidgetVisible;
    }

    [RelayCommand]
    private void OpenLogoutDialog()
    {
        WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.OpenExitConfirmMessage());
    }

    public DashboardViewModel(
        IDatabase database,
        IActivityService activityService, // ADDED: Missing parameter!
        ILanguageService? languageService = null, 
        Services.IProcessLogService? logService = null)
    {
        StatPoints = "0";
        
        _database = database;
        _activityService = activityService; // ADDED: Missing assignment!
        _languageService = languageService; // Nullable for design time or fallback
        _logService = logService;
        
        LoadLogPanelState();
        _ = LoadNavSettings();
        
        if (_languageService != null)
        {
            _languageService.PropertyChanged += (s, e) => UpdateGreeting();
        }
        
        if (_logService != null)
        {
            _logService.Logs.CollectionChanged += (s, e) => UpdateLogText();
            UpdateLogText();
        }
        
        RegisterMessages(); // Hook up message handlers
        
        // Initialize Child ViewModels
        _folderLockerVM = new FolderLockerViewModel();
        _batchVM = new BatchViewModel(database, logService);
        _batchVM = new BatchViewModel(database, logService);
        _pixelcutVM = new PixelcutViewModel(database);
        _gdriveVM = new GdriveViewModel(database);
        _pointLeaderboardVM = new PointLeaderboardViewModel(database);
        _spreadsheetVM = new SpreadsheetViewModel(database);
        
        // Leaderboard will load in LoadData() async method
        
        // Initialize Persistent List VMs
        _editingListVM = new EditingCardListViewModel(database);
        _revisionListVM = new RevisionCardListViewModel(database);
        _lateListVM = new LateCardListViewModel(database);

        // SYNC: Listen to changes in lists to update Stats immediately (Thread-Safe)
        _editingListVM.Cards.CollectionChanged += (s, e) => 
        {
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 StatEditing = _editingListVM.Cards.Count.ToString();
                 StatEditingPercentage = Math.Min(_editingListVM.Cards.Count / 10.0, 1.0);
             });
        };
        _revisionListVM.Cards.CollectionChanged += (s, e) => 
        {
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 StatRevision = _revisionListVM.Cards.Count.ToString();
                 StatRevisionPercentage = Math.Min(_revisionListVM.Cards.Count / 10.0, 1.0);
             });
        };
        _lateListVM.Cards.CollectionChanged += (s, e) => 
        {
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 StatLate = _lateListVM.Cards.Count.ToString();
                 StatLatePercentage = Math.Min(_lateListVM.Cards.Count / 10.0, 1.0);
             });
        };

        // Call LoadData directly (async fire-and-forget)
        _ = LoadData(); // Direct call instead of Command.Execute

        // START AUTO-REFRESH IMMEDIATELY (Don't wait for LoadData async)
        _editingListVM.StartAutoRefresh();
        _revisionListVM.StartAutoRefresh();
        _lateListVM.StartAutoRefresh();

        // SAFETY: Fallback Timer to force sync UI if events fail
        var safetyTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        safetyTimer.Tick += (s, e) => 
        {
             if (_editingListVM != null)
             {
                 int c = _editingListVM.Cards.Count;
                 if (StatEditing != c.ToString()) 
                 {
                     StatEditing = c.ToString();
                     StatEditingPercentage = Math.Min(c / 10.0, 1.0);
                 }
             }
             if (_revisionListVM != null)
             {
                 int c = _revisionListVM.Cards.Count;
                 if (StatRevision != c.ToString()) 
                 {
                     StatRevision = c.ToString();
                     StatRevisionPercentage = Math.Min(c / 10.0, 1.0);
                 }
             }
             if (_lateListVM != null)
             {
                 int c = _lateListVM.Cards.Count;
                 if (StatLate != c.ToString()) 
                 {
                     StatLate = c.ToString();
                     StatLatePercentage = Math.Min(c / 10.0, 1.0);
                 }
             }
        };
        safetyTimer.Start();
    }

    private async void LoadLogPanelState()
    {
        if (_database != null) 
        {
            var str = await _database.GetAsync<string>("Dashboard.IsLogPanelOpen");
            if (bool.TryParse(str, out var result)) IsLogPanelOpen = result;
        }
    }
    
    // Fallback constructor for Design-Time
    public DashboardViewModel()
    {
         _database = null!;
         _languageService = null!;
         _userName = "Preview User";
    }

    private Avalonia.Threading.DispatcherTimer? _timer;
    private int _lastEditingCount = -1;
    private int _lastRevisionCount = -1;
    private int _lastLateCount = -1;
    
    // --- Widget Colors ---
    [ObservableProperty] private IBrush _statEditingColor = SolidColorBrush.Parse("#3b82f6");
    [ObservableProperty] private IBrush _statRevisionColor = SolidColorBrush.Parse("#f97316");
    [ObservableProperty] private IBrush _statLateColor = SolidColorBrush.Parse("#f97316");
    [ObservableProperty] private IBrush _statPointsColor = SolidColorBrush.Parse("#f97316");
    
    // --- Widget Animation ---
    [ObservableProperty] private TimeSpan _statAnimationDuration = TimeSpan.FromSeconds(1.5);





    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _processStatusText = "Console"; // Default Title
    [ObservableProperty] private Avalonia.Media.IBrush _statusColor = Avalonia.Media.Brushes.Green; // Default logic

    // Progress Reporting
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 100;
    [ObservableProperty] private bool _isDeterminateProgress;

    public ObservableCollection<string> Logs => _logService?.Logs ?? new();
    
    [ObservableProperty] private string _logText = "";
    
    [ObservableProperty] 
    private ObservableCollection<LogItem> _logItems = new();

    [ObservableProperty] private string _logFilterText = "";
    
    [ObservableProperty] 
    private ObservableCollection<string> _filterPresets = new()
    {
        "Error", "Warning", "Sukses", "Script", "Path"
    };

    partial void OnLogFilterTextChanged(string value)
    {
        UpdateLogText();
    }

    [RelayCommand]
    private void StopProcess()
    {
        if (IsProcessing)
        {
            WeakReferenceMessenger.Default.Send(new StopProcessMessage(true));
        }
    }


    
    // Handle Dropped Files on Log Panel
    public async Task HandleDroppedLogFile(string path)
    {
        if (System.IO.File.Exists(path))
        {
            try
            {
                var ext = System.IO.Path.GetExtension(path);
                LogItems.Clear();
                AddLog($"Reading log file: {System.IO.Path.GetFileName(path)}", BMachine.UI.Models.LogLevel.System);

                string[] lines = Array.Empty<string>();

                if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    // DOCX Parsing (Simple Text Extraction)
                    await Task.Run(() => 
                    {
                        try 
                        {
                            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                            using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                            {
                                var docEntry = archive.GetEntry("word/document.xml");
                                if (docEntry != null)
                                {
                                    using (var stream = docEntry.Open())
                                    using (var reader = new System.IO.StreamReader(stream))
                                    {
                                        var xml = reader.ReadToEnd();
                                        // Simple Regex to strip XML tags and handle basic paragraphs
                                        // <w:p> usually denotes a paragraph.
                                        // We can replace <w:p> with newline? Or just strip all tags?
                                        // Stripping all tags joins everything.
                                        // Better: Replace </w:p> with Environment.NewLine, then strip tags.
                                        
                                        var pProcessed = System.Text.RegularExpressions.Regex.Replace(xml, @"</w:p>", Environment.NewLine);
                                        var textOnly = System.Text.RegularExpressions.Regex.Replace(pProcessed, "<.*?>", "");
                                        // Decode XML entities
                                        textOnly = System.Net.WebUtility.HtmlDecode(textOnly);
                                        
                                        lines = textOnly.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                    }
                                }
                                else 
                                {
                                    throw new Exception("Invalid DOCX: missing document.xml");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Failed to parse DOCX: " + ex.Message);
                        }
                    });
                }
                else
                {
                    // Default .txt
                    lines = await System.IO.File.ReadAllLinesAsync(path);
                }

                foreach (var line in lines)
                {
                    ParseLogAndAdd(line.Trim());
                }
                AddLog("--- End of File ---", BMachine.UI.Models.LogLevel.System);
            }
            catch (Exception ex)
            {
                AddLog($"Error reading file: {ex.Message}", BMachine.UI.Models.LogLevel.Error);
            }
        }
    }
    
    private void AddLog(string message, BMachine.UI.Models.LogLevel level)
    {
         LogItems.Add(new BMachine.UI.Models.LogItem(message, level));
    }
    
    private void ParseLogAndAdd(string line)
    {
        var item = ParseLog(line);
        if (item != null) LogItems.Add(item);
    }

    private void UpdateLogText()
    {
        if (_logService == null) return;
        
        // Update Text Block (Clipboard)
        var cleanedLogs = _logService.Logs.Select(line => 
        {
             return line.Replace("[DEBUG] ", "").Replace("[INFO] ", "").Replace("[SUCCESS] ", "").Replace("[ERROR] ", "");
        });
        LogText = string.Join(Environment.NewLine, cleanedLogs);
        
        LogItems.Clear();
        foreach (var line in _logService.Logs)
        {
            var item = ParseLog(line);
            if (item != null)
            {
                // Filter Logic
                if (!string.IsNullOrWhiteSpace(LogFilterText))
                {
                    if (item.Message.Contains(LogFilterText, StringComparison.OrdinalIgnoreCase))
                    {
                        LogItems.Add(item);
                    }
                }
                else
                {
                    LogItems.Add(item);
                }
            }
        }
    }

    private LogItem? ParseLog(string line)
    {
        // --- 1. FILTERING (Hide specific logs) ---
        if (line.Contains("Working Directory:") ||
            line.Contains("'config.json' tidak ditemukan") ||
            line.Contains("Process exited with code"))
        {
            return null;
        }

        // --- 2. REPLACEMENTS (Translate/Alias) ---
        string msg = line;

        // Dynamic Script Name Extraction
        // Pattern: "Asking for INPUT folder for [ScriptName]..."
        var scriptMatch = System.Text.RegularExpressions.Regex.Match(msg, @"Asking for INPUT folder for\s+(.+)\.\.\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (scriptMatch.Success) 
        {
            string scriptName = scriptMatch.Groups[1].Value.Trim();
            msg = $"Menjalankan Script {scriptName}...";
        }
        else if (msg.Contains("Input selected:")) msg = msg.Replace("Input selected:", "Path PILIHAN :");
        else if (msg.Contains("Using Default Output:")) msg = msg.Replace("Using Default Output:", "Output Lokal :");
        else if (msg.Contains("RunPythonProcess called. User:")) msg = msg.Replace("RunPythonProcess called. User:", "Nama User :");
        
        var level = LogLevel.Standard; // Default is Standard (White)
        
        // --- 3. DETECT LEVEL ---
        if (line.IndexOf("[DEBUG]", StringComparison.OrdinalIgnoreCase) >= 0) 
        {
            level = LogLevel.Debug;
        }
        else if (line.IndexOf("[INFO]", StringComparison.OrdinalIgnoreCase) >= 0) 
        {
            level = LogLevel.Info;
        }
        else if (line.IndexOf("[SUCCESS]", StringComparison.OrdinalIgnoreCase) >= 0) 
        {
            level = LogLevel.Success;
        }
        else if (line.IndexOf("[WARNING]", StringComparison.OrdinalIgnoreCase) >= 0) 
        {
            level = LogLevel.Warning;
        }
        else if (line.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0 || 
                 line.IndexOf("Error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 line.IndexOf("Fail:", StringComparison.OrdinalIgnoreCase) >= 0) 
        {
            level = LogLevel.Error;
        }
        
        // --- 4. CLEANUP (Remove Tags & Timestamps) ---
        try 
        {
             var pattern = @"^(\d{1,2}:\d{2}:\d{2}\s+)?(\[[a-zA-Z]+\]\s*)?";
             var match = System.Text.RegularExpressions.Regex.Match(msg, pattern);
             
             if (match.Success && match.Length > 0)
             {
                 msg = msg.Substring(match.Length).Trim();
             }
        }
        catch { /* Fallback */ }

        // Sanity check: if message became empty after cleaning, don't show it unless it was intentionally empty?
        if (string.IsNullOrWhiteSpace(msg)) return null;

        return new LogItem(msg, level);
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<DashboardViewModel, NavSettingsChangedMessage>(this, (r, m) => _ = r.LoadNavSettings());

        WeakReferenceMessenger.Default.Register<BMachine.UI.Messages.RefreshDashboardMessage>(this, async (r, m) => 
        {
            await LoadVisualSettings();
        });

        WeakReferenceMessenger.Default.Register<BMachine.UI.Messages.DashboardVisibilityChangedMessage>(this, async (r, m) => 
        {
            await LoadVisualSettings();
        });
        
        WeakReferenceMessenger.Default.Register<ProfileUpdatedMessage>(this, (r, m) =>
        {
            UserName = m.Value.UserName;
            LoadAvatarImage(m.Value.AvatarSource);
        });
        
        WeakReferenceMessenger.Default.Register<ProcessStatusMessage>(this, (r, m) =>
        {
            IsProcessing = m.Value;
            ProcessStatusText = m.Value ? "Running..." : "Console";
            // Update Status Color (Blue for Running, Green for Ready)
            StatusColor = m.Value ? Avalonia.Media.Brushes.DodgerBlue : Avalonia.Media.Brushes.LimeGreen;

            if (m.Value && !string.IsNullOrEmpty(m.ProcessName)) ProcessStatusText = $"Running {m.ProcessName}...";
            
            // Auto open log panel on start? User preference. Maybe yes.
            if (m.Value) IsLogPanelOpen = true;
        });

        // Register all messages (OpenTextFileMessage, AppFocusChangedMessage)
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public void Receive(AppFocusChangedMessage message)
    {
         if (_timer != null)
         {
             if (message.Value) // Focused
             {
                 _timer.Interval = TimeSpan.FromSeconds(5);
             }
             else // Background
             {
                 _timer.Interval = TimeSpan.FromSeconds(60);
             }
         }
    }

    public void Receive(NavigateBackMessage message)
    {
        NavigateBack();
    }

    private async Task LoadVisualSettings()
    {
        if (_database == null) return;
        
        // Colors
        StatEditingColor = await GetBrushFromSetting("Settings.Color.Editing", "#3b82f6");
        StatRevisionColor = await GetBrushFromSetting("Settings.Color.Revision", "#f97316");
        StatLateColor = await GetBrushFromSetting("Settings.Color.Late", "#f97316");
        StatPointsColor = await GetBrushFromSetting("Settings.Color.Points", "#f97316");
        
        // Animation Speed
        var speedStr = await _database.GetAsync<string>("Settings.StatSpeed");
        int speedIdx = 1; // Default Normal
        if (!string.IsNullOrEmpty(speedStr)) int.TryParse(speedStr, out speedIdx);
        
        StatAnimationDuration = speedIdx switch
        {
            0 => TimeSpan.FromSeconds(3.0), // Slow
            2 => TimeSpan.FromSeconds(0.5), // Fast
            _ => TimeSpan.FromSeconds(1.5)  // Normal
        };
        
        // Load Visibility
        IsGdriveVisible = bool.Parse(await _database.GetAsync<string>("Settings.Dash.Gdrive") ?? "True");
        IsPixelcutVisible = bool.Parse(await _database.GetAsync<string>("Settings.Dash.Pixelcut") ?? "True");
        IsBatchVisible = bool.Parse(await _database.GetAsync<string>("Settings.Dash.Batch") ?? "True");
        IsLockerVisible = bool.Parse(await _database.GetAsync<string>("Settings.Dash.Lock") ?? "True");
        IsPointVisible = bool.Parse(await _database.GetAsync<string>("Settings.Dash.Point") ?? "True");

        // Load refresh intervals (Seconds)
        EditingRefreshSeconds = int.Parse(await _database.GetAsync<string>("Settings.Interval.Editing") ?? "60");
        RevisionRefreshSeconds = int.Parse(await _database.GetAsync<string>("Settings.Interval.Revision") ?? "60");
        LateRefreshSeconds = int.Parse(await _database.GetAsync<string>("Settings.Interval.Late") ?? "60");
        PointsRefreshSeconds = int.Parse(await _database.GetAsync<string>("Settings.Interval.Points") ?? "60");
    }

    [ObservableProperty] private int _editingRefreshSeconds = 30;
    [ObservableProperty] private int _revisionRefreshSeconds = 30;
    [ObservableProperty] private int _lateRefreshSeconds = 30;
    [ObservableProperty] private int _pointsRefreshSeconds = 30; // Reduced from 60s for faster GSheet updates
    
    // Flag to bypass interval check on first stats load
    private bool _isFirstStatsLoad = true;

    private DateTime _lastEditingSync = DateTime.MinValue;
    private DateTime _lastRevisionSync = DateTime.MinValue;
    private DateTime _lastLateSync = DateTime.MinValue;
    private DateTime _lastPointsSync = DateTime.MinValue;
    
    private async Task<IBrush> GetBrushFromSetting(string key, string defaultHex)
    {
        var hex = await _database.GetAsync<string>(key);
        if (string.IsNullOrEmpty(hex)) hex = defaultHex;
        if (hex == "RANDOM") return SolidColorBrush.Parse("#FFFFFF"); // Fallback for Random
        try { return SolidColorBrush.Parse(hex); }
        catch { return SolidColorBrush.Parse(defaultHex); }
    }
    
    private void LoadAvatarImage(string source)
    {
        try
        {
            if (string.IsNullOrEmpty(source) || source == "default")
            {
                UserAvatar = null;
                return;
            }

            if (source.StartsWith("preset:"))
            {
                var filename = source.Substring(7); // "preset:".Length
                var uri = new Uri($"avares://BMachine.UI/Assets/Avatars/{filename}");
                if (Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    UserAvatar = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                }
            }
            else if (source.StartsWith("custom:"))
            {
                var path = source.Substring(7);
                if (System.IO.File.Exists(path))
                {
                     // Ensure we don't lock the file? Bitmap constructor locks file until disposed?
                     // Loading into memory stream is safer
                     using var stream = File.OpenRead(path);
                     UserAvatar = new Avalonia.Media.Imaging.Bitmap(stream);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Avatar Load Error: {ex.Message}");
            UserAvatar = null;
        }
    }
    // ---------------------
    
    // Initialize Timer in Constructor or LoadData
    // Let's do it in LoadData to ensure everything is ready
    
    [RelayCommand]
    private async Task LoadData()
    {
        if (_activityService == null || _database == null) return;

        // Load User Name
        var storedName = await _database.GetAsync<string>("User.Name");
        if (!string.IsNullOrEmpty(storedName))
        {
            UserName = storedName;
        }
        else
        {
            // Save default to database
            UserName = "USER";
            await _database.SetAsync("User.Name", UserName);
        }
        
        // Load Avatar
        var storedAvatar = await _database.GetAsync<string>("User.Avatar");
        LoadAvatarImage(storedAvatar ?? "default");

        UpdateGreeting();

        UpdateGreeting();

        // Load Floating Widget State (Stored as string because IDatabase requires class)
        var isWidgetStr = await _database.GetAsync<string>("Settings.FloatingWidget");
        bool isWidgetVisible = true; // Default True (Visible)
        if (!string.IsNullOrEmpty(isWidgetStr))
        {
             bool.TryParse(isWidgetStr, out isWidgetVisible);
        }
        // Load Smart Orb State (Persistence)
        var savedOrbState = await _database.GetAsync<string>("Dashboard.IsFloatingWidgetVisible");
        
        // Default to false if not set (first run), or true if saved as true
        bool isOrbVisible = false;
        if (!string.IsNullOrEmpty(savedOrbState))
        {
            bool.TryParse(savedOrbState, out isOrbVisible);
        }
        
        IsFloatingWidgetVisible = isOrbVisible;
        
        // Broadcast initial state
        WeakReferenceMessenger.Default.Send(new FloatingWidgetMessage(IsFloatingWidgetVisible)); 

        await LoadVisualSettings(); // Load Colors & Speed
        




        // Load Activities
        var logs = await _activityService.GetRecentAsync(10);
        Activities.Clear();
        foreach (var log in logs)
        {
            Activities.Add(new ActivityItem 
            { 
                Title = log.Title + ": " + log.Description, 
                TimeDisplay = GetSmartDateString(log.CreatedAt.ToLocalTime())
            });
        }
        
        if (Activities.Count == 0)
        {
             await _activityService.LogAsync("System", "Welcome", "Dashboard initialized");
             Activities.Add(new ActivityItem { Title = "Welcome: Dashboard initialized", TimeDisplay = "Now" });
        }
        
        // Initial Sync (Checks intervals)
        await SyncTrelloStats();
        
        // Start Realtime Timer (Every 1 seconds)
        if (_timer == null)
        {
            _timer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // Run fast to check intervals
            };
            _timer.Tick += async (s, e) => 
            {
                 // Console.WriteLine("[Timer] Tick");
                 // Use 1-second tick to check against intervals
                 await SyncTrelloStats();
            };
            _timer.Start();
        }
    }
    
    [ObservableProperty] private string _statEditing = "0";
    [ObservableProperty] private double _statEditingPercentage = 0;
    
    [ObservableProperty] private string _statRevision = "0";
    [ObservableProperty] private double _statRevisionPercentage = 0;
    
    [ObservableProperty] private string _statLate = "0";
    [ObservableProperty] private double _statLatePercentage = 0;
    
    [ObservableProperty] private string _statPoints = "0";
    [ObservableProperty] private double _statPointsPercentage = 0;

    private async Task SyncTrelloStats()
    {
        const double MAX_CARDS = 10.0;
        
        // Force initial load (bypass interval check first time)
        bool shouldSync = _isFirstStatsLoad; 

        // 1. Editing List
        try 
        {
             if (shouldSync || (DateTime.Now - _lastEditingSync).TotalSeconds >= EditingRefreshSeconds)
             {
                 _lastEditingSync = DateTime.Now;
                 await _editingListVM.RefreshCommand.ExecuteAsync(null);
                 
                 int count = _editingListVM.Cards.Count;
                 StatEditing = count.ToString();
                 StatEditingPercentage = Math.Min(count / MAX_CARDS, 1.0);
                 
                 // Check for new cards
                 if (_lastEditingCount != -1 && count > _lastEditingCount)
                 {
                     int diff = count - _lastEditingCount;
                     string msg = $"New Card Board EDITING {diff}";
                     TriggerWindowsNotification("BMachine Update", msg);
                     await _activityService.LogAsync("Trello", "New Card", msg);
                     await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                     {
                         Activities.Insert(0, new ActivityItem { Title = $"New Card: {msg}", TimeDisplay = GetSmartDateString(DateTime.Now) });
                         if (Activities.Count > 10) Activities.RemoveAt(Activities.Count - 1);
                     });
                 }
                 _lastEditingCount = count;
             }
        }
        catch (Exception ex) { /* Log? */ }

        // 2. Revision List
        try 
        {
             if (shouldSync || (DateTime.Now - _lastRevisionSync).TotalSeconds >= RevisionRefreshSeconds)
             {
                 _lastRevisionSync = DateTime.Now;
                 await _revisionListVM.RefreshCommand.ExecuteAsync(null);
                 
                 int count = _revisionListVM.Cards.Count;
                 StatRevision = count.ToString();
                 StatRevisionPercentage = Math.Min(count / MAX_CARDS, 1.0);
                 
                 if (_lastRevisionCount != -1 && count > _lastRevisionCount)
                 {
                     int diff = count - _lastRevisionCount;
                     string msg = $"New Card Board REVISI {diff}";
                     TriggerWindowsNotification("BMachine Update", msg);
                     await _activityService.LogAsync("Trello", "New Card", msg);
                 }
                 _lastRevisionCount = count;
             }
        }
        catch { }

        // 3. Late List
        try 
        {
             if (shouldSync || (DateTime.Now - _lastLateSync).TotalSeconds >= LateRefreshSeconds)
             {
                 _lastLateSync = DateTime.Now;
                 await _lateListVM.RefreshCommand.ExecuteAsync(null);
                 
                 int count = _lateListVM.Cards.Count;
                 StatLate = count.ToString();
                 StatLatePercentage = Math.Min(count / MAX_CARDS, 1.0);
                 
                 if (_lastLateCount != -1 && count > _lastLateCount)
                 {
                     int diff = count - _lastLateCount;
                     string msg = $"New Card Board SUSULAN {diff}";
                     TriggerWindowsNotification("BMachine Update", msg);
                     await _activityService.LogAsync("Trello", "New Card", msg);
                 }
                 _lastLateCount = count;
             }
        }
        catch { }
    
        // StatPoints reset removed to persist value
        
        // 4. Google Sheets Integration for Points
        if (shouldSync || (DateTime.Now - _lastPointsSync).TotalSeconds >= PointsRefreshSeconds)
        {
            // Syncing...
            const double MAX_POINTS = 1500.0;
            _lastPointsSync = DateTime.Now;
            
            // Console.WriteLine("[Points] Starting sync..."); // Cleaned up
            
            try 
            {
                var credsPath = await _database.GetAsync<string>("Google.CredsPath");
                var sheetId = await _database.GetAsync<string>("Google.SheetId");
                var sheetName = await _database.GetAsync<string>("Google.SheetName");
                var sheetCol = await _database.GetAsync<string>("Google.SheetColumn");
                var sheetRow = await _database.GetAsync<string>("Google.SheetRow");
                
                // _logService?.AddLog($"[Points Debug] Config - Col: '{sheetCol}', Row: '{sheetRow}', Sheet: '{sheetName}'", LogLevel.Debug);
                
                if (!string.IsNullOrEmpty(credsPath) && 
                    !string.IsNullOrEmpty(sheetId) && 
                    !string.IsNullOrEmpty(sheetName) &&
                    !string.IsNullOrEmpty(sheetCol) &&
                    !string.IsNullOrEmpty(sheetRow))
                {
                    if (!System.IO.File.Exists(credsPath))
                    {
                        _logService?.AddLog($"[GSheet Error] File kredensial tidak ditemukan di: {credsPath}");
                        StatPoints = "ErrFile";
                    }
                    else
                    {
                        await Task.Run(async () => 
                        {
                            int retries = 3;
                            int delay = 1000;
                            
                            while (retries > 0)
                            {
                                try
                                {
                                    // Initialize Google Sheets Service
                                    GoogleCredential credential;
                                    using (var stream = new System.IO.FileStream(credsPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                                    {
                                        credential = GoogleCredential.FromStream(stream)
                                            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
                                    }
                                    
                                    using (var service = new SheetsService(new BaseClientService.Initializer()
                                    {
                                        HttpClientInitializer = credential,
                                        ApplicationName = "BMachine"
                                    }))
                                    {
                                        service.HttpClient.Timeout = TimeSpan.FromMinutes(1); // Reduced timeout
                                        var range = $"{sheetName}!{sheetCol}{sheetRow}";
                                        
                                        var request = service.Spreadsheets.Values.Get(sheetId, range);
                                        var response = await request.ExecuteAsync();
                                        
                                        if (response.Values != null && response.Values.Count > 0 && response.Values[0].Count > 0)
                                        {
                                            var valStr = response.Values[0][0]?.ToString() ?? "0";
                                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                            {
                                                StatPoints = valStr;
                                                // Calculate Percentage (Assuming Max 1500?)
                                                if (double.TryParse(valStr, out double dVal))
                                                {
                                                     StatPointsPercentage = Math.Max(0, Math.Min(dVal / MAX_POINTS, 1.0));
                                                }
                                            });
                                            await _database.SetAsync("Cache.GSheet.Points", valStr);
                                            // Success
                                            break; 
                                        }
                                    }
                                    break; // If we get here (no values or success), stop retrying
                                }
                                catch (Exception ex)
                                {
                                    retries--;
                                    if (retries == 0)
                                    {
                                        _logService?.AddLog($"[GSheet Fail] After 3 attempts: {ex.Message}");
                                        // Keep previous value or show Err?
                                        // StatPoints = "Err"; // Maybe keep last known
                                    }
                                    else
                                    {
                                        await Task.Delay(delay);
                                        delay *= 2; // Exponential backoff
                                    }
                                }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.AddLog($"[Points Error] {ex.Message}");
            }
        }

        
        // Reset first load flag to allow interval-based refreshes
        if (_isFirstStatsLoad) _isFirstStatsLoad = false;
    }

    private void TriggerWindowsNotification(string title, string message)
    {
         // Check if "Notifikasi" extension is enabled (file exists in Plugins)
         var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
         var notifPluginPath = Path.Combine(pluginsDir, "Notifikasi.dll");
         
         if (!File.Exists(notifPluginPath)) return; // Extension disabled or missing

         Task.Run(() => 
         {
             try 
             {
                 var ps = $"& {{Add-Type -AssemblyName System.Windows.Forms; $n = New-Object System.Windows.Forms.NotifyIcon; $n.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Get-Process -Id $pid).Path); $n.Visible = $True; $n.ShowBalloonTip(3000, '{title}', '{message}', [System.Windows.Forms.ToolTipIcon]::Info); Start-Sleep 3; $n.Dispose()}}";
                 var info = new System.Diagnostics.ProcessStartInfo
                 {
                     FileName = "powershell",
                     Arguments = $"-WindowStyle Hidden -Command \"{ps.Replace("\"", "\\\"")}\"",
                     UseShellExecute = false,
                     CreateNoWindow = true
                 };
                 System.Diagnostics.Process.Start(info);
             }
             catch (Exception ex) 
             { 
                 Console.WriteLine($"Notif Failed: {ex.Message}"); 
             }
         });
    }

    private async Task<string?> GetTrelloListCount(string? listId, string? apiKey, string? token)
    {
        if (string.IsNullOrEmpty(listId) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return null;
        
        var cacheKey = $"Cache.ListCount.{listId}";
        
        try 
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var url = $"https://api.trello.com/1/lists/{listId}?key={apiKey}&token={token}&cards=open&fields=none";
            var json = await client.GetStringAsync(url);
            
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cards", out var cardsElement) && 
                cardsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var count = cardsElement.GetArrayLength().ToString();
                await _database.SetAsync(cacheKey, count);
                IsOnline = true;
                return count;
            }
            return "0";
        }
        catch 
        {
            IsOnline = false;
            var cached = await _database.GetAsync<string>(cacheKey);
            return cached ?? null;
        }
    }

    private void UpdateGreeting()
    {
        if (_languageService == null) 
        {
             // Fallback
             Greeting = "Hello"; 
             return;
        }

        var hour = DateTime.Now.Hour;
        string key;
        
        if (hour < 12) key = "Dashboard.GoodMorning";
        else if (hour < 15) key = "Dashboard.GoodAfternoon";
        else if (hour < 18) key = "Dashboard.GoodEvening";
        else key = "Dashboard.GoodEvening"; 
        
        Greeting = _languageService.GetString(key);
    }



    public async void Receive(OpenTextFileMessage message)
    {
        try 
        {
            if (System.IO.File.Exists(message.Value))
            {
                var text = await System.IO.File.ReadAllTextAsync(message.Value);
                
                // Clear and show file content
                LogItems.Clear();
                ProcessStatusText = $"Viewing: {System.IO.Path.GetFileName(message.Value)}";
                StatusColor = Brushes.Cyan;
                
                // One block to preserve formatting
                var item = new BMachine.UI.Models.LogItem(text, BMachine.UI.Models.LogLevel.Standard);
                // Optionally set color if needed, but Standard (White) is fine for text file
                // item.CustomColor = Brushes.LightGray; 
                
                LogItems.Add(item);
                
                IsLogPanelOpen = true;
            }
        }
        catch (Exception ex)
        {
             LogItems.Add(new BMachine.UI.Models.LogItem($"Error reading file: {ex.Message}", BMachine.UI.Models.LogLevel.Error));
             IsLogPanelOpen = true;
        }
    }

    private string GetSmartDateString(DateTime date)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var inputDate = date.Date;

        if (inputDate == today)
        {
            return $"Today, {date:HH.mm}";
        }
        else if (inputDate == yesterday)
        {
            return $"Yesterday, {date:HH.mm}";
        }
        else
        {
            return $"{date:dd MMM yy}, {date:HH.mm}";
        }
    }
    [RelayCommand]
    private void OpenLeaderboard()
    {
        // 1. Switch to Points Tab (Inside App)
        IsPointsTabSelected = true;
        // 2. Trigger data refresh (optional)
        PointLeaderboardVM.LoadDataCommand.Execute(null);
    }
    

}

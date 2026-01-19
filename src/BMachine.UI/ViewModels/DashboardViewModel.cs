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

public partial class DashboardViewModel : ObservableObject
{
    private readonly IActivityService _activityService;
    private readonly IDatabase _database;
    private readonly ILanguageService? _languageService;
    private readonly Services.IProcessLogService? _logService;

    public IDatabase Database => _database;
    public ILanguageService? Language => _languageService;





    [ObservableProperty] private bool _isLogPanelOpen;

    partial void OnIsLogPanelOpenChanged(bool value)
    {
         _database?.SetAsync("Dashboard.IsLogPanelOpen", value.ToString());
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

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
    [ObservableProperty] private double _navButtonWidth = 40; // Reduced to 40 (icon width only)
    [ObservableProperty] private double _navButtonHeight = 40;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(NavCornerRadiusStruct))]
    private double _navCornerRadius = 20;

    public CornerRadius NavCornerRadiusStruct => new CornerRadius(NavCornerRadius);

    [ObservableProperty] private double _navFontSize = 14;

    partial void OnNavButtonWidthChanged(double value) => _database?.SetAsync("Dashboard.Nav.Width", value.ToString());
    partial void OnNavButtonHeightChanged(double value) => _database?.SetAsync("Dashboard.Nav.Height", value.ToString());
    partial void OnNavCornerRadiusChanged(double value) => _database?.SetAsync("Dashboard.Nav.Radius", value.ToString());
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
    private string _userName = "User";

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
    private int _selectedTabIndex = 0; // 0=Home, 1=Grid, 2=Locker, 3=Pixelcut, 4=GDrive

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
    
    // Gdrive ViewModel
    [ObservableProperty]
    private GdriveViewModel _gdriveVM;

    // Batch Master ViewModel
    [ObservableProperty]
    private BatchViewModel _batchVM;

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }
    
    [RelayCommand]
    private void OpenEditingList()
    {
        OpenEditingListRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenRevisionList()
    {
        OpenRevisionListRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenLateList()
    {
        OpenLateListRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ClearActivities()
    {
        Activities.Clear();
        if (_activityService != null)
        {
             await _activityService.ClearAsync();
        }
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
    private void LogOut()
    {
        WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.ShutdownMessage());
    }

    public DashboardViewModel(IActivityService activityService, IDatabase database, ILanguageService? languageService = null, Services.IProcessLogService? logService = null)
    {
        _activityService = activityService;
        _database = database;
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
        
        LoadDataCommand.Execute(null);
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
         _activityService = null!;
         _database = null!;
         _languageService = null!;
         _userName = "Preview User";
         _activities = new ObservableCollection<ActivityItem>
         {
             new ActivityItem { Title = "Design Data 1", TimeDisplay = "10:00" },
             new ActivityItem { Title = "Design Data 2", TimeDisplay = "11:00" }
         };
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
                AddLog($"Reading log file: {System.IO.Path.GetFileName(path)}", Avalonia.Media.Colors.Cyan);

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
                AddLog("--- End of File ---", Avalonia.Media.Colors.Cyan);
            }
            catch (Exception ex)
            {
                AddLog($"Error reading file: {ex.Message}", Avalonia.Media.Colors.Red);
            }
        }
    }
    
    private void AddLog(string message, Avalonia.Media.Color color)
    {
         LogItems.Add(new LogItem(message, LogLevel.Info) { Color = new Avalonia.Media.SolidColorBrush(color) });
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
    }
    
    private async Task<IBrush> GetBrushFromSetting(string key, string defaultHex)
    {
        var hex = await _database.GetAsync<string>(key);
        if (string.IsNullOrEmpty(hex)) hex = defaultHex;
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
        UserName = !string.IsNullOrEmpty(storedName) ? storedName : "ABENG";
        
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
        
        // Initial Sync
        await SyncTrelloStats();
        
        // Start Realtime Timer (Every 5 seconds)
        if (_timer == null)
        {
            _timer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += async (s, e) => 
            {
                 Console.WriteLine("[Timer] Tick");
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
        // MAX reference values for progress calculation
        const double MAX_CARDS = 10.0; 
        const double MAX_POINTS = 1500.0;

        // 1. Trello Stats - Fetch Real Card Counts from Lists
        var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
        var token = await _database.GetAsync<string>("Trello.Token");
        
        var editingId = await _database.GetAsync<string>("Trello.EditingListId");
        if (!string.IsNullOrEmpty(editingId)) 
        {
            var newValStr = await GetTrelloListCount(editingId, apiKey, token);
            if (newValStr != null) // Only update if fetch succeeded
            {
                if (StatEditing != newValStr || _lastEditingCount == -1)
                {
                     StatEditing = newValStr;
                     if (int.TryParse(newValStr, out int val))
                     {
                         // Animate to new Percentage
                         StatEditingPercentage = Math.Min(val / MAX_CARDS, 1.0);
                     }
                     else StatEditingPercentage = 0;
                     
                     // Notification Logic
                     if (int.TryParse(newValStr, out int currentCount))
                     {
                         if (_lastEditingCount != -1 && currentCount > _lastEditingCount)
                         {
                             int diff = currentCount - _lastEditingCount;
                             string msg = $"New Card Board EDITING {diff}";
                             
                             TriggerWindowsNotification("BMachine Update", msg);
                             
                            // Log to Activity Feed And Refresh List
                            await _activityService.LogAsync("Trello", "New Card", msg);
                            
                            // Refresh Activities UI immediately
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                            {
                                Activities.Insert(0, new ActivityItem 
                                { 
                                    Title = $"New Card: {msg}", 
                                    TimeDisplay = GetSmartDateString(DateTime.Now)
                                });
                                if (Activities.Count > 10) Activities.RemoveAt(Activities.Count - 1);
                            });
                        }
                        
                        _lastEditingCount = currentCount;
                        // Save last count
                        await _database.SetAsync("Trello.LastEditingCount", _lastEditingCount.ToString());
                    }
                }
            }
        }

        
        var revisionId = await _database.GetAsync<string>("Trello.RevisionListId");
        if (!string.IsNullOrEmpty(revisionId)) 
        {
             var newValStr = await GetTrelloListCount(revisionId, apiKey, token);
             if (newValStr != null) // Only update if fetch succeeded
             {
                 if (StatRevision != newValStr || _lastRevisionCount == -1)
                 {
                     StatRevision = newValStr;
                     if (int.TryParse(newValStr, out int val))
                     {
                         StatRevisionPercentage = Math.Min(val / MAX_CARDS, 1.0);
                     }
                     else StatRevisionPercentage = 0;
    
                     if (int.TryParse(newValStr, out int currentCount))
                     {
                         if (_lastRevisionCount != -1 && currentCount > _lastRevisionCount)
                         {
                             int diff = currentCount - _lastRevisionCount;
                             string msg = $"New Card Board REVISI {diff}";
                             TriggerWindowsNotification("BMachine Update", msg);
                             await _activityService.LogAsync("Trello", "New Card", msg);
                             await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                             {
                                 Activities.Insert(0, new ActivityItem { Title = $"New Card: {msg}", TimeDisplay = GetSmartDateString(DateTime.Now) });
                                 if (Activities.Count > 10) Activities.RemoveAt(Activities.Count - 1);
                             });
                         }
                         _lastRevisionCount = currentCount;
                     }
                 }
             }
        }
        
        var lateId = await _database.GetAsync<string>("Trello.LateListId");
        if (!string.IsNullOrEmpty(lateId)) 
        {
            var newValStr = await GetTrelloListCount(lateId, apiKey, token);
            if (newValStr != null) // Only update if fetch succeeded
            {
                if (StatLate != newValStr || _lastLateCount == -1)
                {
                    StatLate = newValStr;
                    if (int.TryParse(newValStr, out int val))
                    {
                        StatLatePercentage = Math.Min(val / MAX_CARDS, 1.0);
                    }
                    else StatLatePercentage = 0;
    
                    if (int.TryParse(newValStr, out int currentCount))
                    {
                        if (_lastLateCount != -1 && currentCount > _lastLateCount)
                        {
                            int diff = currentCount - _lastLateCount;
                            string msg = $"New Card Board SUSULAN {diff}";
                            TriggerWindowsNotification("BMachine Update", msg);
                            await _activityService.LogAsync("Trello", "New Card", msg);
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                            {
                                ActivityItem item = new ActivityItem { Title = $"New Card: {msg}", TimeDisplay = GetSmartDateString(DateTime.Now) };
                                Activities.Insert(0, item);
                                if (Activities.Count > 10) Activities.RemoveAt(Activities.Count - 1);
                            });
                        }
                        _lastLateCount = currentCount;
                    }
                }
            }
        }
        
        // 2. Google Sheets Integration for Points
        try 
        {
            var credsPath = await _database.GetAsync<string>("Google.CredsPath");
            var sheetId = await _database.GetAsync<string>("Google.SheetId");
            var sheetName = await _database.GetAsync<string>("Google.SheetName");
            var sheetCol = await _database.GetAsync<string>("Google.SheetColumn");
            var sheetRow = await _database.GetAsync<string>("Google.SheetRow");
            
            // DEBUG: Print config status once (or on error)
            // Console.WriteLine($"[GSheet Config] Path: {credsPath}, ID: {sheetId}, Range: {sheetName}!{sheetCol}{sheetRow}");

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
                            var range = $"{sheetName}!{sheetCol}{sheetRow}";
                            // _logService?.AddLog($"[GSheet Info] Mengambil data dari: {range}");
                            
                            var request = service.Spreadsheets.Values.Get(sheetId, range);
                            var response = await request.ExecuteAsync();
                            
                            if (response.Values != null && response.Values.Count > 0 && response.Values[0].Count > 0)
                            {
                                var valStr = response.Values[0][0]?.ToString() ?? "0";
                                StatPoints = valStr;
                                
                                if (double.TryParse(valStr, out double val))
                                {
                                     StatPointsPercentage = Math.Min(val / MAX_POINTS, 1.0);
                                }
                            }
                            else
                            {
                                 _logService?.AddLog($"[GSheet Warning] Data tidak ditemukan di range: {range}");
                                 StatPoints = "0";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         _logService?.AddLog($"[GSheet Exception] {ex.Message}");
                         StatPoints = "ErrAPI";
                    }
                }
            }
            else
            {
                 // Config missing debug
                 // _logService?.AddLog($"[GSheet Config] Konfigurasi belum lengkap. Path: {credsPath}, ID: {sheetId}");
                 if (string.IsNullOrEmpty(StatPoints) || StatPoints == "0") StatPoints = "0";
            }
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[GSheet System Error] {ex.Message}");
        }
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
        
        try 
        {
            using var client = new System.Net.Http.HttpClient();
            var url = $"https://api.trello.com/1/lists/{listId}?key={apiKey}&token={token}&cards=open&fields=none";
            var json = await client.GetStringAsync(url);
            
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cards", out var cardsElement) && 
                cardsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return cardsElement.GetArrayLength().ToString();
            }
            return "0";
        }
        catch 
        {
            return null; // Return null on error to prevent resetting state to 0
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
}

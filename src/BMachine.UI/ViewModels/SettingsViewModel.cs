using BMachine.SDK;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Services;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.IO;
using QRCoder;
using BMachine.UI.Messages;
using BMachine.UI.Models;

namespace BMachine.UI.ViewModels;

public class ColorOption
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "";
    public IBrush? Brush { get; set; }
}

public class TrelloItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class ScriptOrderItem
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    private readonly Action? _navigateBack;
    private readonly IThemeService? _themeService;
    private readonly ILanguageService? _languageService;
    
    public ILanguageService? Language => _languageService;
    
    // Mock Data Sources
    [ObservableProperty] private ObservableCollection<TrelloItem> _mockBoards = new();
    [ObservableProperty] private ObservableCollection<TrelloItem> _editingLists = new();
    [ObservableProperty] private ObservableCollection<TrelloItem> _revisionLists = new();
    [ObservableProperty] private ObservableCollection<TrelloItem> _lateLists = new();
    
    // Script Ordering
    [ObservableProperty] private ObservableCollection<ScriptOrderItem> _scriptOrderList = new();
    [ObservableProperty] private ScriptOrderItem? _selectedScriptItem;

    // Static Cache to persist across navigations
    private static List<TrelloItem>? _staticBoardCache;

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _avatarImage;

    [ObservableProperty]
    private string _avatarSource = "default"; // "default", "preset:X", "custom:path"

    partial void OnAvatarSourceChanged(string value)
    {
        try
        {
            if (string.IsNullOrEmpty(value) || value == "default")
            {
                AvatarImage = null;
                return;
            }

            if (value.StartsWith("preset:"))
            {
                var filename = value.Substring(7);
                // Try avares
                 var assemblyNames = new[] { "BMachine.UI", "BMachine.App", "BMachine" };
                 bool loaded = false;
                 foreach(var asm in assemblyNames)
                 {
                    var uri = new Uri($"avares://{asm}/Assets/Avatars/{filename}");
                    if (Avalonia.Platform.AssetLoader.Exists(uri))
                    {
                        AvatarImage = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                        loaded = true;
                        break;
                    }
                 }
                 
                 // Try fallback if not loaded
                 if (!loaded)
                 {
                     var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Avatars", filename);
                     if (File.Exists(path))
                     {
                         using var stream = File.OpenRead(path);
                         AvatarImage = new Avalonia.Media.Imaging.Bitmap(stream);
                     }
                 }
            }
            else if (value.StartsWith("custom:"))
            {
                var path = value.Substring(7);
                if (System.IO.File.Exists(path))
                {
                    using var stream = System.IO.File.OpenRead(path);
                    AvatarImage = new Avalonia.Media.Imaging.Bitmap(stream);
                }
            }
        }
        catch { AvatarImage = null; }
    }

    public event Action? OpenAvatarSelectionRequested;

    [RelayCommand]
    private void OpenAvatarSelection()
    {
        OpenAvatarSelectionRequested?.Invoke();
    }
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsContentVisible))]
    private bool _isMobileView = false;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsContentVisible))]
    private bool _isMobileContentOpen = false; // True if viewing detail in mobile

    public bool IsSidebarVisible => !IsMobileView || !IsMobileContentOpen;
    public bool IsContentVisible => !IsMobileView || IsMobileContentOpen;

    [RelayCommand]
    private void NavigateBack()
    {
        IsMobileContentOpen = false;
    }

    [ObservableProperty]
    private double _navButtonWidth = 120;
    partial void OnNavButtonWidthChanged(double value) => SaveAndNotify(value, "Dashboard.Nav.Width");

    [ObservableProperty] private double _navButtonHeight = 40;
    partial void OnNavButtonHeightChanged(double value) => SaveAndNotify(value, "Dashboard.Nav.Height");

    [ObservableProperty] private double _navCornerRadius = 20;
    partial void OnNavCornerRadiusChanged(double value) => SaveAndNotify(value, "Dashboard.Nav.Radius");

    [ObservableProperty] private double _navFontSize = 14;
    partial void OnNavFontSizeChanged(double value) => SaveAndNotify(value, "Dashboard.Nav.FontSize");

    private void SaveAndNotify(double value, string key)
    {
        _database.SetAsync(key, value.ToString());
        WeakReferenceMessenger.Default.Send(new NavSettingsChangedMessage());
    }

    private async Task LoadNavSettings()
    {
        var w = await _database.GetAsync<string>("Dashboard.Nav.Width");
        if (double.TryParse(w, out double dW)) NavButtonWidth = dW;

        var h = await _database.GetAsync<string>("Dashboard.Nav.Height");
        if (double.TryParse(h, out double dH)) NavButtonHeight = dH;

        var r = await _database.GetAsync<string>("Dashboard.Nav.Radius");
        if (double.TryParse(r, out double dR)) NavCornerRadius = dR;
        
        var f = await _database.GetAsync<string>("Dashboard.Nav.FontSize");
        if (double.TryParse(f, out double dF)) NavFontSize = dF;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSelected))]
    [NotifyPropertyChangedFor(nameof(IsAccountSelected))]
    [NotifyPropertyChangedFor(nameof(IsNotificationsSelected))]
    [NotifyPropertyChangedFor(nameof(IsScriptsSelected))]
    [NotifyPropertyChangedFor(nameof(IsPathsSelected))] 
    private int _selectedMenuIndex = -1;
    
    partial void OnSelectedMenuIndexChanged(int value)
    {
        // Responsive Logic
        if (IsMobileView && value != -1)
        {
            IsMobileContentOpen = true;
        }
        
        // Navigation Logic
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsAppearanceSelected));
        OnPropertyChanged(nameof(IsAccountSelected));
        OnPropertyChanged(nameof(IsNotificationsSelected));
        OnPropertyChanged(nameof(IsScriptsManagerSelected)); // New
        OnPropertyChanged(nameof(IsScriptsSelected));
        OnPropertyChanged(nameof(IsPathsSelected));
        
        // Lazy Load Logic
        if (value == 2 && IsTrelloConnected) // 2 = Account
        {
             // Trigger Trello Refresh if connected
             // We fire and forget here or use a command if needed, but async void via event handler pattern is okay-ish in partial methods, 
             // but better to wrap in Task.Run or safe invoke.
             _ = RefreshTrelloData();
        }
        
        if (value == 4) // 4 = Scripts Manager
        {
             LoadScriptOrder();
        }
    }
    
    public bool IsGeneralSelected => SelectedMenuIndex == 0;
    public bool IsAppearanceSelected => SelectedMenuIndex == 1;
    public bool IsAccountSelected => SelectedMenuIndex == 2;
    public bool IsNotificationsSelected => SelectedMenuIndex == 3;
    public bool IsScriptsManagerSelected => SelectedMenuIndex == 4; // New
    public bool IsScriptsSelected => SelectedMenuIndex == 6; // Moved to 6 (Shortcuts) due to insertion? 
    // Wait, let's keep it simple. Index 4 matched UI.
    // The previous code had "Scripts" at 4. I changed UI to "Script Manager" at 4.
    // "Shortcuts" (was Scripts?) is now likely 5 or removed?
    // Let's look at UI again.
    public bool IsPathsSelected => SelectedMenuIndex == 5;

    // Sub-ViewModels
    public PathSettingsViewModel? PathSettingsVM { get; private set; }

    // Theme Settings
    [ObservableProperty]
    private int _selectedThemeIndex = 0; // 0=Dark, 1=Light
    
    partial void OnSelectedThemeIndexChanged(int value)
    {
         UpdateTheme(value == 0);
    }
    
    [ObservableProperty]
    private bool _isDarkMode = true; // Default Dark
    
    partial void OnIsDarkModeChanged(bool value)
    {
        SelectedThemeIndex = value ? 0 : 1;
    }
    
    private void UpdateTheme(bool isDark)
    {
        // 1. Update Avalonia Theme
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        }
        
        // 2. Persist
        _database?.SetAsync("Settings.Theme", isDark ? "Dark" : "Light");
        
        // 3. Notify Service
        _themeService?.SetTheme(isDark ? ThemeVariantType.Dark : ThemeVariantType.Light);
    }
    
    [ObservableProperty] private string _darkBackgroundColor = "#1C1C1C"; // Default Dark
    [ObservableProperty] private string _lightBackgroundColor = "#F5F5F5"; // Default Light
    
    [ObservableProperty] private IBrush? _darkBackgroundBrush;
    [ObservableProperty] private IBrush? _lightBackgroundBrush;

    partial void OnDarkBackgroundColorChanged(string value)
    {
        try 
        { 
            var brush = Brush.Parse(value);
            DarkBackgroundBrush = brush;
            // Immediate Preview
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new ThemeSettingsChangedMessage(value, LightBackgroundColor));
        } 
        catch { }
    }
    
    partial void OnLightBackgroundColorChanged(string value)
    {
        try 
        { 
            var brush = Brush.Parse(value);
            LightBackgroundBrush = brush;
            // Immediate Preview
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new ThemeSettingsChangedMessage(DarkBackgroundColor, value));
        } 
        catch { }
    }
    
    
    [ObservableProperty]
    private Color _customLightBorderColor = Color.Parse("#E5E7EB");

    [ObservableProperty]
    private Color _customDarkBorderColor = Color.Parse("#333333");

    partial void OnCustomLightBorderColorChanged(Color value)
    {
        if (_themeService == null) return;
        _themeService.SetBorderColor(value.ToString(), false);
    }

    partial void OnCustomDarkBorderColorChanged(Color value)
    {
        if (_themeService == null) return;
        _themeService.SetBorderColor(value.ToString(), true);
    }
    
    [ObservableProperty]
    private Color _customLightCardBgColor;
    
    [ObservableProperty]
    private Color _customDarkCardBgColor;

    partial void OnCustomLightCardBgColorChanged(Color value)
    {
        if (_themeService == null || _isInitializing) return;
        _themeService.SetCardBackgroundColor(value.ToString(), false);
    }
    
    partial void OnCustomDarkCardBgColorChanged(Color value)
    {
        if (_themeService == null || _isInitializing) return;
        _themeService.SetCardBackgroundColor(value.ToString(), true);
    }

    [ObservableProperty] private Color _customDarkBackgroundColor = Color.Parse("#1C1C1C");
    [ObservableProperty] private Color _customLightBackgroundColor = Color.Parse("#F5F5F5");

    partial void OnCustomDarkBackgroundColorChanged(Color value)
    {
        if (_isInitializing) return;
        DarkBackgroundColor = value.ToString(); 
    }

    partial void OnCustomLightBackgroundColorChanged(Color value)
    {
        if (_isInitializing) return;
        LightBackgroundColor = value.ToString();
    }


    [ObservableProperty]
    private ObservableCollection<ColorOption> _accentColors = new();
    
    [ObservableProperty]
    private ColorOption? _selectedAccentColor;
    
    [ObservableProperty]
    private int _selectedLanguageIndex = 0; // 0=English, 1=Indonesian
    
    [ObservableProperty]
    private bool _isFloatingWidgetEnabled = true;

    [ObservableProperty] private double _orbButtonWidth = 160;
    [ObservableProperty] private double _orbButtonHeight = 36;
    
    partial void OnOrbButtonWidthChanged(double value)
    {
        if (_isInitializing) return;
        _database?.SetAsync("Settings.Orb.ButtonWidth", value.ToString());
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbButtonSizeChangedMessage(value, OrbButtonHeight));
    }
    
    partial void OnOrbButtonHeightChanged(double value)
    {
        if (_isInitializing) return;
        _database?.SetAsync("Settings.Orb.ButtonHeight", value.ToString());
         CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbButtonSizeChangedMessage(OrbButtonWidth, value));
    }

    [RelayCommand]
    private void SyncSystemTime()
    {
        try
        {
            // Open Windows Date & Time Settings
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true });
        }
        catch 
        {
            StatusMessage = "Failed to open settings";
            IsStatusVisible = true;
        }
    }

    // ...

    // Integrations (Account)
    [ObservableProperty] private string _trelloApiKey = "";
    [ObservableProperty] private string _trelloToken = "";
    [ObservableProperty] private string _googleCredsPath = "";
    [ObservableProperty] private string _googleSheetId = "";
    [ObservableProperty] private string _sheetName = "";
    [ObservableProperty] private string _sheetColumn = "";
    [ObservableProperty] private string _sheetRow = "";
    
    // Trello Config
    [ObservableProperty] private string _editingBoardId = "";
    [ObservableProperty] private string _editingListId = "";
    [ObservableProperty] private string _revisionBoardId = "";
    [ObservableProperty] private string _revisionListId = "";
    [ObservableProperty] private string _lateBoardId = "";
    [ObservableProperty] private string _lateListId = "";
    [ObservableProperty] private string _qcBoardId = ""; // QC Board for Auto-Move logic
    
    // --- Object-Based Properties for AutoCompleteBox ---
    [ObservableProperty] private TrelloItem? _selectedEditingBoard;
    [ObservableProperty] private TrelloItem? _selectedEditingList;
    [ObservableProperty] private TrelloItem? _selectedRevisionBoard;
    [ObservableProperty] private TrelloItem? _selectedRevisionList;
    [ObservableProperty] private TrelloItem? _selectedLateBoard;
    [ObservableProperty] private TrelloItem? _selectedLateList;
    [ObservableProperty] private TrelloItem? _selectedQcBoard;

    async partial void OnSelectedEditingBoardChanged(TrelloItem? value)
    {
        if (value == null) return;
        EditingBoardId = value.Id;
        await _database.SetAsync("Trello.EditingBoardId", value.Id);
        await FetchListsAsync(value.Id, EditingLists);
    }

    async partial void OnSelectedRevisionBoardChanged(TrelloItem? value)
    {
        if (value == null) return;
        RevisionBoardId = value.Id;
        await _database.SetAsync("Trello.RevisionBoardId", value.Id);
        await FetchListsAsync(value.Id, RevisionLists);
    }

    async partial void OnSelectedLateBoardChanged(TrelloItem? value)
    {
        if (value == null) return;
        LateBoardId = value.Id;
        await _database.SetAsync("Trello.LateBoardId", value.Id);
        await FetchListsAsync(value.Id, LateLists);
    }
    
    partial void OnSelectedQcBoardChanged(TrelloItem? value)
    {
        if (value == null) return;
        QcBoardId = value.Id;
        _database.SetAsync("Trello.QcBoardId", value.Id);
    }

    partial void OnSelectedEditingListChanged(TrelloItem? value)
    {
        if (value != null)
        {
            EditingListId = value.Id;
            _database.SetAsync("Trello.EditingListId", value.Id);
        }
    }

    partial void OnSelectedRevisionListChanged(TrelloItem? value)
    {
        if (value != null)
        {
            RevisionListId = value.Id;
            _database.SetAsync("Trello.RevisionListId", value.Id);
        }
    }

    partial void OnSelectedLateListChanged(TrelloItem? value)
    {
        if (value != null)
        {
            LateListId = value.Id;
            _database.SetAsync("Trello.LateListId", value.Id);
        }
    }
    // ---------------------------------------------------

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isStatusVisible = false;

    // Google Sheet Dropdown Options
    public ObservableCollection<string> SheetColumns { get; } = new();
    public ObservableCollection<string> SheetRows { get; } = new();

    // --- Folder Locker Setup (Temporarily Disabled) ---
    [ObservableProperty] private bool _isLockerConfigured;
    
    [ObservableProperty] private bool _isStartupAnimationEnabled = true;
    
    [ObservableProperty] 
    private int _selectedStatSpeedIndex = 1; // 0=Slow, 1=Normal, 2=Fast
    
    partial void OnSelectedStatSpeedIndexChanged(int value)
    {
         // Persist immediately or just notify?
         // Let's notify Dashboard to update live
         CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new RefreshDashboardMessage());
    }
    
    // --- End Folder Locker Setup ---

    public SettingsViewModel() 
    {
        UserName = "Preview User";
        InitializeAppearanceOptions();
        
        IsLockerConfigured = BMachine.Core.Security.FolderLockerConfig.Load().IsConfigured;

        PathSettingsVM = new PathSettingsViewModel(); // Design-time fallback
    }

    [ObservableProperty] private ObservableCollection<ExtensionViewModel> _extensions = new();
    
    // File Picker delegate to be set by View/Parent
    public Func<Task<string?>>? PickExtensionFileFunc { get; set; }

    public SettingsViewModel(IDatabase database, Action navigateBack, ILanguageService? languageService = null, INotificationService? notificationService = null)
    {
        _database = database;
        _navigateBack = navigateBack;
        _themeService = new ThemeService(database); 
        _languageService = languageService;

        // Initialize Path Settings VM
        PathSettingsVM = new PathSettingsViewModel(database, notificationService!);

        if (_languageService != null)
        {
            SelectedLanguageIndex = _languageService.CurrentLanguage.Name.StartsWith("id") ? 1 : 0;
        }
        
        MockBoards = new ObservableCollection<TrelloItem>();
        EditingLists = new ObservableCollection<TrelloItem>();
        RevisionLists = new ObservableCollection<TrelloItem>();
        LateLists = new ObservableCollection<TrelloItem>();
        
        InitializeAppearanceOptions();
        InitializeWidgetColors();
        InitializeSheetOptions(); // Initialize Google Sheet Options
        
        // _isInitializing = true; // Implicitly true from field init
        
        LoadSettings();
        _ = LoadNavSettings();
        LoadExtensions();
        LoadAllScripts();
        
        // Shortcut
        WeakReferenceMessenger.Default.Register<TriggerRecordedMessage>(this, (r, m) =>
        {
             if (r is SettingsViewModel vm) vm.OnShortcutRecorded(m);
        });
        LoadShortcutConfig();
    }

    private void InitializeSheetOptions()
    {
        // Columns A-Z
        SheetColumns.Clear();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            SheetColumns.Add(c.ToString());
        }
        // Extended Columns AA-AZ (Optional, maybe just A-Z is enough for now as per "dropdown" request which usually implies simple set)
        
        // Rows 1-100
        SheetRows.Clear();
        for (int i = 1; i <= 100; i++)
        {
            SheetRows.Add(i.ToString());
        }
    }
    
    private bool _isInitializing = true;
    
    // --- Widget Colors (Advanced) ---
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsEditingCustom))]
    private ColorOption? _editingColor;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsRevisionCustom))]
    private ColorOption? _revisionColor;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsLateCustom))]
    private ColorOption? _lateColor;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsPointsCustom))]
    private ColorOption? _pointsColor;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsOrbCustom))]
    private ColorOption? _orbColor;
    
    public bool IsEditingCustom => EditingColor?.Name == "Custom";
    public bool IsRevisionCustom => RevisionColor?.Name == "Custom";
    public bool IsLateCustom => LateColor?.Name == "Custom";
    public bool IsPointsCustom => PointsColor?.Name == "Custom";
    public bool IsOrbCustom => OrbColor?.Name == "Custom";
    
    [ObservableProperty] private Color _customOrbColor = Color.Parse("#3b82f6");
    
    // Custom Color Pickers (Visible if "Custom" is selected for that widget)
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(CustomEditingHex))]
    private Color _customEditingColor = Colors.Blue;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(CustomRevisionHex))]
    private Color _customRevisionColor = Colors.Orange;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(CustomLateHex))]
    private Color _customLateColor = Colors.Red;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(CustomPointsHex))]
    private Color _customPointsColor = Colors.Green;

    partial void OnCustomEditingColorChanged(Color value) => ApplyWidgetColors();
    partial void OnCustomRevisionColorChanged(Color value) => ApplyWidgetColors();
    partial void OnCustomLateColorChanged(Color value) => ApplyWidgetColors();
    partial void OnCustomPointsColorChanged(Color value) => ApplyWidgetColors();


    partial void OnEditingColorChanged(ColorOption? value) 
    {
        OnPropertyChanged(nameof(IsEditingCustom));
        ApplyWidgetColors();
    }
    partial void OnRevisionColorChanged(ColorOption? value)
    {
        OnPropertyChanged(nameof(IsRevisionCustom));
        ApplyWidgetColors();
    }
    partial void OnLateColorChanged(ColorOption? value)
    {
        OnPropertyChanged(nameof(IsLateCustom));
        ApplyWidgetColors();
    }
    partial void OnPointsColorChanged(ColorOption? value)
    {
        OnPropertyChanged(nameof(IsPointsCustom));
        ApplyWidgetColors();
    }


    private void ApplyWidgetColors()
    {
        if (_isInitializing || _themeService == null) return;
        
        // Editing
        var editHex = GetColorHex(EditingColor, CustomEditingColor);
        _themeService.SetWidgetColor("Editing", editHex);
        _database?.SetAsync("Settings.Color.Editing", editHex);
        
        // Revision
        var revHex = GetColorHex(RevisionColor, CustomRevisionColor);
        _themeService.SetWidgetColor("Revision", revHex);
        _database?.SetAsync("Settings.Color.Revision", revHex);
        
        // Late
        var lateHex = GetColorHex(LateColor, CustomLateColor);
        _themeService.SetWidgetColor("Late", lateHex);
        _database?.SetAsync("Settings.Color.Late", lateHex);
        
        // Points
        var ptsHex = GetColorHex(PointsColor, CustomPointsColor);
        _themeService.SetWidgetColor("Points", ptsHex);
        _database?.SetAsync("Settings.Color.Points", ptsHex);
        
        // Orb
        var orbHex = GetColorHex(OrbColor, CustomOrbColor);
        _themeService.SetWidgetColor("Orb", orbHex);
        _database?.SetAsync("Settings.Color.Orb", orbHex);
    }
    


    // Legacy Helper overload removal or keep if needed?
    // The existing code calls GetColorHex(EditingColor, CustomEditingColor) where CustomEditingColor WAS string in old code
    // but IS Color in new code.
    // Wait, the "SaveProfile" method calls GetColorHex(EditingColor, CustomEditingHex) -> CustomEditingHex IS string wrapper property.
    // To avoid breaking SaveProfile, I should overload or update SaveProfile.
    // Let's UPDATE the helper signature and fix usage in SaveProfile later or relies on the fact that CustomEditingHex wraps CustomEditingColor?
    // No, I changed _customEditingColor to Color.
    // SaveProfile calls GetColorHex(EditingColor, CustomEditingColor).
    // So the signature above IS correct for the new property type.
    
    public string CustomEditingHex
    {
        get => CustomEditingColor.ToString();
        set { if (Color.TryParse(value, out Color c)) CustomEditingColor = c; }
    }
    public string CustomRevisionHex
    {
        get => CustomRevisionColor.ToString();
        set { if (Color.TryParse(value, out Color c)) CustomRevisionColor = c; }
    }
    public string CustomLateHex
    {
        get => CustomLateColor.ToString();
        set { if (Color.TryParse(value, out Color c)) CustomLateColor = c; }
    }
    public string CustomPointsHex
    {
        get => CustomPointsColor.ToString();
        set { if (Color.TryParse(value, out Color c)) CustomPointsColor = c; }
    }
    public string CustomOrbHex
    {
        get => CustomOrbColor.ToString();
        set { if (Color.TryParse(value, out Color c)) CustomOrbColor = c; }
    }
    
    public ObservableCollection<ColorOption> WidgetColorOptions { get; private set; } = new();

    [ObservableProperty]
    private int _orbBreathingSpeedIndex = 1; // 0=Slow, 1=Normal, 2=Fast

    [ObservableProperty]
    private bool _isOrbBreathingEnabled = true;

    partial void OnIsOrbBreathingEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _database?.SetAsync("Settings.Orb.Breathing", value.ToString());
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbBreathingToggledMessage(value));
    }

    partial void OnOrbBreathingSpeedIndexChanged(int value)
    {
        if (_isInitializing) return;
        _database?.SetAsync("Settings.Orb.Speed", value.ToString());
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbSpeedChangedMessage(value));
    }

    partial void OnOrbColorChanged(ColorOption? value)
    {
        if (_isInitializing) return;
        if (value == null) return;
        var hex = GetColorHex(value, CustomOrbColor);
        _database?.SetAsync("Settings.Color.Orb", hex);
        
        if (Color.TryParse(hex, out var color))
        {
             CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbColorChangedMessage(new SolidColorBrush(color)));
        }
    }

    partial void OnCustomOrbColorChanged(Color value)
    {
        if (_isInitializing) return;
        if (IsOrbCustom) 
        {
             var hex = value.ToString();
             _database?.SetAsync("Settings.Color.Orb", hex);
             CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.OrbColorChangedMessage(new SolidColorBrush(value)));
        }
    }

    private void InitializeWidgetColors()
    {
        WidgetColorOptions = new ObservableCollection<ColorOption>(AccentColors);
        // Add "Custom" option
        WidgetColorOptions.Add(new ColorOption { Name = "Custom", Hex = "CUSTOM", Brush = new SolidColorBrush(Colors.White) });
        
        // Default Selections
        EditingColor = WidgetColorOptions[0]; // Blue
        RevisionColor = WidgetColorOptions[1]; // Orange
        LateColor = WidgetColorOptions[1]; // Orange
        PointsColor = WidgetColorOptions[1]; // Orange
        OrbColor = WidgetColorOptions[0]; // Blue
    }

    private void InitializeAppearanceOptions()
    {
        AccentColors = new ObservableCollection<ColorOption>
        {
            new() { Name = "Blue", Hex = "#3b82f6", Brush = SolidColorBrush.Parse("#3b82f6") },
            new() { Name = "Orange", Hex = "#f97316", Brush = SolidColorBrush.Parse("#f97316") },
            new() { Name = "Green", Hex = "#10b981", Brush = SolidColorBrush.Parse("#10b981") },
            new() { Name = "Purple", Hex = "#8b5cf6", Brush = SolidColorBrush.Parse("#8b5cf6") },
            new() { Name = "Red", Hex = "#ef4444", Brush = SolidColorBrush.Parse("#ef4444") },
            new() { Name = "Random/Campur", Hex = "#FFFFFF", Brush = new SolidColorBrush(Colors.Gray) } 
        };
        SelectedAccentColor = AccentColors[0];
    }

    private async void LoadSettings()
    {
        _isInitializing = true;
        try 
        {
            if (_database == null) return;
            var name = await _database.GetAsync<string>("UserProfile.Name");
            UserName = name ?? "ABENG";
            
            // Load Animation Setting
            var animStr = await _database.GetAsync<string>("Settings.StartupAnim");
            IsStartupAnimationEnabled = string.IsNullOrEmpty(animStr) || bool.Parse(animStr); // Default True
            
            // Load Stat Speed
            var speedStr = await _database.GetAsync<string>("Settings.StatSpeed");
            if (int.TryParse(speedStr, out int speedIdx)) SelectedStatSpeedIndex = speedIdx;
            
            // Load Theme
            var themeStr = await _database.GetAsync<string>("Settings.Theme");
            if (themeStr == "Light") SelectedThemeIndex = 1;
            else SelectedThemeIndex = 0;
            IsDarkMode = SelectedThemeIndex == 0;
            
            // Load Background Colors
            var darkBg = await _database.GetAsync<string>("Appearance.Background.Dark");
            if (!string.IsNullOrEmpty(darkBg)) 
            {
                DarkBackgroundColor = darkBg;
                if(Color.TryParse(darkBg, out var c)) CustomDarkBackgroundColor = c;
            }
            else DarkBackgroundColor = "#1C1C1C"; // Trigger default brush
            
            var lightBg = await _database.GetAsync<string>("Appearance.Background.Light");
            if (!string.IsNullOrEmpty(lightBg)) 
            {
                LightBackgroundColor = lightBg;
                if(Color.TryParse(lightBg, out var c)) CustomLightBackgroundColor = c;
            }
            else LightBackgroundColor = "#F5F5F5"; // Trigger default brush
            
            // Ensure brushes are set if they werent triggered by change (e.g. initial load might not trigger if value same)
            // Actually ObservableProperty logic triggers if value changes. If default is same, it might not.
            if (DarkBackgroundBrush == null) OnDarkBackgroundColorChanged(DarkBackgroundColor);
            if (LightBackgroundBrush == null) OnLightBackgroundColorChanged(LightBackgroundColor);
            
            // Broadcast initial theme settings
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new ThemeSettingsChangedMessage(DarkBackgroundColor, LightBackgroundColor));
            
            // Load Border Colors
            var borderLight = await _database.GetAsync<string>("Settings.BorderLight");
            if (!string.IsNullOrEmpty(borderLight) && Color.TryParse(borderLight, out var bl)) 
                CustomLightBorderColor = bl;
                
            var borderDark = await _database.GetAsync<string>("Settings.BorderDark");
            if (!string.IsNullOrEmpty(borderDark) && Color.TryParse(borderDark, out var bd)) 
                CustomDarkBorderColor = bd;
            
            // Load Card Background Colors
            var cardBgLight = await _database.GetAsync<string>("Settings.CardBgLight");
            if (!string.IsNullOrEmpty(cardBgLight) && Color.TryParse(cardBgLight, out var cbl))
                CustomLightCardBgColor = cbl;
            else
                CustomLightCardBgColor = Color.Parse("#FFFFFF"); // Default

            var cardBgDark = await _database.GetAsync<string>("Settings.CardBgDark");
            if (!string.IsNullOrEmpty(cardBgDark) && Color.TryParse(cardBgDark, out var cbd))
                CustomDarkCardBgColor = cbd;
            else
                CustomDarkCardBgColor = Color.Parse("#1A1C20"); // Default

            // Load Accent
            // Load Accent
            var accentHex = await _database.GetAsync<string>("Settings.Accent");
            if (!string.IsNullOrEmpty(accentHex))
            {
                var existing = AccentColors.FirstOrDefault(c => c.Hex == accentHex);
                if (existing != null) 
                {
                    SelectedAccentColor = existing;
                }
            }
            
            // Load Floating Widget
            var fwEnabled = await _database.GetAsync<string>("Settings.FloatingWidget") ?? "True";
            IsFloatingWidgetEnabled = bool.Parse(fwEnabled);

             var orbBtnW = await _database.GetAsync<string>("Settings.Orb.ButtonWidth");
            if (double.TryParse(orbBtnW, out double obw)) OrbButtonWidth = obw;
            
            var orbBtnH = await _database.GetAsync<string>("Settings.Orb.ButtonHeight");
            if (double.TryParse(orbBtnH, out double obh)) OrbButtonHeight = obh;
            
            // Allow time for ViewModel to bind, then broadcast initial state
            // (Actually FloatingWidgetViewModel should read form DB too, but message sync prevents drift)
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new FloatingWidgetMessage(IsFloatingWidgetEnabled));

            // --- Load Widget Colors ---
            await LoadWidgetColorAsync("Settings.Color.Editing", c => EditingColor = c, hex => CustomEditingColor = Color.Parse(hex));
            await LoadWidgetColorAsync("Settings.Color.Revision", c => RevisionColor = c, hex => CustomRevisionColor = Color.Parse(hex));
            await LoadWidgetColorAsync("Settings.Color.Late", c => LateColor = c, hex => CustomLateColor = Color.Parse(hex));
            await LoadWidgetColorAsync("Settings.Color.Points", c => PointsColor = c, hex => CustomPointsColor = Color.Parse(hex));
            await LoadWidgetColorAsync("Settings.Color.Orb", c => OrbColor = c, hex => CustomOrbColor = Color.Parse(hex));
            
            // Load Orb Speed
            var orbSpeedStr = await _database.GetAsync<string>("Settings.Orb.Speed");
            if (int.TryParse(orbSpeedStr, out int orbSpeedIdx)) OrbBreathingSpeedIndex = orbSpeedIdx;
            else OrbBreathingSpeedIndex = 1; // Default Normal
            
            var orbBreathStr = await _database.GetAsync<string>("Settings.Orb.Breathing");
            IsOrbBreathingEnabled = string.IsNullOrEmpty(orbBreathStr) || bool.Parse(orbBreathStr);
            // --------------------------

            // Load Integrations
            TrelloApiKey = await _database.GetAsync<string>("Trello.ApiKey") ?? "";
            TrelloToken = await _database.GetAsync<string>("Trello.Token") ?? "";
            
            var trelloConnStr = await _database.GetAsync<string>("Trello.IsConnected");
            IsTrelloConnected = trelloConnStr == "True";

            GoogleCredsPath = await _database.GetAsync<string>("Google.CredsPath") ?? "";
            GoogleSheetId = await _database.GetAsync<string>("Google.SheetId") ?? "";
            SheetName = await _database.GetAsync<string>("Google.SheetName") ?? "";
            SheetColumn = await _database.GetAsync<string>("Google.SheetColumn") ?? "";
            SheetRow = await _database.GetAsync<string>("Google.SheetRow") ?? "";
            
            if (IsTrelloConnected && !string.IsNullOrEmpty(TrelloApiKey) && !string.IsNullOrEmpty(TrelloToken))
            {
                 // Static Cache Logic
                 if (_staticBoardCache != null && _staticBoardCache.Count > 0)
                 {
                     MockBoards = new ObservableCollection<TrelloItem>(_staticBoardCache);
                     // Console.WriteLine("Loaded Boards from Cache!");
                 }
                 else
                 {
                     // Restore async fetch to ensure MockBoards are populated 
                     // This prevents EditingBoardId binding from wiping the setting if the list is empty
                     // We await this now to ensure list is populated BEFORE final init check if possible,
                     // OR at least ensure we don't wipe stuff.
                     // Since LoadSettings is async void, awaiting here is fine.
                     try 
                     {
                        await RefreshTrelloData(true);
                     }
                     catch { /* ignore connection errors */ }
                 }
            }

            EditingBoardId = await _database.GetAsync<string>("Trello.EditingBoardId") ?? "";
            RevisionBoardId = await _database.GetAsync<string>("Trello.RevisionBoardId") ?? "";
            LateBoardId = await _database.GetAsync<string>("Trello.LateBoardId") ?? "";
            QcBoardId = await _database.GetAsync<string>("Trello.QcBoardId") ?? ""; 
            
            if (IsTrelloConnected)
            {
                if (!string.IsNullOrEmpty(EditingBoardId)) await FetchListsAsync(EditingBoardId, EditingLists);
                if (!string.IsNullOrEmpty(RevisionBoardId)) await FetchListsAsync(RevisionBoardId, RevisionLists);
                if (!string.IsNullOrEmpty(LateBoardId)) await FetchListsAsync(LateBoardId, LateLists);
            }


            EditingListId = await _database.GetAsync<string>("Trello.EditingListId") ?? "";
            RevisionListId = await _database.GetAsync<string>("Trello.RevisionListId") ?? "";
            LateListId = await _database.GetAsync<string>("Trello.LateListId") ?? "";

            // Sync Objects from IDs
            if (IsTrelloConnected)
            {
                SelectedEditingBoard = MockBoards.FirstOrDefault(b => b.Id == EditingBoardId);
                SelectedRevisionBoard = MockBoards.FirstOrDefault(b => b.Id == RevisionBoardId);
                SelectedLateBoard = MockBoards.FirstOrDefault(b => b.Id == LateBoardId);
                SelectedQcBoard = MockBoards.FirstOrDefault(b => b.Id == QcBoardId);

                SelectedEditingList = EditingLists.FirstOrDefault(l => l.Id == EditingListId);
                SelectedRevisionList = RevisionLists.FirstOrDefault(l => l.Id == RevisionListId);
                SelectedLateList = LateLists.FirstOrDefault(l => l.Id == LateListId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }
        finally
        {
            _isInitializing = false;
            ApplyWidgetColors();
        }
    }

    private async Task LoadWidgetColorAsync(string key, Action<ColorOption> setOption, Action<string> setCustomHex)
    {
        if (_database == null) return;
        try
        {
            var hex = await _database.GetAsync<string>(key);
            if (string.IsNullOrEmpty(hex)) return;
    
            // Check if it matches a preset
            var preset = WidgetColorOptions.FirstOrDefault(x => x.Hex == hex && x.Name != "Custom");
            if (preset != null)
            {
                setOption(preset);
            }
            else
            {
                // Must be custom
                setOption(WidgetColorOptions.First(x => x.Name == "Custom"));
                try { setCustomHex(hex); } catch {}
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading widget color {key}: {ex.Message}");
        }
    }
    
    private string GetColorHex(ColorOption? option, Color custom)
    {
        if (option == null) return "#3b82f6"; // Default Blue
        if (option.Name == "Custom") return custom.ToString();
        return option.Hex;
    }
    
    // ------------------------------------
    
    // --- Extension Management Logic ---
    
    private void LoadExtensions()
    {
        Extensions.Clear();
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);

        // Load active dlls
        var dlls = Directory.GetFiles(pluginsDir, "*.dll");
        foreach(var dll in dlls)
        {
             Extensions.Add(CreateExtensionViewModel(dll, true));
        }
        
        // Load disabled (e.g. .disabled)
        var disabled = Directory.GetFiles(pluginsDir, "*.disabled");
        foreach(var file in disabled)
        {
             Extensions.Add(CreateExtensionViewModel(file, false));
        }
    }
    
    private ExtensionViewModel CreateExtensionViewModel(string path, bool isEnabled)
    {
        var fileName = Path.GetFileName(path);
        // For disabled files, remove the .disabled extension for display if needed, 
        // but here we just want the base DLL name usually.
        // If file is "MyPlugin.dll.disabled", Name -> "MyPlugin.dll"
        var displayName = isEnabled ? fileName : Path.GetFileNameWithoutExtension(fileName);
        
        // Mocking LoadedPlugin wrapping since we don't have the real context fully set up here
        // We will create a dummy LoadedPlugin-like structure or just use ExtensionViewModel properly.
        // Wait, ExtensionViewModel expects LoadedPlugin. I need to modify ExtensionViewModel constructor OR
        // create a dummy LoadedPlugin.
        
        // Let's instantiate a dummy Plugin for the view model 
        var dummyPlugin = new BMachine.Core.PluginSystem.LoadedPlugin 
        { 
            Plugin = new MockPlugin { Id = displayName, Name = displayName, Version = "1.0.0" },
            Assembly = System.Reflection.Assembly.GetExecutingAssembly(), // Dummy
            LoadedAt = DateTime.Now
        };

        var vm = new ExtensionViewModel(dummyPlugin, OnToggleExtension, OnDeleteExtension);
        vm.IsEnabled = isEnabled;
        // Hack: Store the real path somewhere? 
        // We use Id as the filename for now in this simple implementation.
        return vm;
    }
    
    private class MockPlugin : BMachine.SDK.IPlugin
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = ""; // Missing
        public string Icon { get; set; } = ""; // Missing
        
        public void Initialize(BMachine.SDK.IPluginContext context) {}
        public void Shutdown() {}
        public IEnumerable<BMachine.SDK.IWidget> GetDashboardWidgets() => Enumerable.Empty<BMachine.SDK.IWidget>();
        public IEnumerable<BMachine.SDK.IMenuEntry> GetMenuEntries() => Enumerable.Empty<BMachine.SDK.IMenuEntry>();
    }

    [RelayCommand]
    private async Task AddExtension()
    {
        if (PickExtensionFileFunc == null) return;
        
        var sourceFile = await PickExtensionFileFunc();
        if (string.IsNullOrEmpty(sourceFile)) return;
        
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
        
        var fileName = Path.GetFileName(sourceFile);
        var destPath = Path.Combine(pluginsDir, fileName);
        
        try
        {
            File.Copy(sourceFile, destPath, true);
            LoadExtensions(); // Refresh
            StatusMessage = "Extension Added!";
            IsStatusVisible = true;
            await Task.Delay(2000);
            IsStatusVisible = false;
        }
        catch(Exception ex)
        {
            StatusMessage = "Error adding extension";
            Console.WriteLine(ex.Message);
        }
    }
    
    private void OnToggleExtension(ExtensionViewModel vm)
    {
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        var fileName = vm.Name; // Assuming Name holds filenames relative to Plugins dir
        
        if (vm.IsEnabled)
        {
            // Disable it: Rename .dll -> .disabled
            var currentPath = Path.Combine(pluginsDir, fileName);
            var newPath = currentPath + ".disabled";
            if (File.Exists(currentPath))
            {
                try {
                    File.Move(currentPath, newPath, true);
                    vm.IsEnabled = false;
                } catch {}
            }
        }
        else
        {
             // Enable: Rename .disabled -> .dll
             // Current Name is "MyPlugin.dll", but file on disk is "MyPlugin.dll.disabled"
             var currentPath = Path.Combine(pluginsDir, fileName + ".disabled");
             var newPath = Path.Combine(pluginsDir, fileName);
             if (File.Exists(currentPath))
             {
                 try {
                     File.Move(currentPath, newPath, true);
                     vm.IsEnabled = true;
                 } catch {}
             }
        }
        
        // Reload to refresh list state properly
        LoadExtensions();
    }
    
    private void OnDeleteExtension(ExtensionViewModel vm)
    {
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        var fileName = vm.Name; 
        
        // Check both .dll and .disabled
        var pathDll = Path.Combine(pluginsDir, fileName);
        var pathDisabled = Path.Combine(pluginsDir, fileName + ".disabled");
        
        try
        {
            if (File.Exists(pathDll)) File.Delete(pathDll);
            if (File.Exists(pathDisabled)) File.Delete(pathDisabled);
            
            LoadExtensions();
        }
        catch {}
    }

    private bool _isRefreshingTrello;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrelloButtonText))]
    private bool _isTrelloConnected;

    public string TrelloButtonText => IsTrelloConnected ? "Disconnect" : "Connect";

    [RelayCommand]
    private async Task ToggleTrelloConnection()
    {
        if (IsTrelloConnected)
        {
            // Disconnect
            IsTrelloConnected = false;
            
            // Reset Selections
            EditingBoardId = ""; EditingListId = "";
            RevisionBoardId = ""; RevisionListId = "";
            LateBoardId = ""; LateListId = "";
            QcBoardId = "";

            // Clear Lists
            EditingLists.Clear();
            RevisionLists.Clear();
            LateLists.Clear();

            StatusMessage = "Disconnected";
            IsStatusVisible = true;
            
            // Save state immediately
             if (_database != null)
             {
                 await _database.SetAsync("Trello.IsConnected", "False");
                 await _database.SetAsync("Trello.EditingBoardId", "");
                 await _database.SetAsync("Trello.EditingListId", "");
                 await _database.SetAsync("Trello.RevisionBoardId", "");
                 await _database.SetAsync("Trello.RevisionListId", "");
                 await _database.SetAsync("Trello.LateBoardId", "");
                 await _database.SetAsync("Trello.LateListId", "");
                 await _database.SetAsync("Trello.QcBoardId", "");
             }
        }
        else
        {
            // Connect
            if (string.IsNullOrEmpty(TrelloApiKey) || string.IsNullOrEmpty(TrelloToken))
            {
                StatusMessage = "Enter API Key & Token!";
                IsStatusVisible = true;
                await Task.Delay(2000);
                IsStatusVisible = false;
                return;
            }

            StatusMessage = "Connecting...";
            IsStatusVisible = true;
            
            IsTrelloConnected = true;
            if (_database != null) await _database.SetAsync("Trello.IsConnected", "True");
            
            await RefreshTrelloData();
            StatusMessage = "Connected!";
        }
        
        await Task.Delay(2000);
        IsStatusVisible = false;
    }

    [RelayCommand]
    private async Task RefreshTrelloData(bool silent = false)
    {
        if (string.IsNullOrEmpty(TrelloApiKey) || string.IsNullOrEmpty(TrelloToken))
        {
            if (!silent)
            {
                StatusMessage = "Isi API Key & Token dulu!";
                IsStatusVisible = true;
                await Task.Delay(2000);
                IsStatusVisible = false;
            }
            return;
        }

        if (!silent)
        {
            StatusMessage = "Connecting to Trello...";
            IsStatusVisible = true;
        }
        
        _isRefreshingTrello = true;

        try
        {
            using var client = new System.Net.Http.HttpClient();
            var boardsUrl = $"https://api.trello.com/1/members/me/boards?key={TrelloApiKey}&token={TrelloToken}&fields=name,id";
            var response = await client.GetStringAsync(boardsUrl);
            
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var tempList = new ObservableCollection<TrelloItem>();
                
                // Keep the items in the existing list if possible? 
                // No, better to just create fresh. 
                // However, we want to construct the list fully before assigning to property 
                // so the View sees the swap atomically.
                
                var boardList = new List<TrelloItem>();
                foreach (var element in root.EnumerateArray())
                {
                    boardList.Add(new TrelloItem 
                    { 
                        Id = element.GetProperty("id").GetString() ?? "", 
                        Name = element.GetProperty("name").GetString() ?? "" 
                    });
                }
                
                // Update Cache
                _staticBoardCache = new List<TrelloItem>(boardList);
                
                // Atomic Update to View
                // Must be on UI thread if bound
                Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                {
                    MockBoards = new ObservableCollection<TrelloItem>(boardList);
                });
            }
            
            // Re-fetch lists if boards are selected
            if (!silent) StatusMessage = "Boards Loaded!";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching Trello boards: {ex.Message}");
            if (!silent) StatusMessage = "Koneksi Gagal!";
        }
        finally
        {
            _isRefreshingTrello = false;
        }
        
        if (!silent)
        {
            await Task.Delay(2000);
            IsStatusVisible = false;
        }
    }

    partial void OnEditingBoardIdChanged(string value) 
    {
        if (_isRefreshingTrello) return;
        _ = FetchListsAsync(value, EditingLists);
        if (!_isInitializing) _database?.SetAsync("Trello.EditingBoardId", value);
    } 
    partial void OnRevisionBoardIdChanged(string value) 
    {
        if (_isRefreshingTrello) return;
        _ = FetchListsAsync(value, RevisionLists);
        if (!_isInitializing) _database?.SetAsync("Trello.RevisionBoardId", value);
    }
    partial void OnLateBoardIdChanged(string value) 
    {
        if (_isRefreshingTrello) return;
        _ = FetchListsAsync(value, LateLists);
        if (!_isInitializing) _database?.SetAsync("Trello.LateBoardId", value);
    }
    partial void OnQcBoardIdChanged(string value) 
    {
        if (_isRefreshingTrello || _isInitializing) return;
        _database?.SetAsync("Trello.QcBoardId", value);
    }

    partial void OnEditingListIdChanged(string value)
    {
        if (_isRefreshingTrello || _isInitializing) return;
        _database?.SetAsync("Trello.EditingListId", value);
    }
    partial void OnRevisionListIdChanged(string value)
    {
        if (_isRefreshingTrello || _isInitializing) return;
        _database?.SetAsync("Trello.RevisionListId", value);
    }
    partial void OnLateListIdChanged(string value)
    {
        if (_isRefreshingTrello || _isInitializing) return;
        _database?.SetAsync("Trello.LateListId", value);
    }

    private async Task FetchListsAsync(string boardId, ObservableCollection<TrelloItem> targetCollection)
    {
        if (string.IsNullOrEmpty(boardId) || string.IsNullOrEmpty(TrelloApiKey) || string.IsNullOrEmpty(TrelloToken)) return;

        try
        {
            using var client = new System.Net.Http.HttpClient();
            var listsUrl = $"https://api.trello.com/1/boards/{boardId}/lists?key={TrelloApiKey}&token={TrelloToken}&fields=name,id";
            var response = await client.GetStringAsync(listsUrl);

            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
            {
                targetCollection.Clear();
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        targetCollection.Add(new TrelloItem 
                        { 
                            Id = element.GetProperty("id").GetString() ?? "", 
                            Name = element.GetProperty("name").GetString() ?? "" 
                        });
                    }
                }
            });
        }
        catch (Exception ex) 
        {
            Console.WriteLine($"Error fetching lists for board {boardId}: {ex.Message}");
        }
    }
    
    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_languageService == null) return;
        var code = value == 1 ? "id-ID" : "en-US";
        _ = SetLanguageAndRefresh(code);
    }
    
    private async Task SetLanguageAndRefresh(string code)
    {
        if (_languageService == null) return;
        await _languageService.SetLanguageAsync(code);
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(AccentColors)); 
        });
    }



    partial void OnSelectedAccentColorChanged(ColorOption? value)
    {
        if (value == null || _themeService == null) return;
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            if (value.Name == "Random/Campur")
            {
                var rnd = new Random();
                var randomColor = AccentColors[rnd.Next(0, AccentColors.Count - 1)]; 
                if (randomColor.Name == "Random/Campur") randomColor = AccentColors[0];
                
                _themeService.SetAccentColor(randomColor.Hex);
            }
            else
            {
                _themeService.SetAccentColor(value.Hex);
            }
        });
    }

    partial void OnIsFloatingWidgetEnabledChanged(bool value)
    {
         if (_database == null) return;
         _ = _database.SetAsync("Settings.FloatingWidget", value.ToString());
         CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new FloatingWidgetMessage(value));
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (_database == null) return;
        StatusMessage = "Saving...";
        IsStatusVisible = true;
        
        try 
        {
            await _database.SetAsync("User.Name", UserName);
            if (!string.IsNullOrEmpty(AvatarSource))
            {
                await _database.SetAsync("User.Avatar", AvatarSource);
            }
            await _database.SetAsync("Settings.StartupAnim", IsStartupAnimationEnabled.ToString());
            await _database.SetAsync("Settings.StatSpeed", SelectedStatSpeedIndex.ToString());
            
            await _database.SetAsync("Trello.ApiKey", TrelloApiKey);
            await _database.SetAsync("Trello.Token", TrelloToken);
            await _database.SetAsync("Trello.IsConnected", IsTrelloConnected.ToString());
            await _database.SetAsync("Google.CredsPath", GoogleCredsPath);
            await _database.SetAsync("Google.SheetId", GoogleSheetId);
            await _database.SetAsync("Google.SheetName", SheetName);
            await _database.SetAsync("Google.SheetColumn", SheetColumn);
            await _database.SetAsync("Google.SheetRow", SheetRow);
            
            await _database.SetAsync("Trello.EditingBoardId", EditingBoardId);
            await _database.SetAsync("Trello.EditingListId", EditingListId);
            await _database.SetAsync("Trello.RevisionBoardId", RevisionBoardId);
            await _database.SetAsync("Trello.RevisionListId", RevisionListId);
            await _database.SetAsync("Trello.LateBoardId", LateBoardId);
            await _database.SetAsync("Trello.LateListId", LateListId);
            await _database.SetAsync("Trello.QcBoardId", QcBoardId);
            
            await _database.SetAsync("Settings.Theme", SelectedThemeIndex == 1 ? "Light" : "Dark");
            if (SelectedAccentColor != null)
                await _database.SetAsync("Settings.Accent", SelectedAccentColor.Hex);
            
            // Save Background Colors
            await _database.SetAsync("Appearance.Background.Dark", DarkBackgroundColor);
            await _database.SetAsync("Appearance.Background.Light", LightBackgroundColor);
            
            // Notify Theme Change
             CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new ThemeSettingsChangedMessage(DarkBackgroundColor, LightBackgroundColor));
             
             // Notify Profile Change
             CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(UserName, AvatarSource));
            
            // --- Save Widget Colors ---
            await _database.SetAsync("Settings.Color.Editing", GetColorHex(EditingColor, CustomEditingColor));
            await _database.SetAsync("Settings.Color.Revision", GetColorHex(RevisionColor, CustomRevisionColor));
            await _database.SetAsync("Settings.Color.Late", GetColorHex(LateColor, CustomLateColor));
            await _database.SetAsync("Settings.Color.Points", GetColorHex(PointsColor, CustomPointsColor));
            await _database.SetAsync("Settings.Color.Orb", GetColorHex(OrbColor, CustomOrbColor));
            await _database.SetAsync("Settings.Orb.Speed", OrbBreathingSpeedIndex.ToString());
            await _database.SetAsync("Settings.Orb.Breathing", IsOrbBreathingEnabled.ToString());
            
            // Notify Dashboard (We'll use a specific message)
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new RefreshDashboardMessage());
            // --------------------------
            
            StatusMessage = "Settings Saved!";
        }
        catch (Exception ex)
        {
             StatusMessage = "Error Saving!";
             Console.WriteLine($"Save Error: {ex.Message}");
        }

        await Task.Delay(2000);
        IsStatusVisible = false;
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigateBack?.Invoke();
    }

    // --- Script Manager Logic ---
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScriptsSelected))]
    private int _selectedScriptTabIndex = 0; // 0=Master, 1=Action, 2=Others

    // Duplicate removed
    


    [ObservableProperty] private ObservableCollection<ScriptItem> _masterScripts = new();
    [ObservableProperty] private ObservableCollection<ScriptItem> _actionScripts = new();
    [ObservableProperty] private ObservableCollection<ScriptItem> _otherScripts = new();
    
    // File Picker for Scripts
    public Func<Task<string?>>? PickScriptFileFunc { get; set; }

    // Metadata for Aliases


    private Dictionary<string, ScriptConfig> _scriptAliases = new();
    private string _metadataPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");

    private void LoadMetadata()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);
                
                // Try Parse New Format
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    // Fallback: Migration from Old Format (Dictionary<string, string>)
                    var oldAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    _scriptAliases = new Dictionary<string, ScriptConfig>();
                    if (oldAliases != null)
                    {
                        foreach(var kvp in oldAliases)
                        {
                            _scriptAliases[kvp.Key] = new ScriptConfig 
                            { 
                                Name = kvp.Value, 
                                Code = kvp.Value.Length <= 4 ? kvp.Value.ToUpper() : kvp.Value.Substring(0, Math.Min(3, kvp.Value.Length)).ToUpper()
                            };
                        }
                        SaveMetadata(); // Save immmediately in new format
                    }
                }
            }
        }
        catch { _scriptAliases = new(); }
    }

    private void SaveMetadata()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_scriptAliases, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_metadataPath, json);
            
            // Notify Floating Widget to Refresh (Use Messenger or just rely on file)
            WeakReferenceMessenger.Default.Send(new RefreshScriptsMessage());
            WeakReferenceMessenger.Default.Send(new ScriptOrderChangedMessage());
        }
        catch { }
    }

    public void LoadAllScripts()
    {
        LoadMetadata();
        LoadScriptsFor(MasterScripts, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Master"), "*.py;*.pyw");
        
        // Action Scripts: Load .jsx and .pyw from "Scripts/Action"
        LoadScriptsFor(ActionScripts, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Action"), "*.jsx;*.pyw");
        
        // Action Scripts: ALSO Load .pyw from "Scripts/" (Root) as RadialMenu supports this
        // We use a helper to Append instead of Clear for this second call
        LoadScriptsFor(ActionScripts, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"), "*.pyw", append: true);

        LoadScriptsFor(OtherScripts, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Others"), "*.*");
        
        // Add Pixelcut Extension as internal entry
        var pixelcutConfig = _scriptAliases.ContainsKey("INTERNAL_PIXELCUT_EXTENSION") 
            ? _scriptAliases["INTERNAL_PIXELCUT_EXTENSION"] 
            : new ScriptConfig { Name = "Pixelcut Extension", Code = "PXL" };
            
        var pixelcutItem = new ScriptItem
        {
            Name = pixelcutConfig.Name,
            ShortCode = pixelcutConfig.Code,
            OriginalName = "INTERNAL_PIXELCUT_EXTENSION",
            FullPath = "INTERNAL_PIXELCUT_EXTENSION",
            Type = "EXT"
        };
        pixelcutItem.OnSaveRequested += (newName, newCode, newIcon) =>
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                _scriptAliases["INTERNAL_PIXELCUT_EXTENSION"] = new ScriptConfig { Name = newName, Code = newCode, IconKey = newIcon };
                pixelcutItem.Name = newName;
                pixelcutItem.ShortCode = newCode;
                pixelcutItem.IconKey = newIcon;
                SaveMetadata();
            }
        };
        OtherScripts.Insert(0, pixelcutItem); // Add at top (Pixelcut)

        // Add GDrive Extension as internal entry
        var gdriveConfig = _scriptAliases.ContainsKey("INTERNAL_GDRIVE_EXTENSION") 
            ? _scriptAliases["INTERNAL_GDRIVE_EXTENSION"] 
            : new ScriptConfig { Name = "GDrive Uploader", Code = "GDR" };
            
        var gdriveItem = new ScriptItem
        {
            Name = gdriveConfig.Name,
            ShortCode = gdriveConfig.Code,
            OriginalName = "INTERNAL_GDRIVE_EXTENSION",
            FullPath = "INTERNAL_GDRIVE_EXTENSION",
            Type = "EXT"
        };
        gdriveItem.OnSaveRequested += (newName, newCode, newIcon) =>
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                _scriptAliases["INTERNAL_GDRIVE_EXTENSION"] = new ScriptConfig { Name = newName, Code = newCode, IconKey = newIcon };
                gdriveItem.Name = newName;
                gdriveItem.ShortCode = newCode;
                gdriveItem.IconKey = newIcon;
                SaveMetadata();
            }
        };
        OtherScripts.Insert(1, gdriveItem); // Add after Pixelcut
    }



    private void LoadScriptsFor(ObservableCollection<ScriptItem> collection, string path, string pattern, bool append = false)
    {
        if (!append) collection.Clear();
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        
        var tempList = new List<(ScriptItem Item, int Order)>();
        
        var patterns = pattern.Split(';');
        foreach(var p in patterns)
        {
            var files = Directory.GetFiles(path, p);
            foreach (var f in files)
            {
                var filename = Path.GetFileName(f);
                var config = _scriptAliases.ContainsKey(filename) 
                    ? _scriptAliases[filename] 
                    : new ScriptConfig { Name = Path.GetFileNameWithoutExtension(filename), Code = "", Order = 9999 }; // Default to end
                
                var item = new ScriptItem 
                { 
                    Name = config.Name,
                    ShortCode = config.Code,
                    IconKey = config.IconKey, // Load Icon
                    OriginalName = filename,
                    FullPath = f,
                    Type = Path.GetExtension(f).TrimStart('.').ToUpper(),
                    PickIconFunc = PickIconAsync // Assign Picker
                };
                
                // Attach Save Handler
                item.OnSaveRequested += (newName, newCode, newIcon) => 
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        var currentOrder = _scriptAliases.ContainsKey(item.OriginalName) ? _scriptAliases[item.OriginalName].Order : 0;
                        var newConfig = new ScriptConfig { 
                            Name = newName, 
                            Code = newCode, 
                            IconKey = newIcon,
                            Order = currentOrder 
                        };
                        _scriptAliases[item.OriginalName] = newConfig;
                        
                        item.Name = newName;
                        item.ShortCode = newCode;
                        item.IconKey = newIcon;
                        SaveMetadata();
                    }
                };
                
                tempList.Add((item, config.Order));
            }
        }

        // Sort by Order, then Name
        var sortedList = tempList.OrderBy(x => x.Order).ThenBy(x => x.Item.Name).ToList();

        foreach(var item in sortedList)
        {
            collection.Add(item.Item);
        }
    }

    private async Task<string?> PickIconAsync()
    {
        var topLevel = TopLevel.GetTopLevel(
             Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
             ? desktop.MainWindow : null);
             
        if (topLevel == null) return null;

        var picker = new BMachine.UI.Views.IconPickerWindow();
        var result = await picker.ShowDialog<string?>(topLevel as Window);
        
        return result;
    }

    public void MoveScript(ScriptItem item, int newIndex, bool isMaster)
    {
        var collection = isMaster ? MasterScripts : ActionScripts;
        var oldIndex = collection.IndexOf(item);
        
        if (oldIndex < 0 || oldIndex == newIndex) return;

        collection.Move(oldIndex, newIndex);

        // Update Orders for ALL items in this collection
        for (int i = 0; i < collection.Count; i++)
        {
            var script = collection[i];
            
            // Get existing or create new config
            var config = _scriptAliases.ContainsKey(script.OriginalName) ? _scriptAliases[script.OriginalName] : new ScriptConfig();
            config.Name = script.Name; // Ensure sync
            config.Code = script.ShortCode; // Ensure sync
            config.IconKey = script.IconKey; // Ensure sync
            config.Order = i; // 0-based index is the new order

            _scriptAliases[script.OriginalName] = config;
        }

        SaveMetadata();
    }

    [RelayCommand]
    private async Task AddScript(string type)
    {
        if (PickScriptFileFunc == null) return;
        
        var sourceFile = await PickScriptFileFunc();
        if (string.IsNullOrEmpty(sourceFile)) return;
        
        string targetSubDir = type switch 
        {
            "Master" => "Master",
            "Action" => "Action",
            _ => "Others"
        };
        
        var targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", targetSubDir);
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
        
        var dest = Path.Combine(targetDir, Path.GetFileName(sourceFile));
        try
        {
             File.Copy(sourceFile, dest, true);
             LoadAllScripts(); // Refresh
             StatusMessage = $"{type} Script Added!";
             IsStatusVisible = true;
             await Task.Delay(2000);
             IsStatusVisible = false;
        }
        catch(Exception ex)
        {
            StatusMessage = "Error Adding Script";
            Console.WriteLine(ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteScript(ScriptItem item)
    {
        try
        {
            if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
                // Also remove alias
                if (_scriptAliases.ContainsKey(item.OriginalName))
                {
                    _scriptAliases.Remove(item.OriginalName);
                    SaveMetadata();
                }
                LoadAllScripts();
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Error deleting script: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ReloadScript(ScriptItem item)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(
                Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                ? desktop.MainWindow : null);
            if (topLevel == null) return;

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = $"Select new file to replace {item.Name}",
                AllowMultiple = false,
                FileTypeFilter = new[] { 
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (result?.Count > 0)
            {
                var newFilePath = result[0].Path.LocalPath;
                
                // Copy new file to replace old one
                File.Copy(newFilePath, item.FullPath, overwrite: true);
                
                // Refresh the list to show updated file
                LoadAllScripts();
                
                Console.WriteLine($"Script '{item.Name}' reloaded successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reloading script: {ex.Message}");
        }
    }

    // --- SCRIPT ORDERING LOGIC ---
    [ObservableProperty] private ObservableCollection<ScriptOrderItem> _masterScriptOrderList = new();
    [ObservableProperty] private ObservableCollection<ScriptOrderItem> _actionScriptOrderList = new();
    [ObservableProperty] private ScriptOrderItem? _selectedMasterScriptItem;
    [ObservableProperty] private ScriptOrderItem? _selectedActionScriptItem;

    public void LoadScriptOrder()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
            var masterList = new List<ScriptOrderItem>();
            var actionList = new List<ScriptOrderItem>();
            
            // Files check helper
            bool IsMaster(string key) => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Master", key));
            bool IsAction(string key) => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Action", key)) || key.EndsWith(".jsx") || key.EndsWith(".pyw");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                
                 // Load Dictionary (New Format)
                Dictionary<string, ScriptConfig> dict;
                try 
                {
                     dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                     dict = new(); // Should have been migrated by LoadMetadata, but safe fallback
                }

                // Load Raw Order
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var orderedKeys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                
                foreach(var key in orderedKeys)
                {
                    var config = dict.ContainsKey(key) ? dict[key] : new ScriptConfig { Name = key, Code = "" };
                    var item = new ScriptOrderItem 
                    { 
                        Key = key, 
                        DisplayName = config.Name 
                    };
                    
                    if (IsMaster(key)) masterList.Add(item);
                    else actionList.Add(item); 
                }
            }
            
            MasterScriptOrderList = new ObservableCollection<ScriptOrderItem>(masterList);
            ActionScriptOrderList = new ObservableCollection<ScriptOrderItem>(actionList);
        }
        catch { }
    }

    [RelayCommand]
    private void MoveMasterUp()
    {
        if (SelectedMasterScriptItem == null) return;
        var index = MasterScriptOrderList.IndexOf(SelectedMasterScriptItem);
        if (index > 0)
        {
            MasterScriptOrderList.Move(index, index - 1);
            SaveScriptOrder();
        }
    }

    [RelayCommand]
    private void MoveMasterDown()
    {
        if (SelectedMasterScriptItem == null) return;
        var index = MasterScriptOrderList.IndexOf(SelectedMasterScriptItem);
        if (index < MasterScriptOrderList.Count - 1)
        {
            MasterScriptOrderList.Move(index, index + 1);
            SaveScriptOrder();
        }
    }

    [RelayCommand]
    private void MoveActionUp()
    {
        if (SelectedActionScriptItem == null) return;
        var index = ActionScriptOrderList.IndexOf(SelectedActionScriptItem);
        if (index > 0)
        {
            ActionScriptOrderList.Move(index, index - 1);
            SaveScriptOrder();
        }
    }

    [RelayCommand]
    private void MoveActionDown()
    {
        if (SelectedActionScriptItem == null) return;
        var index = ActionScriptOrderList.IndexOf(SelectedActionScriptItem);
        if (index < ActionScriptOrderList.Count - 1)
        {
            ActionScriptOrderList.Move(index, index + 1);
            SaveScriptOrder();
        }
    }

    private void SaveScriptOrder()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
            
            // Reconstruct Dictionary while preserving order
            // We need to merge Master and Action lists, AND preserve any existing config (ShortCodes)
            // But _scriptAliases holds the truth for ShortCodes.
            
            var newDict = new Dictionary<string, ScriptConfig>();
            
             // Helper to get existing config or create default
            ScriptConfig GetConfig(string key, string displayName)
            {
                if (_scriptAliases.ContainsKey(key))
                {
                    var existing = _scriptAliases[key];
                    existing.Name = displayName; // Update name from order list if changed (though they should sync)
                    return existing;
                }
                return new ScriptConfig { Name = displayName, Code = "" };
            }

            foreach(var item in MasterScriptOrderList) 
                newDict[item.Key] = GetConfig(item.Key, item.DisplayName);
                
            foreach(var item in ActionScriptOrderList) 
                newDict[item.Key] = GetConfig(item.Key, item.DisplayName);
            
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(newDict, options);
            File.WriteAllText(path, json);
            
            // Update local memory cache
            _scriptAliases = newDict;
            
            WeakReferenceMessenger.Default.Send(new Messages.ScriptOrderChangedMessage());
        }
        catch { }
    }

    public void MoveMasterScript(ScriptOrderItem item, int newIndex)
    {
        if (item == null || newIndex < 0 || newIndex >= MasterScriptOrderList.Count) return;
        var oldIndex = MasterScriptOrderList.IndexOf(item);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        MasterScriptOrderList.Move(oldIndex, newIndex);
        SaveScriptOrder();
    }

    public void MoveActionScript(ScriptOrderItem item, int newIndex)
    {
        if (item == null || newIndex < 0 || newIndex >= ActionScriptOrderList.Count) return;
        var oldIndex = ActionScriptOrderList.IndexOf(item);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        ActionScriptOrderList.Move(oldIndex, newIndex);
        SaveScriptOrder();
    }

    // --- Customizable Shortcut ---
    [ObservableProperty]
    private TriggerConfig _currentShortcut = new TriggerConfig(); // Default Shift+Middle

    [ObservableProperty]
    private bool _isRecordingShortcut;

    [RelayCommand]
    private void StartRecordingShortcut()
    {
        IsRecordingShortcut = true;
        WeakReferenceMessenger.Default.Send(new SetRecordingModeMessage(true));
    }

    [RelayCommand]
    private void CancelRecordingShortcut()
    {
        IsRecordingShortcut = false;
        WeakReferenceMessenger.Default.Send(new SetRecordingModeMessage(false));
    }

    public void OnShortcutRecorded(TriggerRecordedMessage msg)
    {
        CurrentShortcut = msg.Value;
        IsRecordingShortcut = false;
        SaveShortcutConfig();
    }
    
    private void SaveShortcutConfig()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(CurrentShortcut);
            _database?.SetAsync("ShortcutConfig", json);
            WeakReferenceMessenger.Default.Send(new UpdateTriggerConfigMessage(CurrentShortcut));
        }
        catch {}
    }
    
    private async void LoadShortcutConfig()
    {
        try
        {
            var json = await _database.GetAsync<string>("ShortcutConfig");
            if (!string.IsNullOrEmpty(json))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<TriggerConfig>(json);
                if (config != null)
                {
                    CurrentShortcut = config;
                    WeakReferenceMessenger.Default.Send(new UpdateTriggerConfigMessage(CurrentShortcut));
                }
            }
        }
        catch {}
    }
}
public partial class ScriptItem : ObservableObject
{
    [ObservableProperty] private string _name = ""; // Display Name
    [ObservableProperty] private string _shortCode = ""; // Short Code (NEW)
    [ObservableProperty] private string _iconKey = ""; // Icon Key (NEW)
    [ObservableProperty] private string _originalName = ""; // Real Filename
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private string _type = "";
    
    // External Picker Function
    public Func<Task<string?>>? PickIconFunc { get; set; }
    
    // Editing Logic
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editShortCode = "";
    [ObservableProperty] private string _editIconKey = "";
    
    // Action: (Name, Code, IconKey)
    public event Action<string, string, string>? OnSaveRequested;

    public StreamGeometry? IconGeometry 
    {
        get
        {
            if (!string.IsNullOrEmpty(IconKey) && Application.Current!.TryGetResource(IconKey, null, out var res) && res is StreamGeometry geom)
                return geom;
            return null; 
        }
    }
    
    public StreamGeometry? EditIconGeometry 
    {
        get
        {
            if (!string.IsNullOrEmpty(EditIconKey) && Application.Current!.TryGetResource(EditIconKey, null, out var res) && res is StreamGeometry geom)
                return geom;
            return null;
        }
    }

    partial void OnIconKeyChanged(string value) => OnPropertyChanged(nameof(IconGeometry));
    partial void OnEditIconKeyChanged(string value) => OnPropertyChanged(nameof(EditIconGeometry));

    [RelayCommand]
    private void ToggleEdit()
    {
        if (IsEditing)
        {
            // Cancel
            IsEditing = false;
        }
        else
        {
            // Start
            EditName = Name;
            EditShortCode = ShortCode;
            EditIconKey = IconKey;
            IsEditing = true;
        }
    }
    
    [RelayCommand]
    private async Task PickIcon()
    {
        if (PickIconFunc != null)
        {
            var key = await PickIconFunc();
            if (key != null) // If null (cancel), do nothing
            {
                EditIconKey = key; // Empty string clears it
            }
        }
    }
    
    [RelayCommand]
    private void SaveRename()
    {
        OnSaveRequested?.Invoke(EditName, EditShortCode, EditIconKey);
        IsEditing = false;
    }
}



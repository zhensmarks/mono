using CommunityToolkit.Mvvm.ComponentModel;
using BMachine.UI.ViewModels;
using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using CommunityToolkit.Mvvm.Input;
using Avalonia;

namespace BMachine.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IRecipient<ThemeSettingsChangedMessage>
{
    [ObservableProperty]
    private object? _currentView;

    // Services
    private readonly BMachine.Core.Database.DatabaseService _database;
    private readonly BMachine.UI.Services.LanguageService _languageService;
    private readonly BMachine.UI.Services.ProcessLogService _logService;

    [ObservableProperty]
    private bool _isAnimationEnabled = true;

    [ObservableProperty]
    private IBrush _windowBackground = Brushes.Transparent; // Default

    [ObservableProperty]
    private bool _isExitConfirmOpen;

    [RelayCommand]
    private void OpenExitConfirm() => IsExitConfirmOpen = true;

    [RelayCommand]
    private void CancelExit() => IsExitConfirmOpen = false;

    [RelayCommand]
    private void ConfirmExit()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private string _cachedDarkBg = "#1C1C1C";
    private string _cachedLightBg = "#F5F5F5";

    public MainWindowViewModel(BMachine.Core.Database.DatabaseService? db = null, BMachine.UI.Services.ProcessLogService? logService = null)
    {
        // Init services (Use passed or create default)
        _database = db ?? new BMachine.Core.Database.DatabaseService();
        _languageService = new BMachine.UI.Services.LanguageService(_database);
        _logService = logService ?? new BMachine.UI.Services.ProcessLogService();
        
        // Initialize Language (async fire and forget)
        _ = _languageService.InitializeAsync();
        
        // Load Configs
        LoadAnimationConfig();
        _ = LoadBackgroundConfig();

        // Start with Dashboard
        NavigateToDashboard();
        
        // Register Messenger
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Listen to Theme Changes
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) => UpdateBackground();
        }
    }

    private async System.Threading.Tasks.Task LoadBackgroundConfig()
    {
        var dark = await _database.GetAsync<string>("Appearance.Background.Dark");
        if (!string.IsNullOrEmpty(dark)) _cachedDarkBg = dark;
        
        var light = await _database.GetAsync<string>("Appearance.Background.Light");
        if (!string.IsNullOrEmpty(light)) _cachedLightBg = light;
        
        UpdateBackground();
    }
    
    public void Receive(ThemeSettingsChangedMessage message)
    {
        if (!string.IsNullOrEmpty(message.DarkBackgroundColor)) _cachedDarkBg = message.DarkBackgroundColor;
        if (!string.IsNullOrEmpty(message.LightBackgroundColor)) _cachedLightBg = message.LightBackgroundColor;
        UpdateBackground();
    }

    private void UpdateBackground()
    {
        var isDark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var hex = isDark ? _cachedDarkBg : _cachedLightBg;
        
        try
        {
            if (Color.TryParse(hex, out var color))
            {
                WindowBackground = new SolidColorBrush(color);
                
                // Update Resource for other Windows/Views
                if (Application.Current != null)
                {
                    Application.Current.Resources["AppBackgroundBrush"] = WindowBackground;
                }
            }
        }
        catch { }
    }

    private async void LoadAnimationConfig()
    {
         var animStr = await _database.GetAsync<string>("Settings.StartupAnim");
         IsAnimationEnabled = string.IsNullOrEmpty(animStr) || bool.Parse(animStr);
    }

    private DashboardViewModel? _cachedDashboardVM;

    public void NavigateToDashboard()
    {
        if (_cachedDashboardVM == null)
        {
            _cachedDashboardVM = new DashboardViewModel(_database, _database, _languageService, _logService);
            _cachedDashboardVM.OpenSettingsRequested += () => NavigateToSettings();
            _cachedDashboardVM.OpenEditingListRequested += () => NavigateToEditingList();
            _cachedDashboardVM.OpenRevisionListRequested += () => NavigateToRevisionList();
            _cachedDashboardVM.OpenLateListRequested += () => NavigateToLateList();
        }
        
        CurrentView = _cachedDashboardVM;
    }

    public void NavigateToSettings()
    {
        var vm = new SettingsViewModel(_database, NavigateToDashboard, _languageService);
        CurrentView = vm;
    }

    public void NavigateToEditingList()
    {
        var vm = new EditingCardListViewModel(_database);
        vm.CloseRequested += () => NavigateToDashboard();
        vm.StartAutoRefresh();
        CurrentView = vm;
    }

    public void NavigateToRevisionList()
    {
        var vm = new RevisionCardListViewModel(_database);
        vm.CloseRequested += () => NavigateToDashboard();
        vm.StartAutoRefresh();
        CurrentView = vm;
    }

    public void NavigateToLateList()
    {
        var vm = new LateCardListViewModel(_database);
        vm.CloseRequested += () => NavigateToDashboard();
        vm.StartAutoRefresh();
        CurrentView = vm;
    }

    public async System.Threading.Tasks.Task SaveWindowState(double width, double height, Avalonia.Controls.WindowState state, int x, int y)
    {
        await _database.SetAsync("Configs.Window.Width", width.ToString());
        await _database.SetAsync("Configs.Window.Height", height.ToString());
        await _database.SetAsync("Configs.Window.State", state.ToString());
        await _database.SetAsync("Configs.Window.X", x.ToString());
        await _database.SetAsync("Configs.Window.Y", y.ToString());
    }

    public async System.Threading.Tasks.Task<(double W, double H, Avalonia.Controls.WindowState State, int X, int Y)?> GetSavedWindowState()
    {
        var wStr = await _database.GetAsync<string>("Configs.Window.Width");
        var hStr = await _database.GetAsync<string>("Configs.Window.Height");
        var sStr = await _database.GetAsync<string>("Configs.Window.State");
        var xStr = await _database.GetAsync<string>("Configs.Window.X");
        var yStr = await _database.GetAsync<string>("Configs.Window.Y");

        if (double.TryParse(wStr, out double w) && double.TryParse(hStr, out double h))
        {
            Avalonia.Controls.WindowState state = Avalonia.Controls.WindowState.Normal;
            if (Enum.TryParse(sStr, out Avalonia.Controls.WindowState parsedState)) state = parsedState;
            
            int x = 0, y = 0;
            int.TryParse(xStr, out x);
            int.TryParse(yStr, out y);
            
            return (w, h, state, x, y);
        }
        return null; // No Saved Config
    }
}

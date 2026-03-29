using CommunityToolkit.Mvvm.ComponentModel;
using BMachine.UI.ViewModels;
using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using CommunityToolkit.Mvvm.Input;
using Avalonia;

namespace BMachine.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IRecipient<ThemeSettingsChangedMessage>, IRecipient<UpdateAvailableMessage>, IRecipient<OpenExitConfirmMessage>
{
    [ObservableProperty]
    private object? _currentView;

    // Services
    private readonly BMachine.Core.Database.DatabaseService _database;
    private readonly BMachine.UI.Services.LanguageService _languageService;
    private readonly BMachine.UI.Services.ProcessLogService _logService;

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

    [RelayCommand]
    private void RestartApplication()
    {
        var appPath = System.Environment.ProcessPath;
        if (appPath != null)
        {
            System.Diagnostics.Process.Start(appPath);
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                System.Environment.Exit(0);
            }
        }
    }

    private string _cachedDarkBg = "#1C1C1C";
    private string _cachedLightBg = "#F5F5F5";



    public MainWindowViewModel(
        BMachine.Core.Database.DatabaseService? db = null, 
        BMachine.UI.Services.ProcessLogService? logService = null)
    {
        // Init services (Use passed or create default)
        _database = db ?? new BMachine.Core.Database.DatabaseService();
        _languageService = new BMachine.UI.Services.LanguageService(_database);
        _logService = logService ?? new BMachine.UI.Services.ProcessLogService();
        
        // Initialize Language (async fire and forget)
        _ = _languageService.InitializeAsync();
        
        // Load Configs
        _ = LoadBackgroundConfig();
        _ = LoadShortcutConfig();

        // Remove deferred dashboard load, it will be controlled externally by splash screen
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Listen to Theme Changes
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) => UpdateBackground();
        }
        
        // Check for updates
        CheckForUpdatesBackground();
        
        // Eager-load SettingsViewModel in background after 1s delay (to ensure Dashboard is ready)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000); // Wait for Dashboard to fully initialize
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _cachedSettingsVM = new SettingsViewModel(_database, NavigateToDashboard, _languageService, null);
                    Console.WriteLine("[MainWindow] SettingsViewModel pre-loaded successfully");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] Error pre-loading SettingsViewModel: {ex.Message}");
            }
        });
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

    public void Receive(UpdateAvailableMessage message)
    {
        IsUpdateAvailable = true;
        LatestVersion = message.Value.LatestVersion;
        UpdateUrl = message.Value.DownloadUrl;
    }

    public void Receive(OpenExitConfirmMessage message)
    {
        IsExitConfirmOpen = true;
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

    // Update State
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private string _updateUrl = "";
    [ObservableProperty]
    private string _greeting = "Hello";
    
    // Store the initial log panel state loaded from DB until Dashboard is ready
    public bool InitialLogPanelOpen { get; set; } = false;

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (!string.IsNullOrEmpty(UpdateUrl))
        {
            try 
            { 
               System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = UpdateUrl, UseShellExecute = true }); 
            } 
            catch { }
        }
    }

    private DashboardViewModel? _cachedDashboardVM;

    [ObservableProperty]
    private bool _isLoadingDashboard = true;

    public async Task InitializeDashboardAsync()
    {
        _isLoadingDashboard = true;

        if (_cachedDashboardVM == null)
        {
            _cachedDashboardVM = new DashboardViewModel(_database, _database, _languageService, _logService);
            _cachedDashboardVM.OpenSettingsRequested += () => NavigateToSettings();
            _cachedDashboardVM.OpenEditingListRequested += () => NavigateToEditingList();
            _cachedDashboardVM.OpenRevisionListRequested += () => NavigateToRevisionList();
            _cachedDashboardVM.OpenLateListRequested += () => NavigateToLateList();
        }

        if (_cachedDashboardVM.InitializationTask != null)
        {
            try { await _cachedDashboardVM.InitializationTask; } catch {}
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            NavigateToDashboard();
            IsLoadingDashboard = false;
        });
    }

    private async void CheckForUpdatesBackground()
    {
        try
        {
            var service = new BMachine.UI.Services.UpdateService();
            var info = await service.CheckForUpdatesAsync();
            if (info.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                LatestVersion = info.LatestVersion;
                UpdateUrl = info.DownloadUrl;
            }
        }
        catch { }
    }

    public void NavigateToDashboard()
    {
        if (_cachedDashboardVM == null)
        {
            _cachedDashboardVM = new DashboardViewModel(_database, _database, _languageService, _logService);
            _cachedDashboardVM.OpenSettingsRequested += () => NavigateToSettings();
            _cachedDashboardVM.OpenEditingListRequested += () => NavigateToEditingList();
            _cachedDashboardVM.OpenRevisionListRequested += () => NavigateToRevisionList();
            _cachedDashboardVM.OpenLateListRequested += () => NavigateToLateList();
            
            // Apply log panel state restored from MainWindow
            _cachedDashboardVM.IsLogPanelOpen = InitialLogPanelOpen;
            _cachedDashboardVM.MarkInitialLoadComplete();
        }
        
        CurrentView = _cachedDashboardVM;
    }

    private SettingsViewModel? _cachedSettingsVM;
    
    public void NavigateToSettings()
    {
        // Fallback to lazy loading if eager-loading hasn't completed yet
        if (_cachedSettingsVM == null)
        {
            _cachedSettingsVM = new SettingsViewModel(_database, NavigateToDashboard, _languageService, null);
        }
        CurrentView = _cachedSettingsVM;
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

    public async System.Threading.Tasks.Task SaveWindowState(double width, double height, Avalonia.Controls.WindowState state, int x, int y, bool isLogPanelOpen)
    {
        await _database.SetAsync("Configs.Window.Width", width.ToString());
        await _database.SetAsync("Configs.Window.Height", height.ToString());
        await _database.SetAsync("Configs.Window.State", state.ToString());
        await _database.SetAsync("Configs.Window.X", x.ToString());
        await _database.SetAsync("Configs.Window.Y", y.ToString());
        await _database.SetAsync("Configs.Window.LogPanel", isLogPanelOpen.ToString());
    }

    public async System.Threading.Tasks.Task<(double W, double H, Avalonia.Controls.WindowState State, int X, int Y, bool LogPanel)?> GetSavedWindowState()
    {
        var wStr = await _database.GetAsync<string>("Configs.Window.Width");
        var hStr = await _database.GetAsync<string>("Configs.Window.Height");
        var sStr = await _database.GetAsync<string>("Configs.Window.State");
        var xStr = await _database.GetAsync<string>("Configs.Window.X");
        var yStr = await _database.GetAsync<string>("Configs.Window.Y");
        var lpStr = await _database.GetAsync<string>("Configs.Window.LogPanel");

        if (double.TryParse(wStr, out double w) && double.TryParse(hStr, out double h))
        {
            Avalonia.Controls.WindowState state = Avalonia.Controls.WindowState.Normal;
            if (Enum.TryParse(sStr, out Avalonia.Controls.WindowState parsedState)) state = parsedState;
            
            int x = 0, y = 0;
            int.TryParse(xStr, out x);
            int.TryParse(yStr, out y);
            bool logPanel = false;
            bool.TryParse(lpStr, out logPanel);
            
            return (w, h, state, x, y, logPanel);
        }
        return null; // No Saved Config
    }

    private async System.Threading.Tasks.Task LoadShortcutConfig()
    {
        try
        {
            var json = await _database.GetAsync<string>("ShortcutConfig");
            if (!string.IsNullOrEmpty(json))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<BMachine.UI.Models.TriggerConfig>(json);
                if (config != null)
                {
                    WeakReferenceMessenger.Default.Send(new UpdateTriggerConfigMessage(config));
                }
            }
        }
        catch {}
    }
}

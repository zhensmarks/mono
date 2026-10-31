using CommunityToolkit.Mvvm.ComponentModel;
using BMachine.UI.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls;
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
                Console.WriteLine($"[MainWindow] SettingsViewModel preload failed ({ex.GetType().Name}).");
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
        ShowUpdatePopup(message.Value);
    }

    public void Receive(OpenExitConfirmMessage message)
    {
        IsExitConfirmOpen = true;
    }

    private void UpdateBackground()
    {
        BMachine.UI.Services.AppearancePaletteService.RemoveOverrides();
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
                    Application.Current.Resources["CardBackgroundBrush"] = WindowBackground;
                    Application.Current.Resources["TerminalBackgroundBrush"] = WindowBackground;
                    Application.Current.Resources["BackgroundDarkBrush"] = WindowBackground;
                }
            }
        }
        catch { }
        BMachine.UI.Services.AppearancePaletteService.ApplyCurrent();
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
        IsLoadingDashboard = true;

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
            var folder = BMachine.UI.Services.LocalUpdateService.ResolveFolder(
                await _database.GetAsync<string>("Update.LocalPath"));

            var (info, status) = await BMachine.UI.Services.LocalUpdateService.CheckAsync(folder);
            Console.WriteLine($"[Update] Folder='{folder}' Local='{info.CurrentVersion}' Remote='{info.LatestVersion}' Available={info.IsUpdateAvailable} ({status})");
            if (!info.IsUpdateAvailable) return;

            // Bagikan hasilnya SEBELUM cek "Nanti": badge di popup akun memang
            // bertugas memberi tahu tanpa mengganggu (cukup satu kali cek, hasilnya
            // disimpan di memori — membuka popup tidak membaca folder share lagi).
            _currentUpdateInfo = info;
            WeakReferenceMessenger.Default.Send(new UpdateStateMessage(info));

            // Kalau user sudah menekan "Nanti" untuk versi ini, jangan diganggu lagi.
            var dismissed = await _database.GetAsync<string>("Update.DismissedVersion");
            if (!string.IsNullOrEmpty(dismissed) &&
                string.Equals(dismissed.TrimStart('v', 'V'), info.LatestVersion.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastUpdateInfo = info;
            _pendingUpdateInfo = info;
            // Jangan tampilkan selama masih di atas splash: dialog yang muncul
            // sebelum window utama tampil jatuh di posisi keliru sehingga kliknya
            // tembus ke window di belakangnya.
            if (_mainWindowReady) ShowUpdatePopup(info);
        }
        catch { }
    }

    private BMachine.UI.Models.UpdateInfo? _pendingUpdateInfo;
    private bool _mainWindowReady;

    /// <summary>Hasil cek update terakhir, dipakai untuk push ke badge popup akun
    /// setelah DashboardViewModel dibuat (messenger tidak mengantre pesan lama).</summary>
    private BMachine.UI.Models.UpdateInfo? _currentUpdateInfo;

    /// <summary>Dipanggil App setelah MainWindow.Show() + splash ditutup.</summary>
    public void MarkMainWindowReady()
    {
        _mainWindowReady = true;
        if (_pendingUpdateInfo is { } pending)
        {
            _pendingUpdateInfo = null;
            ShowUpdatePopup(pending);
        }
    }

    /// <summary>Buka dialog update popup (jendela baru, bukan strip header).</summary>
    private void ShowUpdatePopup(BMachine.UI.Models.UpdateInfo info)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Isi state dulu (versi saat ini, catatan rilis, ukuran payload).
            // Tanpa ini dialog tampil kosong: "Versi saat ini:" dan daftar butir
            // tidak terisi karena binding mengambil dari properti yang belum pernah di-set.
            ShowUpdateBanner(info);

            if (_updateDialog is { IsVisible: true })
            {
                _updateDialog.Activate();
                return;
            }

            _updateDialog = new BMachine.UI.Views.Dialogs.UpdateDialogWindow
            {
                DataContext = this,
            };
            // Owned window: selalu di atas window utama, tidak bisa jatuh ke belakang
            // (kalau jatuh, klik tombolnya tembus ke window di bawahnya).
            var owner = (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is { IsVisible: true } and not BMachine.UI.Views.SplashWindow)
                _updateDialog.Show(owner);
            else
                _updateDialog.Show();
            ClampToVisibleArea(_updateDialog);
            _updateDialog.Activate();
            Console.WriteLine($"[Update] popup dialog (v{info.LatestVersion})");
        });
    }

    /// <summary>Jaga dialog selalu sepenuhnya di dalam layar. CenterOwner pada owner
    /// yang kecil atau menyempit ke tepi layar bisa melempar dialog ke luar layar
    /// (tercatat posisi 0,1052 dengan tinggi 540 pada layar 1080) sehingga tombolnya
    /// tak terjangkau dan pengguna mengira dialognya rusak.</summary>
    private static void ClampToVisibleArea(Avalonia.Controls.Window window)
    {
        try
        {
            var screen = window.Screens.ScreenFromPoint(window.Position) ?? window.Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;

            var width = (int)window.Bounds.Width;
            var height = (int)window.Bounds.Height;
            if (width <= 0 || height <= 0) return;

            var x = Math.Clamp(window.Position.X, area.X, Math.Max(area.X, area.X + area.Width - width));
            var y = Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
            if (x != window.Position.X || y != window.Position.Y)
                window.Position = new Avalonia.PixelPoint(x, y);
        }
        catch { /* layar bisa berubah di tengah jalan; posisi default tetap dipakai */ }
    }

    private Avalonia.Controls.Window? _updateDialog;

    // --- Mode update lokal (LAN) ---
    private BMachine.UI.Models.UpdateInfo? _lastUpdateInfo;

    [ObservableProperty] private string _updateNotes = "";
    [ObservableProperty] private string _updateNotesInline = "";
    [ObservableProperty] private bool _isApplyingUpdate;
    [ObservableProperty] private string _updateActionStatus = "";

    /// <summary>Versi yang sedang dipakai app (untuk perbandingan di banner).</summary>
    [ObservableProperty] private string _currentVersion = "";

    /// <summary>Ukuran payload siap tampil (mis. "147 MB"); kosong kalau manifest lama.</summary>
    [ObservableProperty] private string _updateSizeText = "";

    /// <summary>Poin catatan rilis untuk daftar berbutir di banner.</summary>
    [ObservableProperty] private ObservableCollection<string> _updateNoteItems = new();

    /// <summary>Isi seluruh state banner dari satu UpdateInfo (dipakai jalur startup &amp; pesan).</summary>
    private void ShowUpdateBanner(BMachine.UI.Models.UpdateInfo info)
    {
        // Diganti oleh ShowUpdatePopup; IsUpdateAvailable dipakai hanya sebagai flag state,
        // bukan sebagai visibility banner (banner sudah dihapus dari MainWindow.axaml).
        _lastUpdateInfo = info;
        LatestVersion = info.LatestVersion;
        CurrentVersion = info.CurrentVersion;
        UpdateUrl = info.DownloadUrl;
        UpdateNotes = info.NotesText;
        UpdateNotesInline = info.NotesInline;
        UpdateSizeText = info.SizeText;
        UpdateNoteItems = new ObservableCollection<string>(info.Notes.Take(4));
        UpdateActionStatus = "";
        IsUpdateAvailable = true;
    }

    partial void OnIsUpdateAvailableChanged(bool value)
        => Console.WriteLine($"[Update] banner {(value ? "tampil" : "disembunyikan")}");

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        if (_lastUpdateInfo == null) return;

        IsApplyingUpdate = true;
        UpdateActionStatus = "Menyiapkan update...";
        try
        {
            var (ok, message) = await BMachine.UI.Services.LocalUpdateService.ApplyAsync(_lastUpdateInfo);
            UpdateActionStatus = message;

            if (ok)
            {
                // Helper PowerShell menunggu proses ini keluar, lalu menyalin payload + membuka app lagi.
                await Task.Delay(1200);
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else
                    Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            UpdateActionStatus = $"Gagal: {ex.Message}";
        }
        finally
        {
            IsApplyingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DismissUpdate()
    {
        IsUpdateAvailable = false;
        if (!string.IsNullOrEmpty(LatestVersion))
        {
            await _database.SetAsync("Update.DismissedVersion", LatestVersion.TrimStart('v', 'V'));
        }

        // Tutup dialog setelah "Nanti": tanpa ini tombol menulis status tapi
        // jendelanya tetap terbuka, sehingga terasa tidak bereaksi.
        if (_updateDialog is { } dialog)
        {
            _updateDialog = null;
            dialog.Close();
        }
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

            // DashboardViewModel dibuat BELAKANGAN; messenger tidak mengantre pesan
            // yang dikirim sebelum registrasi, jadi push ulang hasil ceknya di sini.
            if (_currentUpdateInfo is { } upd)
                WeakReferenceMessenger.Default.Send(new UpdateStateMessage(upd));
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

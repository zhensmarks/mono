using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;
using System.Diagnostics;
using PixelcutCompact.Models;
using PixelcutCompact.Services;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PixelcutCompact.Views;
using System.Net.NetworkInformation;

namespace PixelcutCompact.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PixelcutService _pixelcutService = new();
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _cts;
    private System.Timers.Timer? _vpnCheckTimer;

    // Settings dialog state (Save/Close UX)
    [ObservableProperty] private bool _isSettingsDirty;
    private bool _isRevertingSettings;
    private bool _hasSettingsSnapshot;
    private bool _snapshotIsDarkTheme;
    private string _snapshotAccentColorHex = "";
    private string _snapshotBackgroundHex = "";
    private string _snapshotRemoveBgEngine = "PIXA";
    private string _snapshotRembgModel = "u2netp";
    private string _snapshotRembgExecutablePath = "";
    private bool _snapshotMixProxyEnabled;
    private string _snapshotMixProxyList = "";
    private bool _snapshotShowBrowser;
    private bool _snapshotUseGpuForRembg;
    private bool _snapshotAlphaMattingEnabled;
    private int _snapshotAlphaMattingErodeSize;
    private int _snapshotAlphaMattingForegroundThreshold;
    private int _snapshotAlphaMattingBackgroundThreshold;
    
    [ObservableProperty] private ObservableCollection<PixelcutFileItem> _files = new();
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _isProcessing;
    public int FilesCount => Files.Count;

    /// <summary>Jumlah item yang sudah selesai (done atau failed) — digunakan untuk progress counter.</summary>
    public int ProcessedCount => Files.Count(x => x.IsDone || x.IsFailed);

    /// <summary>Teks header tab: 'Proses (N)' saat idle, 'Proses (X/N)' saat sedang processing.</summary>
    public string TabHeaderText =>
        IsProcessing
            ? $"Proses ({ProcessedCount}/{FilesCount})"
            : $"Proses ({FilesCount})";
    [ObservableProperty] private string _vpnStatus = "Memeriksa...";
    [ObservableProperty] private bool _isVpnActive;
    [ObservableProperty] private string _logOutput = "";
    [ObservableProperty] private bool _showLogPanel;
    [ObservableProperty] private string _statusText = "Siap";
    [ObservableProperty] private int _skippedCount;
    
    // Settings
    [ObservableProperty] private bool _isDarkTheme; // Mapped to Theme
    [ObservableProperty] private string _accentColorHex = "#3b82f6";
    [ObservableProperty] private string _removeBgEngine = "PIXA";
    [ObservableProperty] private string _rembgModel = "u2netp";
    [ObservableProperty] private string _rembgExecutablePath = "";
    [ObservableProperty] private bool _mixProxyEnabled;
    [ObservableProperty] private string _mixProxyList = "";
    [ObservableProperty] private bool _showBrowser;
    [ObservableProperty] private bool _useGpuForRembg;
    [ObservableProperty] private bool _alphaMattingEnabled;
    [ObservableProperty] private int _alphaMattingErodeSize = 10;
    [ObservableProperty] private int _alphaMattingForegroundThreshold = 240;
    [ObservableProperty] private int _alphaMattingBackgroundThreshold = 10;

    [ObservableProperty] private bool _useWebMode = true;
    
    // We bind the UI to this property. When user edits this, we verify which mode we are in and save to the correct field.
    [ObservableProperty] private string _currentBackgroundColorHex = ""; 
    
    // Alert Overlay
    [ObservableProperty] private bool _isAlertOpen;
    [ObservableProperty] private string _alertMessage = "";
    [RelayCommand] private void CloseAlert() => IsAlertOpen = false;
    [ObservableProperty] private bool _isModePickerOpen;
    [RelayCommand] private void ToggleModePicker() => IsModePickerOpen = !IsModePickerOpen;

    // Allowed Paths Sesi (RAM Cache)
    private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private string[]? _pendingPaths;

    private string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    // Confirm Import Overlay
    [ObservableProperty] private bool _isConfirmImportOpen;
    [ObservableProperty] private string _confirmImportMessage = "";

    [RelayCommand]
    private async Task AllowImport()
    {
        IsConfirmImportOpen = false;
        if (_pendingPaths == null || _pendingPaths.Length == 0) return;

        IsProcessing = true;
        try
        {
            var pathsToScan = new List<string>();
            foreach (var path in _pendingPaths)
            {
                var normalizedPath = NormalizePath(path);
                _allowedPaths.Add(normalizedPath);

                // Jika path adalah file, izinkan juga folder induknya
                if (File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        _allowedPaths.Add(NormalizePath(dir));
                    }
                }
                else if (Directory.Exists(path))
                {
                    _allowedPaths.Add(normalizedPath);
                }

                pathsToScan.Add(path);
            }
            await ScanAndAddPathsAsync(pathsToScan);
        }
        catch (Exception ex)
        {
            AppendLog($"Error import: {ex.Message}");
        }
        finally
        {
            _pendingPaths = null;
            IsProcessing = _cts != null;
            CheckRetryVisibility();
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _pendingPaths = null;
        IsConfirmImportOpen = false;
    }

    private int CalculateLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private bool IsPilihanFolderMatch(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var clean = name.Trim().ToLowerInvariant();

        // Layer 1: Containment check for "pilih"
        if (clean.Contains("pilih")) return true;

        // Layer 2: Fuzzy Levenshtein distance <= 2 with "pilihan"
        if (clean.Length >= 4)
        {
            int dist = CalculateLevenshteinDistance(clean, "pilihan");
            if (dist <= 2) return true;
        }

        return false;
    }

    private bool IsAlreadyInsidePilihanFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string? current = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            while (!string.IsNullOrEmpty(current))
            {
                var dirName = Path.GetFileName(current);
                if (IsPilihanFolderMatch(dirName)) return true;
                current = Path.GetDirectoryName(current);
            }
        }
        catch { }
        return false;
    }

    private bool IsPathInAllowedCache(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string? current = NormalizePath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (_allowedPaths.Contains(current)) return true;
                current = Path.GetDirectoryName(current);
                if (current != null)
                {
                    current = NormalizePath(current);
                }
            }
        }
        catch { }
        return false;
    }

    // Toast Notification
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastIcon = "✅";
    private System.Timers.Timer? _toastTimer;

    
    private string? _customDarkBackground;
    private string? _customLightBackground;

    private bool _stopRequested;
    [ObservableProperty] private bool _isPaused;

    // Gallery and Preview Pane
    [ObservableProperty] private ObservableCollection<GalleryItemViewModel> _galleryItems = new();
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _hasSelectedWithResult;
    [ObservableProperty] private bool _isPreviewPaneVisible;
    [ObservableProperty] private PixelcutFileItem? _selectedPreviewItem;

    // Offline Resource Manager
    [ObservableProperty] private bool _isRembgInstalled;
    [ObservableProperty] private bool _isInstallingRembg;
    [ObservableProperty] private double _rembgInstallProgress;
    [ObservableProperty] private string _rembgInstallStatus = "Belum Terpasang";

    [RelayCommand]
    private async Task InstallRembgOffline()
    {
        IsInstallingRembg = true;
        RembgInstallStatus = "Mengunduh...";
        RembgInstallProgress = 0;
        
        try
        {
            var manager = new RembgResourceManager();
            var progress = new Progress<InstallProgressInfo>(info => 
            {
                RembgInstallProgress = info.Percentage;
                if (!string.IsNullOrWhiteSpace(info.Message)) 
                {
                    // Limit text length if it's from PIP output to avoid UI jitter
                    var msg = info.Message.Length > 80 ? info.Message.Substring(0, 77) + "..." : info.Message;
                    RembgInstallStatus = msg;
                }
            });
            
            await manager.DownloadAndInstallAsync(progress, CancellationToken.None);
            
            IsRembgInstalled = true;
            RembgInstallStatus = "Terpasang";
        }
        catch (Exception ex)
        {
            RembgInstallStatus = $"Gagal: {ex.Message}";
        }
        finally
        {
            IsInstallingRembg = false;
        }
    }

    [RelayCommand]
    private void UninstallRembgOffline()
    {
        var manager = new RembgResourceManager();
        manager.Uninstall();
        IsRembgInstalled = false;
        RembgInstallStatus = "Belum Terpasang";
        RembgInstallProgress = 0;
    }

    public MainWindowViewModel()
    {
        
        // Load Settings
        var settings = _settingsService.Load();
        AccentColorHex = settings.AccentColor;
        IsDarkTheme = settings.Theme == "Dark";
        UseWebMode = true;
        RemoveBgEngine = NormalizeEngine(settings.RemoveBgEngine);
        RembgModel = string.IsNullOrWhiteSpace(settings.RembgModel) ? "u2netp" : settings.RembgModel;
        RembgExecutablePath = settings.RembgExecutablePath ?? "";
        MixProxyEnabled = settings.MixProxyEnabled;
        MixProxyList = settings.MixProxyList ?? "";
        _customDarkBackground = settings.CustomDarkBackground;
        _customLightBackground = settings.CustomLightBackground;

        ApplyTheme(IsDarkTheme);
        ApplyAccentColor(AccentColorHex);

        // Initialize Service
        Task.Run(async () => await _pixelcutService.InitializeAsync());
        
        _pixelcutService.UseWebMode = true;
        _pixelcutService.RemoveBgEngine = RemoveBgEngine;
        _pixelcutService.RembgModel = RembgModel;
        _pixelcutService.RembgExecutablePath = string.IsNullOrWhiteSpace(RembgExecutablePath) ? null : RembgExecutablePath;
        _pixelcutService.MixProxyEnabled = MixProxyEnabled;
        _pixelcutService.MixProxyList = MixProxyList;
        _pixelcutService.ShowBrowser = settings.ShowBrowser;
        ShowBrowser = settings.ShowBrowser;
        UseGpuForRembg = settings.UseGpuForRembg;
        _pixelcutService.UseGpuForRembg = UseGpuForRembg;

        AlphaMattingEnabled = settings.AlphaMattingEnabled;
        AlphaMattingErodeSize = settings.AlphaMattingErodeSize;
        AlphaMattingForegroundThreshold = settings.AlphaMattingForegroundThreshold;
        AlphaMattingBackgroundThreshold = settings.AlphaMattingBackgroundThreshold;

        _pixelcutService.AlphaMattingEnabled = AlphaMattingEnabled;
        _pixelcutService.AlphaMattingErodeSize = AlphaMattingErodeSize;
        _pixelcutService.AlphaMattingForegroundThreshold = AlphaMattingForegroundThreshold;
        _pixelcutService.AlphaMattingBackgroundThreshold = AlphaMattingBackgroundThreshold;

        // Start periodic VPN status check (every 5 seconds)
        CheckVpnStatus();
        _vpnCheckTimer = new System.Timers.Timer(5000);
        _vpnCheckTimer.Elapsed += (s, e) => Dispatcher.UIThread.Post(CheckVpnStatus);
        _vpnCheckTimer.Start();

        // Check if Rembg Offline is installed
        var resourceManager = new RembgResourceManager();
        IsRembgInstalled = resourceManager.IsInstalled();
        RembgInstallStatus = IsRembgInstalled ? "Terpasang" : "Belum Terpasang";

        // Subscribe to Files collection for auto-refreshing Gallery
        Files.CollectionChanged += OnFilesCollectionChanged;
    }

    private void OnFilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilesCount));
        OnPropertyChanged(nameof(TabHeaderText));
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (PixelcutFileItem item in e.NewItems)
            {
                // Ensure we run on UI thread to update ObservableCollection
                Dispatcher.UIThread.Post(() =>
                {
                    // Add Source
                    GalleryItems.Add(new GalleryItemViewModel(item, item.FilePath, true));
                    // Add Result if already processed
                    if (item.HasResult && File.Exists(item.ResultPath))
                    {
                        GalleryItems.Add(new GalleryItemViewModel(item, item.ResultPath, false));
                    }
                });
                
                // Hook to property changed to auto-add Result when done
                item.PropertyChanged += OnFileItemPropertyChanged;
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (PixelcutFileItem item in e.OldItems)
            {
                item.PropertyChanged -= OnFileItemPropertyChanged;
                
                Dispatcher.UIThread.Post(() =>
                {
                    var toRemove = GalleryItems.Where(g => g.ParentItem == item).ToList();
                    foreach (var g in toRemove) GalleryItems.Remove(g);
                });
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.UIThread.Post(() => GalleryItems.Clear());
        }
    }

    private void OnFileItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PixelcutFileItem.HasResult))
        {
            if (sender is PixelcutFileItem item && item.HasResult && File.Exists(item.ResultPath))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!GalleryItems.Any(g => g.ParentItem == item && !g.IsSource))
                    {
                        var newGalleryItem = new GalleryItemViewModel(item, item.ResultPath, false);
                        var originalItem = GalleryItems.FirstOrDefault(g => g.ParentItem == item && g.IsSource);
                        
                        if (originalItem != null)
                        {
                            var idx = GalleryItems.IndexOf(originalItem);
                            GalleryItems.Insert(idx + 1, newGalleryItem);
                        }
                        else
                        {
                            GalleryItems.Add(newGalleryItem);
                        }
                    }
                });
            }
        }
    }
    
    partial void OnUseWebModeChanged(bool value)
    {
        // Force web mode only.
        if (!value)
        {
            UseWebMode = true;
            return;
        }
        _pixelcutService.UseWebMode = true;
        SaveSettings();
    }


    partial void OnShowBrowserChanged(bool value)
    {
        _pixelcutService.ShowBrowser = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    public IReadOnlyList<string> RemoveBgEngines { get; } = new[] { "PIXA", "REMBG", "NOBG_SPACE", "BG_ERASER" };
    public sealed class RemoveBgEngineOption
    {
        public string Value { get; init; } = "PIXA";
        public string Label { get; init; } = "PIXA";
    }

    public IReadOnlyList<RemoveBgEngineOption> RemoveBgEngineOptions { get; } = new[]
    {
        new RemoveBgEngineOption { Value = "PIXA", Label = "PIXA" },
        new RemoveBgEngineOption { Value = "REMBG", Label = "REMBG (Offline AI)" },
        new RemoveBgEngineOption { Value = "REMBG_ONLINE", Label = "REMBG Online AI (Web)" },
        new RemoveBgEngineOption { Value = "NOBG_SPACE", Label = "NOBG Space (Web)" },
        new RemoveBgEngineOption { Value = "BG_ERASER", Label = "BG Eraser (Web)" }
    };

    public RemoveBgEngineOption? SelectedRemoveBgEngineOption
    {
        get => RemoveBgEngineOptions.FirstOrDefault(x => string.Equals(x.Value, RemoveBgEngine, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value == null) return;
            RemoveBgEngine = value.Value;
        }
    }

    public bool IsPixaSelected => string.Equals(RemoveBgEngine, "PIXA", StringComparison.OrdinalIgnoreCase);
    public bool IsRembgSelected => string.Equals(RemoveBgEngine, "REMBG", StringComparison.OrdinalIgnoreCase);
    public bool IsRembgOnlineSelected => string.Equals(RemoveBgEngine, "REMBG_ONLINE", StringComparison.OrdinalIgnoreCase);
    public bool IsNobgSpaceSelected => string.Equals(RemoveBgEngine, "NOBG_SPACE", StringComparison.OrdinalIgnoreCase);
    public bool IsBgEraserSelected => string.Equals(RemoveBgEngine, "BG_ERASER", StringComparison.OrdinalIgnoreCase);

    public bool IsAlphaMattingTuningVisible => IsRembgSelected && AlphaMattingEnabled;

    [RelayCommand]
    private void SelectEngine(string engine)
    {
        if (string.IsNullOrEmpty(engine)) return;
        RemoveBgEngine = engine;
    }

    public IReadOnlyList<string> RembgModels { get; } = new[]
    {
        "u2net",
        "u2netp",
        "u2net_human_seg",
        "u2net_cloth_seg",
        "silueta",
        "isnet-general-use",
        "isnet-anime",
        "sam",
        "birefnet-general",
        "birefnet-general-lite",
        "birefnet-portrait",
        "birefnet-dis",
        "birefnet-hrsod",
        "birefnet-cod",
        "birefnet-massive",
        "bria-rmbg"
    };

    partial void OnRemoveBgEngineChanged(string value)
    {
        var normalized = NormalizeEngine(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            RemoveBgEngine = normalized;
            return;
        }

        _pixelcutService.RemoveBgEngine = normalized;
        OnPropertyChanged(nameof(SelectedRemoveBgEngineOption));
        OnPropertyChanged(nameof(IsPixaSelected));
        OnPropertyChanged(nameof(IsRembgSelected));
        OnPropertyChanged(nameof(IsRembgOnlineSelected));
        OnPropertyChanged(nameof(IsNobgSpaceSelected));
        OnPropertyChanged(nameof(IsBgEraserSelected));
        OnPropertyChanged(nameof(IsAlphaMattingTuningVisible));
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnRembgModelChanged(string value)
    {
        _pixelcutService.RembgModel = string.IsNullOrWhiteSpace(value) ? "u2netp" : value.Trim();
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnRembgExecutablePathChanged(string value)
    {
        _pixelcutService.RembgExecutablePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnMixProxyEnabledChanged(bool value)
    {
        _pixelcutService.MixProxyEnabled = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnMixProxyListChanged(string value)
    {
        _pixelcutService.MixProxyList = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnUseGpuForRembgChanged(bool value)
    {
        _pixelcutService.UseGpuForRembg = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnAlphaMattingEnabledChanged(bool value)
    {
        _pixelcutService.AlphaMattingEnabled = value;
        OnPropertyChanged(nameof(IsAlphaMattingTuningVisible));
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnAlphaMattingErodeSizeChanged(int value)
    {
        _pixelcutService.AlphaMattingErodeSize = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnAlphaMattingForegroundThresholdChanged(int value)
    {
        _pixelcutService.AlphaMattingForegroundThreshold = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    partial void OnAlphaMattingBackgroundThresholdChanged(int value)
    {
        _pixelcutService.AlphaMattingBackgroundThreshold = value;
        if (!IsSettingsOpen) SaveSettings();
        MarkSettingsDirty();
    }

    [RelayCommand]
    private void ResetAlphaMatting()
    {
        AlphaMattingEnabled = false;
        AlphaMattingErodeSize = 10;
        AlphaMattingForegroundThreshold = 240;
        AlphaMattingBackgroundThreshold = 10;
        SaveSettings();
    }

    [RelayCommand]
    public async Task RefreshCreditsAsync()
    {
        await Task.CompletedTask;
    }

    private void CheckVpnStatus()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var vpnKeywords = new[] { "vpn", "tap", "ppp", "wintun", "wireguard", "openvpn", "avira", "nordvpn", "expressvpn", "protonvpn", "hotspot" };
            var macSystemInterfaces = new[] { "utun", "llw", "awdl", "bridge", "ap", "anpi", "gif", "stf", "ipsec" };

            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                var name = iface.Name.ToLower();
                var desc = iface.Description.ToLower();

                if (OperatingSystem.IsMacOS() && macSystemInterfaces.Any(si => name.StartsWith(si)))
                    continue;

                if (vpnKeywords.Any(kw => name.Contains(kw) || desc.Contains(kw)))
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }

                if (name == "tun0" || name == "tun1" || name == "tap0" || name == "tap1")
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }
            }

            IsVpnActive = false;
            VpnStatus = "Koneksi Langsung";
        }
        catch
        {
            IsVpnActive = false;
            VpnStatus = "Koneksi Langsung";
        }
    }

    // Warna dot status VPN di header
    public IBrush ConnectionStatusBrush => IsVpnActive ? SolidColorBrush.Parse("#10B981") : SolidColorBrush.Parse("#EF4444");
    partial void OnIsVpnActiveChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatusBrush));


    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplyTheme(value);
        MarkSettingsDirty();
    }

    partial void OnAccentColorHexChanged(string value)
    {
        ApplyAccentColor(value);
        OnPropertyChanged(nameof(AccentColor)); // Notify UI
        OnPropertyChanged(nameof(TrashButtonBrush));
        MarkSettingsDirty();
    }



    partial void OnHasFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(TrashButtonBrush));
    }

    partial void OnIsProcessingChanged(bool value)
    {
        StatusText = value ? "Memproses..." : "Siap";
        OnPropertyChanged(nameof(ProcessedCount));
        OnPropertyChanged(nameof(TabHeaderText));
    }
    
    partial void OnIsPausedChanged(bool value)
    {
        if (IsProcessing)
        {
            StatusText = value ? "Jeda" : "Memproses...";
        }
    }

    partial void OnCurrentBackgroundColorHexChanged(string value)
    {
        if (_isRevertingSettings) return;

        // When user types in the box/picker
        if (IsDarkTheme)
            _customDarkBackground = value;
        else
            _customLightBackground = value;
            
        if (!string.IsNullOrEmpty(value))
        {
             ApplyBackgroundColor(value);
        }
        else
        {
            // If user clears it, re-apply theme default
            ApplyTheme(IsDarkTheme);
        }
        OnPropertyChanged(nameof(CurrentBackgroundColor)); // Notify UI
        MarkSettingsDirty();
    }



    // Proxy properties for ColorPicker binding (Color <-> Hex String)
    public Color AccentColor
    {
        get => Color.TryParse(AccentColorHex, out var c) ? c : Colors.Blue;
        set => AccentColorHex = value.ToString();
    }

    public Color CurrentBackgroundColor
    {
        get => Color.TryParse(CurrentBackgroundColorHex, out var c) ? c : (IsDarkTheme ? Color.Parse("#1A1C20") : Colors.White);
        set => CurrentBackgroundColorHex = value.ToString();
    }

    private void ApplyTheme(bool isDark)
    {
        if (Application.Current != null)
        {
             Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
             
             var cardBg = isDark ? "#1A1C20" : "#FFFFFF";
             var cardBorder = isDark ? "#26282C" : "#E5E7EB";
             var textPrimary = isDark ? "#FFFFFF" : "#000000";
             var textSecondary = isDark ? "#99FFFFFF" : "#66000000";
             
             // Check if we have a custom BG for this mode
             var customBg = isDark ? _customDarkBackground : _customLightBackground;
             CurrentBackgroundColorHex = customBg ?? ""; // Update the UI textbox

             if (!string.IsNullOrEmpty(customBg))
             {
                 Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(customBg);
             }
             else
             {
                 Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(cardBg);
             }
             
             Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(cardBorder);
             Application.Current.Resources["TextPrimaryBrush"] = SolidColorBrush.Parse(textPrimary);
             Application.Current.Resources["TextSecondaryBrush"] = SolidColorBrush.Parse(textSecondary);
        }
    }

    private void ApplyAccentColor(string hex)
    {
        if (Application.Current != null && Color.TryParse(hex, out var color))
        {
             Application.Current.Resources["AccentBlue"] = color;
             Application.Current.Resources["AccentBlueBrush"] = new SolidColorBrush(color);
             Application.Current.Resources["AccentColorBrush"] = new SolidColorBrush(color);
             Application.Current.Resources["AccentLowOpacityBrush"] = new SolidColorBrush(color) { Opacity = 0.15 };
        }
    }

    private void ApplyBackgroundColor(string hex)
    {
        try 
        {
            if (Application.Current != null && Color.TryParse(hex, out var color))
            {
                 Application.Current.Resources["CardBackgroundBrush"] = new SolidColorBrush(color);
            }
        }
        catch { /* Ignore invalid hex during typing */ }
    }

    private void SaveSettings()
    {
        var settings = _settingsService.Load();
        settings.Theme = IsDarkTheme ? "Dark" : "Light";
        settings.AccentColor = AccentColorHex;
        settings.UseWebMode = UseWebMode;
        settings.RemoveBgEngine = RemoveBgEngine;
        settings.RembgModel = string.IsNullOrWhiteSpace(RembgModel) ? "u2netp" : RembgModel.Trim();
        settings.RembgExecutablePath = string.IsNullOrWhiteSpace(RembgExecutablePath) ? null : RembgExecutablePath.Trim();
        settings.MixProxyEnabled = MixProxyEnabled;
        settings.MixProxyList = MixProxyList;
        settings.ShowBrowser = ShowBrowser;
        settings.UseGpuForRembg = UseGpuForRembg;
        settings.CustomDarkBackground = _customDarkBackground;
        settings.CustomLightBackground = _customLightBackground;

        settings.AlphaMattingEnabled = AlphaMattingEnabled;
        settings.AlphaMattingErodeSize = AlphaMattingErodeSize;
        settings.AlphaMattingForegroundThreshold = AlphaMattingForegroundThreshold;
        settings.AlphaMattingBackgroundThreshold = AlphaMattingBackgroundThreshold;

        _settingsService.Save(settings);
    }

    private void MarkSettingsDirty()
    {
        if (_isRevertingSettings) return;
        if (!IsSettingsOpen) return;

        if (!_hasSettingsSnapshot)
        {
            IsSettingsDirty = true;
            return;
        }

        IsSettingsDirty =
            IsDarkTheme != _snapshotIsDarkTheme ||
            !string.Equals(AccentColorHex ?? "", _snapshotAccentColorHex, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(CurrentBackgroundColorHex ?? "", _snapshotBackgroundHex, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RemoveBgEngine ?? "", _snapshotRemoveBgEngine, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RembgModel ?? "", _snapshotRembgModel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RembgExecutablePath ?? "", _snapshotRembgExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            MixProxyEnabled != _snapshotMixProxyEnabled ||
            !string.Equals(MixProxyList ?? "", _snapshotMixProxyList, StringComparison.OrdinalIgnoreCase) ||
            ShowBrowser != _snapshotShowBrowser ||
            UseGpuForRembg != _snapshotUseGpuForRembg ||
            AlphaMattingEnabled != _snapshotAlphaMattingEnabled ||
            AlphaMattingErodeSize != _snapshotAlphaMattingErodeSize ||
            AlphaMattingForegroundThreshold != _snapshotAlphaMattingForegroundThreshold ||
            AlphaMattingBackgroundThreshold != _snapshotAlphaMattingBackgroundThreshold;
    }

    private void TakeSettingsSnapshot()
    {
        _hasSettingsSnapshot = true;
        _snapshotIsDarkTheme = IsDarkTheme;
        _snapshotAccentColorHex = AccentColorHex ?? "";
        _snapshotBackgroundHex = CurrentBackgroundColorHex ?? "";
        _snapshotRemoveBgEngine = RemoveBgEngine ?? "PIXA";
        _snapshotRembgModel = RembgModel ?? "u2netp";
        _snapshotRembgExecutablePath = RembgExecutablePath ?? "";
        _snapshotMixProxyEnabled = MixProxyEnabled;
        _snapshotMixProxyList = MixProxyList ?? "";
        _snapshotShowBrowser = ShowBrowser;
        _snapshotUseGpuForRembg = UseGpuForRembg;
        _snapshotAlphaMattingEnabled = AlphaMattingEnabled;
        _snapshotAlphaMattingErodeSize = AlphaMattingErodeSize;
        _snapshotAlphaMattingForegroundThreshold = AlphaMattingForegroundThreshold;
        _snapshotAlphaMattingBackgroundThreshold = AlphaMattingBackgroundThreshold;
    }

    private void RevertSettingsToSnapshot()
    {
        _isRevertingSettings = true;
        try
        {
            IsDarkTheme = _snapshotIsDarkTheme;
            AccentColorHex = _snapshotAccentColorHex;
            CurrentBackgroundColorHex = _snapshotBackgroundHex;
            RemoveBgEngine = _snapshotRemoveBgEngine;
            RembgModel = _snapshotRembgModel;
            RembgExecutablePath = _snapshotRembgExecutablePath;
            MixProxyEnabled = _snapshotMixProxyEnabled;
            MixProxyList = _snapshotMixProxyList;
            ShowBrowser = _snapshotShowBrowser;
            UseGpuForRembg = _snapshotUseGpuForRembg;
            AlphaMattingEnabled = _snapshotAlphaMattingEnabled;
            AlphaMattingErodeSize = _snapshotAlphaMattingErodeSize;
            AlphaMattingForegroundThreshold = _snapshotAlphaMattingForegroundThreshold;
            AlphaMattingBackgroundThreshold = _snapshotAlphaMattingBackgroundThreshold;
        }
        finally { _isRevertingSettings = false; }

        if (!string.IsNullOrEmpty(_snapshotBackgroundHex))
            ApplyBackgroundColor(_snapshotBackgroundHex);
        else
            ApplyTheme(IsDarkTheme);
    }

    public IBrush TrashButtonBrush => HasFiles ? SolidColorBrush.Parse("#EF4444") : new SolidColorBrush(AccentColor);

    [RelayCommand]
    private async Task DropFiles(string[] paths)
    {
        IsProcessing = true;
        try
        {
            var allowedToScan = new List<string>();
            var pendingConfirm = new List<string>();

            foreach (var path in paths)
            {
                if (IsPathInAllowedCache(path))
                {
                    allowedToScan.Add(path);
                    continue;
                }

                if (IsAlreadyInsidePilihanFolder(path))
                {
                    allowedToScan.Add(path);
                    continue;
                }

                if (Directory.Exists(path))
                {
                    var folderName = Path.GetFileName(path);
                    if (IsPilihanFolderMatch(folderName))
                    {
                        allowedToScan.Add(path);
                        continue;
                    }

                    string? redirectedPath = null;
                    try
                    {
                        var subdirs = Directory.GetDirectories(path);
                        foreach (var subdir in subdirs)
                        {
                            var subName = Path.GetFileName(subdir);
                            if (IsPilihanFolderMatch(subName))
                            {
                                redirectedPath = subdir;
                                break;
                            }
                        }
                    }
                    catch { }

                    if (redirectedPath != null)
                    {
                        allowedToScan.Add(redirectedPath);
                        continue;
                    }
                }

                pendingConfirm.Add(path);
            }

            if (pendingConfirm.Any())
            {
                _pendingPaths = pendingConfirm.ToArray();
                string displayName = pendingConfirm.Count == 1 
                    ? Path.GetFileName(pendingConfirm[0]) 
                    : $"{pendingConfirm.Count} item";
                ConfirmImportMessage = $"Folder atau file '{displayName}' tidak berada di dalam folder PILIHAN. Apakah Anda ingin mengizinkannya?";
                IsConfirmImportOpen = true;
            }

            if (allowedToScan.Any())
            {
                await ScanAndAddPathsAsync(allowedToScan);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error drop: {ex.Message}");
        }
        finally
        {
            IsProcessing = _cts != null;
            CheckRetryVisibility();
        }
    }

    private async Task ScanAndAddPathsAsync(IEnumerable<string> paths)
    {
        await Task.Run(() =>
        {
            var validPaths = new List<string>();
            var searchPattern = new HashSet<string> { ".jpg", ".jpeg", ".psd", ".webp" };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path).ToLower();

                    // --- REDIRECT SMALL PNG TO JPG SOURCE ---
                    if (ext == ".png")
                    {
                        try
                        {
                            if (new FileInfo(path).Length < 1024)
                            {
                                var jpg = Path.ChangeExtension(path, ".jpg");
                                if (File.Exists(jpg)) { validPaths.Add(jpg); continue; }
                                var jpeg = Path.ChangeExtension(path, ".jpeg");
                                if (File.Exists(jpeg)) { validPaths.Add(jpeg); continue; }
                            }
                        }
                        catch { }
                    }
                    // ----------------------------------------

                    if (searchPattern.Contains(ext)) validPaths.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    validPaths.AddRange(SafeGetFiles(path, searchPattern));
                }
            }

            if (validPaths.Any())
            {
                Dispatcher.UIThread.Post(() =>
                {
                    int skipped = 0;
                    foreach (var p in validPaths)
                    {
                        if (!Files.Any(f => f.FilePath == p))
                        {
                            Files.Add(new PixelcutFileItem(p));
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    SortFilesByName();

                    if (skipped > 0)
                    {
                        SkippedCount += skipped;
                        AppendLog($"Skipped {skipped} duplicates");
                    }

                    HasFiles = Files.Count > 0;
                });
            }
        });
    }

    private void SortFilesByName()
    {
        try
        {
            var sorted = Files.OrderBy(x => x.FileName, new NaturalStringComparer()).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var oldIdx = Files.IndexOf(sorted[i]);
                if (oldIdx != i)
                {
                    Files.Move(oldIdx, i);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error sorting files: {ex.Message}");
        }
    }

    private class NaturalStringComparer : System.Collections.Generic.IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string x, string y);

        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    return StrCmpLogicalW(x, y);
                }
            }
            catch { }
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<string> SafeGetFiles(string rootPath, HashSet<string> extensions)
    {
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (extensions.Contains(ext)) result.Add(file);
                }
                foreach (var subDir in Directory.GetDirectories(dir)) stack.Push(subDir);
            }
            catch {}
        }
        return result;
    }

    [RelayCommand]
    private void RemoveFile(PixelcutFileItem item)
    {
        Files.Remove(item);
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }

    private PixelcutFileItem? _lastSelectedItem;

    [RelayCommand]
    private void ToggleSelection(PixelcutFileItem item)
    {
        if (IsProcessing) return;
        
        item.IsSelected = !item.IsSelected;
        _lastSelectedItem = item;
    }

    public void SelectRange(PixelcutFileItem item)
    {
        if (IsProcessing) return;
        if (_lastSelectedItem == null || !Files.Contains(_lastSelectedItem))
        {
            ToggleSelection(item);
            return;
        }

        var idx1 = Files.IndexOf(_lastSelectedItem);
        var idx2 = Files.IndexOf(item);
        
        var start = Math.Min(idx1, idx2);
        var end = Math.Max(idx1, idx2);
        
        // Define target state based on the clicked item's new state (inverse of current, or just force true?)
        // Standard range select usually keeps the state consistent?
        // Let's assume we want to SELECT all
        
        for (int i = start; i <= end; i++)
        {
            Files[i].IsSelected = true;
        }
        
        _lastSelectedItem = item;
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsProcessing) return;
        
        var selected = Files.Where(x => x.IsSelected).ToList();
        if (selected.Count > 0)
        {
            foreach (var item in selected)
            {
                Files.Remove(item);
            }
        }
        else
        {
            Files.Clear();
        }
        
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }
    
    [ObservableProperty] private bool _isRetryVisible;

    private void CheckRetryVisibility()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsProcessing) 
            {
                IsRetryVisible = false;
                return;
            }
            // Check for failed items OR items that finished but are too small (likely error json)
            // Threshold increased to 500 bytes to be safe (User reported 59b)
            IsRetryVisible = Files.Any(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 500));
        });
    }

    private string _lastJobType = "remove_bg";

    [RelayCommand]
    private async Task ProcessRemoveBg() => await ProcessQueue("remove_bg");

    [RelayCommand]
    private void Stop()
    {
        AppendLog("Menghentikan proses...");
        _stopRequested = true;
        _cts?.Cancel();
    }
    
    [RelayCommand]
    private void Pause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private async Task RetrySmallFiles()
    {
        if (IsProcessing) return;
        var toRetry = Files.Where(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 100)).ToList();
        
        if (toRetry.Count == 0) return;
        
        foreach (var item in toRetry)
        {
            item.Status = ""; // Clear status text
            item.IsDone = false;
            item.IsFailed = false;
            item.Progress = 0;
            item.ErrorMessage = "";
        }

        await ProcessQueue(_lastJobType);
    }
    
    // Settings
    [ObservableProperty] private bool _isSettingsOpen;
    [RelayCommand]
    private void ShowSettings()
    {
        if (!IsSettingsOpen)
        {
            TakeSettingsSnapshot();
            IsSettingsDirty = false;
            IsSettingsOpen = true;
        }
        else
        {
            CloseSettings();
        }
    }

    [RelayCommand]
    private void CloseSettings()
    {
        // Close without saving => revert changes if dirty
        if (IsSettingsDirty && _hasSettingsSnapshot)
        {
            RevertSettingsToSnapshot();
        }

        IsSettingsDirty = false;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void SaveSettingsAndClose()
    {
        if (!IsSettingsDirty)
        {
            IsSettingsOpen = false;
            return;
        }

        SaveSettings();
        TakeSettingsSnapshot(); // new baseline
        IsSettingsDirty = false;
        IsSettingsOpen = false;
    }
    [RelayCommand] private void UpdateAccentColor(string hex) => AccentColorHex = hex;

    private static string NormalizeEngine(string? value)
    {
        if (string.Equals(value, "REMBG", StringComparison.OrdinalIgnoreCase))
            return "REMBG";
        if (string.Equals(value, "NOBG_SPACE", StringComparison.OrdinalIgnoreCase))
            return "NOBG_SPACE";
        if (string.Equals(value, "REMBG_ONLINE", StringComparison.OrdinalIgnoreCase))
            return "REMBG_ONLINE";
        if (string.Equals(value, "BG_ERASER", StringComparison.OrdinalIgnoreCase))
            return "BG_ERASER";
        return "PIXA";
    }

    /// <summary>Fired saat item baru mulai diproses — View subscribe untuk auto-scroll ke item tersebut.</summary>
    public event Action<PixelcutFileItem>? ScrollToItemRequested;

    private async Task ProcessQueue(string job)
    {
        if (IsProcessing) return;
        _lastJobType = job;
        IsProcessing = true;
        _stopRequested = false;
        IsPaused = false;
        _cts = new CancellationTokenSource();
        var engineInfo = job == "remove_bg" ? $" [{RemoveBgEngine}]" : "";
        AppendLog($"Memulai proses {job}{engineInfo} (C# Native)...");

        try
        {
            while (!_stopRequested)
            {
                if (IsPaused) { await Task.Delay(500); continue; }

                var item = Files.FirstOrDefault(x => !x.IsDone && !x.IsFailed && !x.IsProcessing);
                if (item == null) break;

                await ProcessItem(item, job, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(TabHeaderText));
            CheckRetryVisibility();

            if (!_stopRequested)
            {
                var success = Files.Count(x => x.IsDone && x.ResultSize >= 500);
                var small = Files.Count(x => x.IsDone && x.ResultSize < 500 && x.ResultSize > 0);
                var failed = Files.Count(x => x.IsFailed);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Proses Selesai!");
                sb.AppendLine();
                sb.AppendLine($"✅ Berhasil: {success}");
                if (small > 0) sb.AppendLine($"⚠️ File Kecil (<500b): {small}");
                if (failed > 0) sb.AppendLine($"❌ Gagal: {failed}");

                AlertMessage = sb.ToString().Trim();
                IsAlertOpen = true;
            }
        }
    }

    private async Task ProcessItem(PixelcutFileItem item, string job, CancellationToken ct)
    {
        // Auto-scroll to this item in the file list
        ScrollToItemRequested?.Invoke(item);

        item.Status = "";
        item.IsProcessing = true;
        item.Progress = 0;
        item.IsFailed = false;

        // --- SKIP LOGIC ---
        var expectedPath = GetResultPath(item.FilePath, job);
        
        // Ensure we are not skipping if input is same as output (e.g. PNG input)
        bool isSameFile = string.Equals(item.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase);

        if (!isSameFile && File.Exists(expectedPath))
        {
            var info = new FileInfo(expectedPath);
            // Only skip if file is valid/large enough (> 1KB)
            // If < 1KB, we assume it's corrupt or failed, so we re-process.
            if (info.Length >= 1024)
            {
                item.ResultPath = expectedPath;
                item.ResultSize = info.Length;
                item.Status = "Selesai (Skipped)";
                item.IsDone = true;
                item.Progress = 100;
                item.IsProcessing = false;
                return;
            }
        }
        // ------------------

        try
        {
            // Simulate progress for UI feedback
            var progressTask = Task.Run(async () => 
            {
                while(item.IsProcessing && item.Progress < 90)
                {
                    if (ct.IsCancellationRequested) break;
                    
                    if (IsPaused) 
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    await Task.Delay(100);
                    Dispatcher.UIThread.Post(() =>
                    {
                        item.Progress += 2;
                        item.Status = item.Progress < 30 ? "Mengunggah..." : 
                                      item.Progress < 60 ? "Memproses..." : "Mengunduh hasil...";
                    });
                }
            }, ct);

            // C# Service ONLY
            await _pixelcutService.ProcessImageAsync(item, job, ct);

            // If Paused, wait here before marking complete
            while (IsPaused)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(500, ct); 
            }

            item.Progress = 100;
            item.Status = "Selesai";
            item.IsDone = true;
            OnPropertyChanged(nameof(ProcessedCount));
            OnPropertyChanged(nameof(TabHeaderText));
            
            // Re-read size
            var resultPath = GetResultPath(item.FilePath, job);
            if (File.Exists(resultPath))
            {
                item.ResultPath = resultPath;
                item.ResultSize = new FileInfo(resultPath).Length;
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = "Berhenti";
            item.IsFailed = true; 
            item.ErrorMessage = "Dibatalkan";
            item.Progress = 0;
            throw; // Rethrow to stop loop in ProcessQueue
        }
        catch (Exception ex)
        {
            item.Status = "Gagal";
            item.IsFailed = true;
            item.ErrorMessage = ex.Message;
            item.Progress = 0;
            AppendLog($"Error {item.FileName}: {ex.Message}");
            OnPropertyChanged(nameof(ProcessedCount));
            OnPropertyChanged(nameof(TabHeaderText));
        }
        finally
        {
            item.IsProcessing = false;
        }
    }
    
    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale") 
        {
             // Keep original extension for upscale
             var ext = Path.GetExtension(input);
             return Path.Combine(dir, $"{name}_up{ext}");
        }
        return Path.Combine(dir, $"{name}.png");
    }

    [RelayCommand]
    private void ToggleLog()
    {
        // Deprecated by Tab UI
        // ShowLogPanel = !ShowLogPanel;
    }

    private void AppendLog(string message, string level = "INFO")
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        LogOutput += $"{msg}\n";
        Console.WriteLine($"[PixelcutCompact] {msg}");
    }
    

    [RelayCommand]
    private void ResetBrowser()
    {
        _pixelcutService.ResetWebAutomation();
        AppendLog("Browser direset — sesi baru akan dibuat saat proses berikutnya.");
        ShowToast("Browser direset ✓", "🔄");
    }


    private void ShowToast(string message, string icon = "✅")
    {
        Dispatcher.UIThread.Post(() =>
        {
            ToastMessage = message;
            ToastIcon = icon;
            IsToastVisible = true;
            
            _toastTimer?.Stop();
            _toastTimer?.Dispose();
            _toastTimer = new System.Timers.Timer(4000);
            _toastTimer.AutoReset = false;
            _toastTimer.Elapsed += (s, e) =>
            {
                Dispatcher.UIThread.Post(() => IsToastVisible = false);
            };
            _toastTimer.Start();
        });
    }

    [RelayCommand]
    private void DismissToast() => IsToastVisible = false;
    
    // === NEW FEATURES ===
    
    [ObservableProperty] private bool _isGridView;
    [RelayCommand] private void ToggleViewMode() => IsGridView = !IsGridView;
    
    [RelayCommand]
    private void OpenFolder(PixelcutFileItem item)
    {
        if (item == null) return;
        var path = item.HasResult ? item.ResultPath : item.FilePath;
        if (File.Exists(path))
        {
            RevealFileInExplorer(path);
        }
    }

    private void RevealFileInExplorer(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{filePath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
    
    [RelayCommand]
    private async Task RetrySingleItem(PixelcutFileItem item)
    {
        if (IsProcessing || item == null) return;
        
        item.Status = "";
        item.IsDone = false;
        item.IsFailed = false;
        item.Progress = 0;
        item.ErrorMessage = "";
        
        // Single item process wrapper
        IsProcessing = true;
        _stopRequested = false;
        IsPaused = false;
        _cts = new CancellationTokenSource();
        
        try
        {
            await ProcessItem(item, _lastJobType, _cts.Token);
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            CheckRetryVisibility();
        }
    }

    private PreviewWindow? _previewWindow;
    private PixelcutFileItem? _currentPreviewItem;
    
    [RelayCommand]
    private void PreviewItem(PixelcutFileItem item)
    {
        if (item == null) return;
        _currentPreviewItem = item;
        
        var original = item.FilePath;
        var result = item.HasResult ? item.ResultPath : null;
        
        // Construct title: ParentFolder\Filename.ext
        var parent = Path.GetFileName(Path.GetDirectoryName(original));
        var fname = Path.GetFileName(original);
        var title = string.IsNullOrEmpty(parent) ? fname : Path.Combine(parent, fname);
        
        if (File.Exists(original) && File.Exists(result))
        {
             // Hide gallery window if it is open
             if (_galleryWindow != null && _galleryWindow.IsVisible)
             {
                 _galleryWindow.Hide();
             }

             if (_previewWindow == null)
             {
                 _previewWindow = new PreviewWindow();
                 _previewWindow.Closed += (s, e) => 
                 { 
                     _previewWindow = null; 
                     var closedItem = _currentPreviewItem;
                     _currentPreviewItem = null; 
                     
                     if (closedItem != null)
                     {
                         RefreshItemThumbnails(closedItem);
                     }
                     
                     // Show gallery window if it was hidden
                     if (_galleryWindow != null && !_galleryWindow.IsVisible)
                     {
                         _galleryWindow.Show();
                     }
                 };
                 // Subscribe to events
                 _previewWindow.Next += OnNextPreview;
                 _previewWindow.Previous += OnPreviousPreview;

                 if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                 {
                     _previewWindow.Show(desktop.MainWindow);
                 }
                 else
                 {
                     _previewWindow.Show();
                 }
             }
             else
             {
                 _previewWindow.Activate();
             }
             
             _previewWindow.ShowLoading();
             _previewWindow.LoadImages(original, result, title);
             UpdatePreviewButtons();
        }
        else
        {
            // Fallback to Explorer
            var path = File.Exists(result) ? result : original;
            if (File.Exists(path))
            {
                  RevealFileInExplorer(path);
            }
        }
    }

    [RelayCommand]
    private void OpenFullPreviewWindow(PixelcutFileItem? item)
    {
        if (item == null) return;
        // Delegate to PreviewItem which has all the logic for preview window, Next/Previous events, etc.
        PreviewItem(item);
    }

    private GalleryWindow? _galleryWindow;

    [RelayCommand]
    private void OpenGallery()
    {
        // Show Window Immediately
        if (_galleryWindow == null)
        {
            _galleryWindow = new GalleryWindow();
            _galleryWindow.DataContext = this;
            _galleryWindow.Closed += (s, e) => _galleryWindow = null;
            
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                _galleryWindow.Show(desktop.MainWindow);
            }
            else
            {
                _galleryWindow.Show();
            }
        }
        else
        {
            if (!_galleryWindow.IsVisible)
            {
                _galleryWindow.Show();
            }
            _galleryWindow.Activate();
        }

        // Window will automatically pick up GalleryItems via DataBinding.
        // GalleryItems is now kept in sync automatically via OnFilesCollectionChanged 
        // and OnFileItemPropertyChanged.
    }

    [RelayCommand]
    private void TogglePreviewPane()
    {
        IsPreviewPaneVisible = !IsPreviewPaneVisible;
    }

    [RelayCommand]
    private async Task OpenSelectedInPhotoshop()
    {
        // Find ALL selected items with result
        var selectedItems = Files.Where(f => f.IsSelected && f.HasResult && File.Exists(f.ResultPath)).ToList();
        if (selectedItems.Count == 0)
        {
            AlertMessage = "Tidak ada item dipilih dengan hasil.";
            IsAlertOpen = true;
            return;
        }

        // Load settings to get Photoshop path
        var settings = Services.PreviewWindowSettings.Load();

        // Check if Photoshop path is set
        if (string.IsNullOrEmpty(settings.PhotoshopPath) || !File.Exists(settings.PhotoshopPath))
        {
            AlertMessage = "Path Photoshop belum diatur. Silakan buka Preview Window (klik ganda foto) dan pilih path Photoshop terlebih dahulu.";
            IsAlertOpen = true;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("#target photoshop");
        sb.AppendLine("function openPair(pngPath, jpgPath) {");
        sb.AppendLine("    try {");
        sb.AppendLine("        var pngFile = new File(pngPath);");
        sb.AppendLine("        if (pngFile.exists) {");
        sb.AppendLine("            var doc = app.open(pngFile);");
        sb.AppendLine("            if (doc.artLayers.length > 0) { doc.artLayers[0].name = 'Hasil (Masker Transparan)'; }");
        sb.AppendLine("            var jpgFile = new File(jpgPath);");
        sb.AppendLine("            if (jpgFile.exists) {");
        sb.AppendLine("                var jpgDoc = app.open(jpgFile);");
        sb.AppendLine("                jpgDoc.selection.selectAll();");
        sb.AppendLine("                jpgDoc.activeLayer.copy();");
        sb.AppendLine("                jpgDoc.close(SaveOptions.DONOTSAVECHANGES);");
        sb.AppendLine("                app.activeDocument = doc;");
        sb.AppendLine("                var pastedLayer = doc.paste();");
        sb.AppendLine("                pastedLayer.name = 'Referensi Asli (Original)';");
        sb.AppendLine("                pastedLayer.move(doc, ElementPlacement.PLACEATEND);");
        sb.AppendLine("                doc.activeLayer = doc.artLayers[0];");
        sb.AppendLine("            }");
        sb.AppendLine("        } else {");
        sb.AppendLine("            return 'File hasil tidak ditemukan: ' + pngPath;");
        sb.AppendLine("        }");
        sb.AppendLine("    } catch (e) {");
        sb.AppendLine("        return 'Gagal membuka/memproses file: ' + e.message;");
        sb.AppendLine("    }");
        sb.AppendLine("    return null;");
        sb.AppendLine("}");
        sb.AppendLine("var errors = [];");

        for (int i = 0; i < selectedItems.Count; i++)
        {
            var item = selectedItems[i];
            var escapedResult = item.ResultPath.Replace("\\", "\\\\").Replace("'", "\\'");
            var escapedOriginal = item.FilePath.Replace("\\", "\\\\").Replace("'", "\\'");
            
            sb.AppendLine($"var err{i} = openPair('{escapedResult}', '{escapedOriginal}');");
            sb.AppendLine($"if (err{i}) {{ errors.push('Gambar {i + 1} ({item.FileName}): ' + err{i}); }}");
        }

        sb.AppendLine("if (errors.length > 0) {");
        sb.AppendLine("    alert('Beberapa file gagal dibuka di Photoshop:\\n\\n' + errors.join('\\n'));");
        sb.AppendLine("}");

        // Generate unique name for the multi JSX file to avoid collisions
        var tempJsxPath = Path.Combine(Path.GetTempPath(), $"pixelcut_multi_open_{Guid.NewGuid().ToString("N").Substring(0, 8)}.jsx");

        try
        {
            await File.WriteAllTextAsync(tempJsxPath, sb.ToString());

            var psi = new ProcessStartInfo
            {
                FileName = settings.PhotoshopPath,
                Arguments = $"\"{tempJsxPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            
            AppendLog($"Membuka {selectedItems.Count} item di Photoshop...");
        }
        catch (Exception ex)
        {
            AppendLog($"Gagal menjalankan Photoshop: {ex.Message}");
            AlertMessage = $"Gagal menjalankan Photoshop: {ex.Message}. Pastikan file executable Photoshop Anda valid.";
            IsAlertOpen = true;

            // Reset path on failure so they can choose again
            settings.PhotoshopPath = "";
            settings.Save();
        }
    }

    private void RefreshItemThumbnails(PixelcutFileItem? item)
    {
        if (item == null) return;
        var itemsToRefresh = GalleryItems.Where(g => g.ParentItem == item).ToList();
        foreach (var gItem in itemsToRefresh)
        {
            gItem.RefreshThumbnail();
        }
    }

    private void OnNextPreview(object? sender, EventArgs e)
    {
        if (_currentPreviewItem == null) return;
        var oldItem = _currentPreviewItem;
        var idx = Files.IndexOf(_currentPreviewItem);
        if (idx >= 0 && idx < Files.Count - 1)
        {
            // Find next item with result
            for (int i = idx + 1; i < Files.Count; i++)
            {
                if (Files[i].HasResult)
                {
                    PreviewItem(Files[i]);
                    RefreshItemThumbnails(oldItem);
                    return;
                }
            }
        }
    }

    private void OnPreviousPreview(object? sender, EventArgs e)
    {
        if (_currentPreviewItem == null) return;
        var oldItem = _currentPreviewItem;
        var idx = Files.IndexOf(_currentPreviewItem);
        if (idx > 0)
        {
            // Find prev item with result
            for (int i = idx - 1; i >= 0; i--)
            {
                if (Files[i].HasResult)
                {
                    PreviewItem(Files[i]);
                    RefreshItemThumbnails(oldItem);
                    return;
                }
            }
        }
    }

    private void UpdatePreviewButtons()
    {
        // We could enable/disable buttons in preview window here if we were binding properties,
        // but for now the events just won't find a next item.
        // If we want to strictly disable buttons, we'd need to expose properties on PreviewWindow.
    }

}

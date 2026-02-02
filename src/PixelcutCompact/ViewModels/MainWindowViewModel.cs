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
using Avalonia.Threading;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PixelcutCompact.Views;

namespace PixelcutCompact.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PixelcutService _pixelcutService = new();
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _cts;
    
    [ObservableProperty] private ObservableCollection<PixelcutFileItem> _files = new();
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _vpnStatus = "Memeriksa...";
    [ObservableProperty] private bool _isVpnActive;
    [ObservableProperty] private string _logOutput = "";
    [ObservableProperty] private bool _showLogPanel;
    [ObservableProperty] private string _statusText = "Siap";
    [ObservableProperty] private int _skippedCount;
    
    // Settings
    [ObservableProperty] private string _proxyAddress = "";
    [ObservableProperty] private bool _isDarkTheme; // Mapped to Theme
    [ObservableProperty] private string _accentColorHex = "#3b82f6";
    
    // We bind the UI to this property. When user edits this, we verify which mode we are in and save to the correct field.
    [ObservableProperty] private string _currentBackgroundColorHex = ""; 
    
    // Alert Overlay
    [ObservableProperty] private bool _isAlertOpen;
    [ObservableProperty] private string _alertMessage = "";
    [RelayCommand] private void CloseAlert() => IsAlertOpen = false;

    
    private string? _customDarkBackground;
    private string? _customLightBackground;

    private bool _stopRequested;
    [ObservableProperty] private bool _isPaused;
    private System.Timers.Timer? _vpnCheckTimer;

    public MainWindowViewModel()
    {
        // Load Settings
        var settings = _settingsService.Load();
        ProxyAddress = settings.ProxyAddress ?? "";
        AccentColorHex = settings.AccentColor;
        IsDarkTheme = settings.Theme == "Dark";
        
        _customDarkBackground = settings.CustomDarkBackground;
        _customLightBackground = settings.CustomLightBackground;

        ApplyTheme(IsDarkTheme);
        ApplyAccentColor(AccentColorHex);

        // Initialize Service
        Task.Run(async () => await _pixelcutService.InitializeAsync());
        
        CheckVpnStatus();
        
        // Start periodic VPN check (every 3 seconds)
        _vpnCheckTimer = new System.Timers.Timer(3000);
        _vpnCheckTimer.Elapsed += (s, e) => 
        {
            Dispatcher.UIThread.Post(() => CheckVpnStatus());
        };
        _vpnCheckTimer.Start();
    }
    
    partial void OnProxyAddressChanged(string value)
    {
        _pixelcutService.ManualProxy = string.IsNullOrWhiteSpace(value) ? null : value;
        CheckVpnStatus();
        SaveSettings();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplyTheme(value);
        SaveSettings();
    }

    partial void OnAccentColorHexChanged(string value)
    {
        ApplyAccentColor(value);
        OnPropertyChanged(nameof(AccentColor)); // Notify UI
        OnPropertyChanged(nameof(TrashButtonBrush));
        SaveSettings();
    }

    partial void OnHasFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(TrashButtonBrush));
    }

    partial void OnIsProcessingChanged(bool value)
    {
        StatusText = value ? "Memproses..." : "Siap";
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
        SaveSettings();
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
        settings.ProxyAddress = ProxyAddress;
        settings.Theme = IsDarkTheme ? "Dark" : "Light";
        settings.AccentColor = AccentColorHex;
        settings.CustomDarkBackground = _customDarkBackground;
        settings.CustomLightBackground = _customLightBackground;
        _settingsService.Save(settings);
    }

    private void CheckVpnStatus()
    {
        // 1. Check Manual Proxy
        if (!string.IsNullOrEmpty(ProxyAddress))
        {
            IsVpnActive = true;
            VpnStatus = "Proxy Manual Aktif";
            return;
        }

        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var vpnKeywords = new[] { "vpn", "tun", "tap", "ppp", "wintun", "wireguard", "openvpn", "avira", "nordvpn", "expressvpn" };
            
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                    
                var name = iface.Name.ToLower();
                var desc = iface.Description.ToLower();
                
                if (vpnKeywords.Any(kw => name.Contains(kw) || desc.Contains(kw)))
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }
            }
            
            IsVpnActive = false;
            VpnStatus = "Koneksi Langsung (Tanpa VPN)";
        }
        catch
        {
            IsVpnActive = false;
            VpnStatus = "Koneksi Langsung";
        }
    }

    // Property for Status Dot Color (Red=Direct, Green=VPN/Proxy)
    public IBrush ConnectionStatusBrush => IsVpnActive ? SolidColorBrush.Parse("#10B981") : SolidColorBrush.Parse("#EF4444");

    partial void OnIsVpnActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStatusBrush));
    }

    public IBrush TrashButtonBrush => HasFiles ? SolidColorBrush.Parse("#EF4444") : new SolidColorBrush(AccentColor);

    [RelayCommand]
    private async Task DropFiles(string[] paths)
    {
        IsProcessing = true;
        try
        {
            await Task.Run(() =>
            {
                var validPaths = new List<string>();
                // Removed .png from allowed extensions as per request (Only accepted via Smart Skip logic)
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
                                    // Check for source JPG
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
        catch (Exception ex)
        {
             AppendLog($"Error drop: {ex.Message}");
        }
        finally
        {
            IsProcessing = false; 
            CheckRetryVisibility();
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
    private async Task ProcessUpscale() => await ProcessQueue("upscale");
    
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
    [RelayCommand] private void ShowSettings() => IsSettingsOpen = !IsSettingsOpen;
    [RelayCommand] private void CloseSettings() => IsSettingsOpen = false;
    [RelayCommand] private void UpdateAccentColor(string hex) => AccentColorHex = hex;

    private async Task ProcessQueue(string job)
    {
        if (IsProcessing) return;
        _lastJobType = job;
        IsProcessing = true;
        _stopRequested = false;
        IsPaused = false;
        _cts = new CancellationTokenSource();
        
        try
        {
            // Simple loop
            while (!_stopRequested)
            {
                if (IsPaused) { await Task.Delay(500); continue; }

                // Find first item that is not Done and not Failed (Wait state)
                // Since we clear status for retries, check flags
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
        // Don't set text "Memproses..."
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
                    Dispatcher.UIThread.Post(() => item.Progress += 2);
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
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
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
             if (_previewWindow == null)
             {
                 _previewWindow = new PreviewWindow();
                 _previewWindow.Closed += (s, e) => { _previewWindow = null; _currentPreviewItem = null; };
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
             
             _previewWindow.LoadImages(original, result, title);
             UpdatePreviewButtons();
        }
        else
        {
            // Fallback to Explorer
            var path = File.Exists(result) ? result : original;
            if (File.Exists(path))
            {
                 new Process { StartInfo = new ProcessStartInfo(path) { UseShellExecute = true } }.Start();
            }
        }
    }

    private void OnNextPreview(object? sender, EventArgs e)
    {
        if (_currentPreviewItem == null) return;
        var idx = Files.IndexOf(_currentPreviewItem);
        if (idx >= 0 && idx < Files.Count - 1)
        {
            // Find next item with result
            for (int i = idx + 1; i < Files.Count; i++)
            {
                if (Files[i].HasResult)
                {
                    PreviewItem(Files[i]);
                    return;
                }
            }
        }
    }

    private void OnPreviousPreview(object? sender, EventArgs e)
    {
        if (_currentPreviewItem == null) return;
        var idx = Files.IndexOf(_currentPreviewItem);
        if (idx > 0)
        {
            // Find prev item with result
            for (int i = idx - 1; i >= 0; i--)
            {
                if (Files[i].HasResult)
                {
                    PreviewItem(Files[i]);
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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;
using System.Diagnostics;
using BMachine.UI.Models;
using BMachine.SDK;
using BMachine.UI.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BMachine.UI.Services;
using Avalonia;
using System.Threading;

namespace BMachine.UI.ViewModels;

public partial class PixelcutViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    private readonly PixelcutService _pixelcutService;
    
    [ObservableProperty] private ObservableCollection<PixelcutFileItem> _files = new();
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _vpnStatus = "Memeriksa...";
    [ObservableProperty] private bool _isVpnActive;
    [ObservableProperty] private string _logOutput = "";
    [ObservableProperty] private bool _showLogPanel;
    [ObservableProperty] private bool _isLogViewVisible;
    
    // Toast Notification
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastIcon = "✅";
    private System.Timers.Timer? _toastTimer;
    
    // Settings
    [ObservableProperty] private string _proxyAddress = "";
    [ObservableProperty] private int _skippedCount;
    
    // OVPN Configurations
    [ObservableProperty] private string _ovpnPath = "";
    [ObservableProperty] private string _ovpnUsername = "";
    [ObservableProperty] private string _ovpnPassword = "";
    [ObservableProperty] private string _ovpnConfigStatus = "";
    [ObservableProperty] private string _ovpnConfigColor = "Gray";
    [ObservableProperty] private bool _isOvpnConfigured;
    
    private bool _stopRequested;
    private CancellationTokenSource? _cts;
    [ObservableProperty] private bool _isPaused;
    private System.Timers.Timer? _vpnCheckTimer;
    
    private Process? _vpnProcess;

    public PixelcutViewModel(IDatabase? database)
    {
        _database = database;
        _pixelcutService = new PixelcutService();
        CheckVpnStatus();
        LoadSettings();
        
        // Start periodic VPN check (every 3 seconds)
        _vpnCheckTimer = new System.Timers.Timer(3000);
        _vpnCheckTimer.Elapsed += (s, e) => 
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckVpnStatus());
        };
        _vpnCheckTimer.Start();
    }
    
    private async void LoadSettings()
    {
        if (_database != null)
        {
            ProxyAddress = await _database.GetAsync<string>("Configs.Pixelcut.Proxy") ?? "";
            OvpnPath = await _database.GetAsync<string>("Configs.Pixelcut.OvpnPath") ?? "";
            OvpnUsername = await _database.GetAsync<string>("Configs.Pixelcut.OvpnUsername") ?? "";
            OvpnPassword = await _database.GetAsync<string>("Configs.Pixelcut.OvpnPassword") ?? "";
            _pixelcutService.ManualProxy = ProxyAddress;
            UpdateOvpnConfigStatus();
        }
    }

    [RelayCommand]
    private async Task PickOvpnFile()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pilih Profil OpenVPN (.ovpn)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("OpenVPN Config") { Patterns = new[] { "*.ovpn", "*.conf" } }
                }
            });

            if (files != null && files.Count > 0)
            {
                OvpnPath = files[0].Path.LocalPath;
            }
        }
    }

    partial void OnProxyAddressChanged(string value)
    {
        _pixelcutService.ManualProxy = value;
        CheckVpnStatus();
    }

    partial void OnOvpnPathChanged(string value) => UpdateOvpnConfigStatus();
    partial void OnOvpnUsernameChanged(string value) => UpdateOvpnConfigStatus();
    partial void OnOvpnPasswordChanged(string value) => UpdateOvpnConfigStatus();

    private void UpdateOvpnConfigStatus()
    {
        bool hasPath = !string.IsNullOrEmpty(OvpnPath);
        bool pathExists = hasPath && File.Exists(OvpnPath);
        bool hasUser = !string.IsNullOrEmpty(OvpnUsername);
        bool hasPass = !string.IsNullOrEmpty(OvpnPassword);

        if (!hasPath && !hasUser && !hasPass)
        {
            OvpnConfigStatus = "";
            OvpnConfigColor = "Gray";
            IsOvpnConfigured = false;
        }
        else if (hasPath && !pathExists)
        {
            OvpnConfigStatus = "⚠️ File .ovpn tidak ditemukan";
            OvpnConfigColor = "#EF4444";
            IsOvpnConfigured = false;
        }
        else if (!hasPath || !hasUser || !hasPass)
        {
            OvpnConfigStatus = "⚠️ Lengkapi semua kolom untuk mengaktifkan VPN";
            OvpnConfigColor = "#F59E0B";
            IsOvpnConfigured = false;
        }
        else
        {
            OvpnConfigStatus = "✅ Konfigurasi lengkap — VPN akan aktif saat proses dimulai";
            OvpnConfigColor = "#16A34A"; // Darker green for legibility on both Light and Dark mode
            IsOvpnConfigured = true;
        }

        CheckVpnStatus();
    }

    partial void OnIsProcessingChanged(bool value)
    {
        CheckRetryVisibility();
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
            // 2. Check for VPN by examining network interfaces
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var vpnKeywords = new[] { "vpn", "tap", "ppp", "wintun", "wireguard", "openvpn", "avira", "nordvpn", "expressvpn" };
            
            // macOS system interfaces to EXCLUDE (iCloud Private Relay, etc.)
            var macSystemInterfaces = new[] { "utun", "llw", "awdl", "bridge", "ap", "anpi", "gif", "stf", "ipsec" };
            
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                    
                var name = iface.Name.ToLower();
                var desc = iface.Description.ToLower();
                
                // Skip known macOS system interfaces that are NOT real VPNs
                if (OperatingSystem.IsMacOS() && macSystemInterfaces.Any(si => name.StartsWith(si)))
                    continue;
                
                if (vpnKeywords.Any(kw => name.Contains(kw) || desc.Contains(kw)))
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }
                
                // Also detect tun/tap but only real tun devices (e.g., tun0) not utun
                if (name == "tun0" || name == "tun1" || name == "tap0" || name == "tap1")
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }
            }
            
            // 3. Check if OVPN is configured (ready but not connected yet)
            if (!string.IsNullOrEmpty(OvpnPath) && File.Exists(OvpnPath) 
                && !string.IsNullOrEmpty(OvpnUsername) && !string.IsNullOrEmpty(OvpnPassword))
            {
                IsVpnActive = false;
                VpnStatus = "OVPN Siap (Akan aktif saat proses)";
                return;
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

    [RelayCommand]
    private async Task DropFiles(string[] paths)
    {
        IsProcessing = true;
        try
        {
            await Task.Run(() =>
            {
                var validPaths = new System.Collections.Generic.List<string>();
                var searchPattern = new System.Collections.Generic.HashSet<string> { ".jpg", ".jpeg", ".psd", ".webp" };

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
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        int skipped = 0;
                        foreach (var p in validPaths)
                        {
                            try
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
                            catch { }
                        }
                        
                        if (skipped > 0)
                        {
                            SkippedCount += skipped;
                            AppendLog($"Skipped {skipped} duplicates");
                        }

                        HasFiles = Files.Count > 0;
                        CheckRetryVisibility();
                    });
                }
            });
        }
        catch (Exception ex)
        {
             Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendLog($"Error processing drop: {ex.Message}"));
        }
        finally
        {
            IsProcessing = false; 
            Avalonia.Threading.Dispatcher.UIThread.Post(CheckRetryVisibility);
        }
    }

    private System.Collections.Generic.IEnumerable<string> SafeGetFiles(string rootPath, System.Collections.Generic.HashSet<string> extensions)
    {
        var result = new System.Collections.Generic.List<string>();
        var stack = new System.Collections.Generic.Stack<string>();
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

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    stack.Push(subDir);
                }
            }
            catch (Exception) { }
        }
        return result;
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
        
        for (int i = start; i <= end; i++) Files[i].IsSelected = true;
        _lastSelectedItem = item;
    }

    [RelayCommand]
    private void RemoveFile(PixelcutFileItem item)
    {
        if (Files.Contains(item)) Files.Remove(item);
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsProcessing) return;
        
        if (Files.Any(f => f.IsSelected))
            RemoveSelectedFiles();
        else
        {
            Files.Clear();
            HasFiles = false;
        }
        CheckRetryVisibility();
    }

    [ObservableProperty] private bool _isRetryVisible;

    private void CheckRetryVisibility()
    {
        if (IsProcessing)
        {
            IsRetryVisible = false;
            return;
        }
        IsRetryVisible = Files.Any(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 100));
    }

    [RelayCommand]
    private async Task ProcessRemoveBg() => await ProcessQueue("remove_bg");

    [RelayCommand]
    private async Task ProcessUpscale() => await ProcessQueue("upscale");
    
    [RelayCommand]
    private void Stop()
    {
        _stopRequested = true;
        _cts?.Cancel();
        AppendLog("Stop diminta...");
        StopVpnProcess();
    }
    
    [RelayCommand]
    private void Pause()
    {
        IsPaused = !IsPaused;
        AppendLog(IsPaused ? "Dijeda..." : "Melanjutkan...");
    }

    [RelayCommand]
    private void ToggleLog() => ShowLogPanel = !ShowLogPanel;

    [RelayCommand]
    private void RemoveSelectedFiles()
    {
        var selected = Files.Where(x => x.IsSelected).ToList();
        foreach (var item in selected) Files.Remove(item);
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }

    [RelayCommand]
    private async Task RetrySmallFiles()
    {
        if (IsProcessing) return;
        var toRetry = Files.Where(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 100)).ToList();
        if (toRetry.Count == 0) return;

        AppendLog($"Mengulangi {toRetry.Count} file gagal/corrupt...");
        foreach (var item in toRetry)
        {
            item.Status = "Menunggu";
            item.IsDone = false;
            item.IsFailed = false;
            item.Progress = 0;
            item.ErrorMessage = "";
        }
        await ProcessQueue(_lastJobType);
    }

    [ObservableProperty] private bool _showCompletionDialog;
    [ObservableProperty] private int _completionSuccessCount;
    [ObservableProperty] private int _completionFailureCount;

    [RelayCommand]
    private void CloseCompletionDialog() => ShowCompletionDialog = false;

    private string _lastJobType = "remove_bg";

    private async Task ProcessQueue(string job)
    {
        if (IsProcessing) return;
        _lastJobType = job;
        IsProcessing = true;
        _stopRequested = false;
        IsPaused = false;
        AppendLog($"Memulai proses {job} (C# Native)...");
        _cts = new CancellationTokenSource();

        bool hasVpn = !string.IsNullOrEmpty(OvpnPath) && File.Exists(OvpnPath)
                      && !string.IsNullOrEmpty(OvpnUsername) && !string.IsNullOrEmpty(OvpnPassword);
        if (hasVpn)
        {
            VpnStatus = "🔄 Menyambungkan OpenVPN...";
            IsVpnActive = false;
            AppendLog("Memulai jalur OpenVPN Split-Tunneling...");
            await StartVpnProcessAsync();
            AppendLog("Menunggu rute jaringan stabil (5 detik)...");
            await Task.Delay(5000); // Allow OS table routing to stabilize
            
            IsVpnActive = true;
            VpnStatus = "🟢 VPN Aktif (OpenVPN)";
            AppendLog("OpenVPN terhubung! Memulai antrean...");
        }

        int success = 0;
        int failed = 0;

        try
        {
            while (!_stopRequested)
            {
                if (IsPaused) 
                {
                     await Task.Delay(500); 
                     continue;
                }

                var item = Files.FirstOrDefault(x => x.Status == "Menunggu");
                if (item == null) break;

                await ProcessItem(item, job, _cts.Token);

                if (item.IsDone && !item.IsFailed) success++;
                else if (item.IsFailed) failed++;
            }
        }
        finally
        {
            if (hasVpn)
            {
                VpnStatus = "🔌 Memutuskan OpenVPN...";
                AppendLog("Membuang jalur OpenVPN...");
                StopVpnProcess();
                IsVpnActive = false;
                AppendLog("OpenVPN terputus.");
            }
            
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            AppendLog("Antrian selesai.");
            CheckRetryVisibility();
            CheckVpnStatus(); // Refresh back to normal status
            
            if (!_stopRequested && (success > 0 || failed > 0))
            {
                CompletionSuccessCount = success;
                CompletionFailureCount = failed;
                ShowCompletionDialog = true;
                
                // Show Toast Notification
                var toastMsg = $"✅ {success} berhasil";
                if (failed > 0) toastMsg += $" / ❌ {failed} gagal";
                ShowToast(toastMsg, failed > 0 ? "⚠️" : "✅");
            }
        }
    }

    private async Task ProcessItem(PixelcutFileItem item, string job, CancellationToken ct = default)
    {
        item.Status = "Memproses";
        item.IsProcessing = true;
        item.Progress = 0;
        item.IsFailed = false;

        // SKIP LOGIC
        var expectedPath = GetResultPath(item.FilePath, job);
        bool isSameFile = string.Equals(item.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase);

        if (!isSameFile && File.Exists(expectedPath))
        {
            var info = new FileInfo(expectedPath);
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

        // Progress ticker: animate progress bar while HTTP request is running
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var progressTask = Task.Run(async () =>
        {
            try
            {
                while (!progressCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(300, progressCts.Token);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (item.IsProcessing && item.Progress < 90)
                        {
                            item.Progress += 1;
                            item.Status = item.Progress < 30 ? "Mengunggah..." : 
                                          item.Progress < 60 ? "Memproses..." : "Mengunduh hasil...";
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        }, progressCts.Token);

        try
        {
            AppendLog($"Memproses {item.FileName}...");
            await _pixelcutService.ProcessImageAsync(item, job, ct);
            progressCts.Cancel(); // Stop progress ticker
            
            item.Status = "Selesai";
            item.IsDone = true;
            item.Progress = 100;
            
            if (File.Exists(expectedPath))
                item.ResultSize = new FileInfo(expectedPath).Length;
            
            AppendLog($"{item.FileName} selesai.");
        }
        catch (OperationCanceledException)
        {
             progressCts.Cancel();
             item.Status = "Berhenti";
             item.IsFailed = true; 
             item.ErrorMessage = "Dibatalkan";
             item.Progress = 0;
        }
        catch (Exception ex)
        {
            progressCts.Cancel();
            item.Status = "Gagal";
            item.IsFailed = true;
            
            var errorMsg = ex.Message;
            if (errorMsg.Contains("429")) item.ErrorMessage = "Limit (429)!";
            else if (errorMsg.Contains("No space")) item.ErrorMessage = "Disk penuh!";
            else item.ErrorMessage = errorMsg.Length > 50 ? errorMsg.Substring(0, 50) + "..." : errorMsg;
            
            AppendLog($"Gagal memproses {item.FileName}: {errorMsg}");
        }
        finally
        {
            progressCts.Cancel();
            try { await progressTask; } catch { }
            item.IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RetryFile(PixelcutFileItem item)
    {
        if (IsProcessing) return;
        IsProcessing = true;
        AppendLog($"Mengulangi {item.FileName} ({_lastJobType})...");
        _cts = new CancellationTokenSource();
        await ProcessItem(item, _lastJobType, _cts.Token);
        IsProcessing = false;
        _cts.Dispose();
        _cts = null;
    }
    
    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale") return Path.Combine(dir, $"{name}_up.png");
        return Path.Combine(dir, $"{name}.png");
    }

    [ObservableProperty] private bool _isSettingsOpen;

    [RelayCommand]
    private void ShowSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        if (_database != null)
        {
            _database.SetAsync("Configs.Pixelcut.Proxy", ProxyAddress);
            _database.SetAsync("Configs.Pixelcut.OvpnPath", OvpnPath);
            _database.SetAsync("Configs.Pixelcut.OvpnUsername", OvpnUsername);
            _database.SetAsync("Configs.Pixelcut.OvpnPassword", OvpnPassword);
        }
    }

    private async Task StartVpnProcessAsync()
    {
        try
        {
            var tempAuth = Path.Combine(Path.GetTempPath(), "bma_vpn_auth.txt");
            await File.WriteAllLinesAsync(tempAuth, new[] { OvpnUsername, OvpnPassword });

            var isMac = OperatingSystem.IsMacOS();
            
            _vpnProcess = new Process();
            
            if (isMac)
            {
                // Inject SPLIT TUNNELING: route-nopull prevents ALL traffic from going to VPN
                // and route api2.pixelcut.app only maps Pixelcut API thru the tunnel.
                var args = $"--config \\\"{OvpnPath}\\\" --auth-user-pass \\\"{tempAuth}\\\" --route-nopull --route api2.pixelcut.app";
                var script = $"do shell script \"openvpn {args}\" with administrator privileges";
                
                _vpnProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e '{script}'",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else
            {
                _vpnProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = "openvpn",
                    Arguments = $"--config \"{OvpnPath}\" --auth-user-pass \"{tempAuth}\" --route-nopull --route api2.pixelcut.app",
                    UseShellExecute = true, // for UAC prompt on Windows
                    Verb = "runas"
                };
            }

            _vpnProcess.Start();
        }
        catch (Exception ex)
        {
            AppendLog($"Gagal menyalakan OpenVPN: {ex.Message}");
        }
    }

    private void StopVpnProcess()
    {
        try
        {
            if (_vpnProcess != null && !_vpnProcess.HasExited)
            {
                _vpnProcess.Kill();
            }
            // Cleanup Auth File
            var tempAuth = Path.Combine(Path.GetTempPath(), "bma_vpn_auth.txt");
            if (File.Exists(tempAuth)) File.Delete(tempAuth);
            
            // For Mac, osascript spawns an OpenVPN daemon that might detach.
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'do shell script \"killall openvpn\" with administrator privileges'",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch {}
    }

    [RelayCommand]
    private void ToggleLogView()
    {
        IsLogViewVisible = !IsLogViewVisible;
    }

    [RelayCommand]
    private void CloseLogView()
    {
        IsLogViewVisible = false;
    }

    private void ShowToast(string message, string icon = "✅")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsToastVisible = false);
            };
            _toastTimer.Start();
        });
    }

    [RelayCommand]
    private void DismissToast() => IsToastVisible = false;
        
    private void AppendLog(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogOutput += $"{msg}\n";
        Console.WriteLine($"[Pixelcut] {msg}");
    }

    [RelayCommand]
    private void OpenVpnBookLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.vpnbook.com/freevpn/openvpn",
                UseShellExecute = true
            });
        }
        catch { }
    }
}

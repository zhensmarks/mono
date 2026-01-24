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

namespace BMachine.UI.ViewModels;

public partial class PixelcutViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    
    [ObservableProperty] private ObservableCollection<PixelcutFileItem> _files = new();
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _vpnStatus = "Memeriksa...";
    [ObservableProperty] private bool _isVpnActive;
    [ObservableProperty] private string _logOutput = "";
    [ObservableProperty] private bool _showLogPanel;
    
    // Settings
    [ObservableProperty] private string _proxyAddress = "";
    
    private bool _stopRequested;
    [ObservableProperty] private bool _isPaused;
    private System.Timers.Timer? _vpnCheckTimer; // Added timer field

    public PixelcutViewModel(IDatabase? database)
    {
        _database = database;
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
        }
    }

    partial void OnProxyAddressChanged(string value)
    {
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
            
            // Common VPN interface patterns
            var vpnKeywords = new[] { "vpn", "tun", "tap", "ppp", "wintun", "wireguard", "openvpn", "avira", "nordvpn", "expressvpn" };
            
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                    
                var name = iface.Name.ToLower();
                var desc = iface.Description.ToLower();
                
                // Check if interface name or description contains VPN keywords
                if (vpnKeywords.Any(kw => name.Contains(kw) || desc.Contains(kw)))
                {
                    IsVpnActive = true;
                    VpnStatus = $"VPN Aktif ({iface.Name})";
                    return;
                }
            }
            
            // No VPN detected
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
                var searchPattern = new System.Collections.Generic.HashSet<string> { ".jpg", ".jpeg", ".png", ".psd", ".webp" };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        var p = CheckSmartPngReplacement(path);
                        if (p != null) validPaths.Add(p);
                    }
                    else if (Directory.Exists(path))
                    {
                         // Use Safe Walker
                         validPaths.AddRange(SafeGetFiles(path, searchPattern));
                    }
                }

                if (validPaths.Any())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var p in validPaths)
                        {
                            try
                            {
                                if (!Files.Any(f => f.FilePath == p))
                                {
                                    Files.Add(new PixelcutFileItem(p));
                                }
                            }
                            catch { }
                        }
                        HasFiles = Files.Count > 0;
                        CheckRetryVisibility();
                    });
                }
            });
        }
        catch (Exception ex)
        {
             Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                AppendLog($"Error processing drop: {ex.Message}"));
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
                // Files
                foreach (var file in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (extensions.Contains(ext))
                    {
                         result.Add(file);
                    }
                }

                // Subdirectories
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    stack.Push(subDir);
                }
            }
            catch (Exception)
            {
                // Ignore Access Denied / Path Too Long / etc for this specific folder
                // Continue with others
            }
        }
        return result;
    }

    private string? CheckSmartPngReplacement(string path)
    {
        try 
        {
             var ext = Path.GetExtension(path).ToLower();
             if (!IsSupportedExtension(ext)) return null;

             if (ext == ".png")
             {
                 var info = new FileInfo(path);
                 // Only accept PNG if it is a placeholder (<= 1KB)
                 if (info.Length <= 1024)
                 {
                     var dir = Path.GetDirectoryName(path);
                     if (string.IsNullOrEmpty(dir)) return null;

                     var name = Path.GetFileNameWithoutExtension(path);
                     var jpg = Path.Combine(dir, name + ".jpg");
                     var jpeg = Path.Combine(dir, name + ".jpeg");

                     if (File.Exists(jpg)) return jpg;
                     if (File.Exists(jpeg)) return jpeg;
                 }
                 
                 // If normal PNG or placeholder without JPG partner -> Ignore
                 return null;
             }
             return path;
        }
        catch { return null; }
    }
    
    private bool IsSupportedExtension(string ext)
    {
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".psd" || ext == ".webp";
    }

    [RelayCommand]
    private void RemoveFile(PixelcutFileItem item)
    {
        if (Files.Contains(item))
        {
            Files.Remove(item);
        }
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsProcessing) return;
        
        // Smart Clear: If items are selected, remove them. Else clear all.
        if (Files.Any(f => f.IsSelected))
        {
            RemoveSelectedFiles();
        }
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

        // Retry if:
        // 1. Files marked as Failed
        // 2. Files marked as Done but Size < 100 bytes (e.g. 59B error)
        IsRetryVisible = Files.Any(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 100));
    }

    [RelayCommand]
    private async Task ProcessRemoveBg()
    {
        await ProcessQueue("remove_bg");
    }

    [RelayCommand]
    private async Task ProcessUpscale()
    {
        await ProcessQueue("upscale");
    }
    
    [RelayCommand]
    private void Stop()
    {
        _stopRequested = true;
        AppendLog("Stop diminta...");
    }
    
    [RelayCommand]
    private void Pause()
    {
        IsPaused = !IsPaused;
        AppendLog(IsPaused ? "Dijeda..." : "Melanjutkan...");
    }

    [RelayCommand]
    private void ToggleLog()
    {
        ShowLogPanel = !ShowLogPanel;
    }

    [RelayCommand]
    private void RemoveSelectedFiles()
    {
        var selected = Files.Where(x => x.IsSelected).ToList();
        foreach (var item in selected)
        {
            Files.Remove(item);
        }
        HasFiles = Files.Count > 0;
        CheckRetryVisibility();
    }

    [RelayCommand]
    private async Task RetrySmallFiles()
    {
        if (IsProcessing) return;

        // "59 B" is very small, likely an error response or empty file.
        // We filter for "Selesai" but size is small (e.g. < 1KB or specifically around 59B).
        // Let's use < 100 bytes to be safe.
        var smallFiles = Files.Where(x => x.IsDone && x.ResultSize > 0 && x.ResultSize < 100).ToList();
        
        // OR files that explicitly failed
        var failedFiles = Files.Where(x => x.IsFailed).ToList();
        
        var toRetry = smallFiles.Union(failedFiles).Distinct().ToList();

        if (toRetry.Count == 0) return;

        AppendLog($"Mengulangi {toRetry.Count} file gagal/corrupt...");
        
        foreach (var item in toRetry)
        {
            // Reset Status
            item.Status = "Menunggu";
            item.IsDone = false;
            item.IsFailed = false;
            item.Progress = 0;
            item.ErrorMessage = "";
        }

        // Trigger Queue again
        await ProcessQueue(_lastJobType);
    }

    [ObservableProperty] private bool _showCompletionDialog;
    [ObservableProperty] private int _completionSuccessCount;
    [ObservableProperty] private int _completionFailureCount;

    [RelayCommand]
    private void CloseCompletionDialog()
    {
        ShowCompletionDialog = false;
    }

    private string _lastJobType = "remove_bg"; // Default

    private async Task ProcessQueue(string job)
    {
        if (IsProcessing) return;
        _lastJobType = job; // Store for Retry
        IsProcessing = true;
        _stopRequested = false;
        IsPaused = false;
        AppendLog($"Memulai proses {job}...");

        int success = 0;
        int failed = 0;

        while (!_stopRequested)
        {
            if (IsPaused) 
            {
                 await Task.Delay(500); 
                 continue;
            }

            // Find next ready item dynamically (Live Queue)
            // We use ToList() only to safely find the item without modifying collection during enumeration
            // But since we are Modify property not collection, it's safer to just Linq.
            // However, Linq on ObservableCollection is not thread safe if UI adds items.
            // But Add happens on UI thread. This ProcessQueue runs on background Task? No, it's async void/Task on UI thread context?
            // Wait, ProcessQueue is async Task. If called from Command, it's on UI thread.
            // But ProcessItem calls RunPythonWorker which awaits Task.Run or Process.
            // Let's assume we are on UI context or captured context.
            
            PixelcutFileItem? item = null;
            
            // Thread-safe access attempt (simple lock not possible on ObservableCollection without proper sync)
            // But if we are on UI thread (due to async/await), it's safe.
            // If we are on background thread, we might crash.
            // ProcessQueue is called by [RelayCommand], so it starts on UI Thread.
            // Await points might return to UI thread if context captured.
            
            item = Files.FirstOrDefault(x => x.Status == "Menunggu");

            if (item == null)
            {
                // No more items ready.
                break;
            }

            await ProcessItem(item, job);

            if (item.IsDone && !item.IsFailed) success++;
            else if (item.IsFailed) failed++;
        }

        IsProcessing = false;
        AppendLog("Antrian selesai.");
        CheckRetryVisibility();
        
        // Show Completion Dialog if not stopped manually
        if (!_stopRequested && (success > 0 || failed > 0))
        {
            CompletionSuccessCount = success;
            CompletionFailureCount = failed;
            ShowCompletionDialog = true;
        }
    }

    private async Task ProcessItem(PixelcutFileItem item, string job)
    {
        item.Status = "Memproses";
        item.IsProcessing = true;
        item.Progress = 0;
        item.IsFailed = false;

        try
        {
            // Call Python Backend
            await RunPythonWorker(item, job);
            
            if (!item.IsFailed)
            {
                item.Status = "Selesai";
                item.IsDone = true;
                item.Progress = 100;
                
                // Check Result Size
                var resultPath = GetResultPath(item.FilePath, job);
                if (File.Exists(resultPath))
                {
                    item.ResultSize = new FileInfo(resultPath).Length;
                }
            }
        }
        catch (Exception ex)
        {
            item.Status = "Gagal";
            item.IsFailed = true;
            
            // Extract core error message for inline display
            var errorMsg = ex.Message;
            if (errorMsg.Contains("No space left")) item.ErrorMessage = "Disk penuh!";
            else if (errorMsg.Contains("Permission denied")) item.ErrorMessage = "Akses ditolak!";
            else if (errorMsg.Contains("exit code")) item.ErrorMessage = "Proses gagal";
            else item.ErrorMessage = errorMsg.Length > 50 ? errorMsg.Substring(0, 50) + "..." : errorMsg;
            
            AppendLog($"Gagal memproses {item.FileName}: {ex.Message}");
        }
        finally
        {
            item.IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RetryFile(PixelcutFileItem item)
    {
        if (IsProcessing) return; // Prevent concurrent single-file retry if queue is running? Or allow parallelism?
        // Let's allow it but set IsProcessing=true just for safety or manage it locally
        
        IsProcessing = true;
        AppendLog($"Mengulangi {item.FileName} ({_lastJobType})...");
        await ProcessItem(item, _lastJobType);
        IsProcessing = false;
    }
    
    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale") return Path.Combine(dir, $"{name}_up.png");
        return Path.Combine(dir, $"{name}.png");
    }

    [RelayCommand]
    private void ShowSettings()
    {
        // Simple toggle for now, or use a dialog service if available.
        // For MVP, we can toggle visibility of a settings panel overlay in the View
        IsSettingsOpen = !IsSettingsOpen;
    }

    [ObservableProperty] private bool _isSettingsOpen;

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        // Save Settings
        if (_database != null)
        {
            _database.SetAsync("Configs.Pixelcut.Proxy", ProxyAddress);
        }
    }

    private async Task RunPythonWorker(PixelcutFileItem item, string job)
    {
        // Use relative path from application directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var scriptPath = Path.Combine(baseDir, "Scripts", "Core", "pixelcut_cli.py");
        
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"pixelcut_cli.py not found at {scriptPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\" --action {job} --input \"{item.FilePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)
        };

        if (!string.IsNullOrEmpty(ProxyAddress))
        {
            startInfo.Arguments += $" --proxy \"{ProxyAddress}\"";
        }

        var tcs = new TaskCompletionSource<bool>();
        
        using var process = new Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (s, e) => 
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            // Handle Signals
            if (e.Data.StartsWith("SIGNAL:"))
            {
                // SIGNAL:name:json_data
                var parts = e.Data.Split(':', 3);
                if (parts.Length == 3)
                {
                    var sigName = parts[1];
                    var json = parts[2];
                    
                    // Update UI on Main Thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        if (sigName == "process")
                        {
                            // Parse JSON for status
                            // {"id":..., "item":..., "data": {"status": "message"}}
                            try 
                            {
                                // Simple string match to avoid JSON parsing overhead for now
                                // Or parse properly if complex
                                if (json.Contains("\"status\":"))
                                {
                                    // Extract status value roughly or use JsonNode
                                    // Let's just log it for now as proof of life
                                    // AppendLog($"[Progress] {json}");
                                    
                                    // If status is percentage?
                                    // The Python sends "100%" or "request to..."
                                }
                            }
                            catch {}
                        }
                    });
                }
            }
            else
            {
                AppendLog(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) => 
        {
             if (!string.IsNullOrEmpty(e.Data))
             {
                 AppendLog($"[Error] {e.Data}");
                 // If error represents failure, we can flag it, but usually exit code tells us.
             }
        };

        process.Exited += (s, e) => 
        {
            if (process.ExitCode == 0)
                tcs.TrySetResult(true);
            else
                tcs.TrySetException(new Exception($"Process exited with code {process.ExitCode}"));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for exit or cancellation
        // We could implement CancellationToken here for Stop/Pause
        while (!process.HasExited)
        {
             if (IsPaused)
             {
                 item.Status = "Dijeda...";
                 await Task.Delay(500);
             }
             else
             {
                 item.Status = "Berjalan..."; 
                 // Simulated progress for responsiveness since pixelcut.py doesn't emit granular % during download
                 if (item.Progress < 90) item.Progress += 1;
                 
                 await Task.Delay(100); 
             }
             
             if (_stopRequested)
             {
                 process.Kill();
                 throw new TaskCanceledException("Pengguna menghentikan proses.");
             }
        }

        await tcs.Task;
    }
    
    private void AppendLog(string message)
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }
}

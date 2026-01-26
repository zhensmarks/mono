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
    
    // Settings
    [ObservableProperty] private string _proxyAddress = "";
    
    private bool _stopRequested;
    [ObservableProperty] private bool _isPaused;
    private System.Timers.Timer? _vpnCheckTimer;

    public MainWindowViewModel()
    {
        // Load Settings
        var settings = _settingsService.Load();
        ProxyAddress = settings.ProxyAddress ?? "";

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
        
        // Save Settings
        var settings = _settingsService.Load();
        settings.ProxyAddress = value;
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

    [RelayCommand]
    private async Task DropFiles(string[] paths)
    {
        IsProcessing = true;
        try
        {
            await Task.Run(() =>
            {
                var validPaths = new List<string>();
                var searchPattern = new HashSet<string> { ".jpg", ".jpeg", ".png", ".psd", ".webp" };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        var ext = Path.GetExtension(path).ToLower();
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
                        foreach (var p in validPaths)
                        {
                            if (!Files.Any(f => f.FilePath == p))
                            {
                                Files.Add(new PixelcutFileItem(p));
                            }
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
        if (IsProcessing) 
        {
            IsRetryVisible = false;
            return;
        }
        IsRetryVisible = Files.Any(x => x.IsFailed || (x.IsDone && x.ResultSize > 0 && x.ResultSize < 100));
    }

    private string _lastJobType = "remove_bg";

    [RelayCommand]
    private async Task ProcessRemoveBg() => await ProcessQueue("remove_bg");

    [RelayCommand]
    private async Task ProcessUpscale() => await ProcessQueue("upscale");
    
    [RelayCommand]
    private void Stop()
    {
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
        }
    }

    private async Task ProcessItem(PixelcutFileItem item, string job, CancellationToken ct)
    {
        // Don't set text "Memproses..."
        item.Status = ""; 
        item.IsProcessing = true;
        item.Progress = 0;
        item.IsFailed = false;

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
                item.ResultSize = new FileInfo(resultPath).Length;
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = "Berhenti";
            item.IsFailed = true; 
            item.ErrorMessage = "Dibatalkan";
            item.Progress = 0;
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
        
        if (job == "upscale") return Path.Combine(dir, $"{name}_up.png");
        return Path.Combine(dir, $"{name}.png");
    }

    [RelayCommand]
    private void ToggleLog()
    {
        // Deprecated by Tab UI
        // ShowLogPanel = !ShowLogPanel;
    }

    private void AppendLog(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogOutput += $"{msg}\n";
        Console.WriteLine($"[PixelcutCompact] {msg}");
    }
}

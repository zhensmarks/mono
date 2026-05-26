using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services;

public class PixelcutService : IDisposable
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static List<string>? _autoProxyCache;
    private static DateTime _autoProxyCacheAtUtc;

    // API mode has been removed; process always uses web automation.
    public string? ApiKey { get; set; }
    public bool UseWebMode { get; set; } = true;
    public string RemoveBgEngine { get; set; } = "PIXA";
    public string RembgModel { get; set; } = "u2netp";
    public string? RembgExecutablePath { get; set; }
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
    public bool ShowBrowser { get; set; }

    public bool UseGpuForRembg { get; set; } = true;
    public bool AlphaMattingEnabled { get; set; }
    public int AlphaMattingErodeSize { get; set; } = 10;
    public int AlphaMattingForegroundThreshold { get; set; } = 240;
    public int AlphaMattingBackgroundThreshold { get; set; } = 10;
    private PixaWebAutomationService? _webAutomation;
    private NobgSpaceWebAutomationService? _nobgWebAutomation;
    private RembgOnlineWebAutomationService? _rembgOnlineWebAutomation;
    private BgEraserWebAutomationService? _bgEraserWebAutomation;
    private readonly RembgCliService _rembgCli = new();

    public PixelcutService()
    {
    }

    /// <summary>Dispose browser instance saat ini. Browser fresh akan dibuat otomatis saat proses berikutnya dimulai.</summary>
    public void ResetWebAutomation()
    {
        try { _webAutomation?.Dispose(); } catch { }
        _webAutomation = null;

        try { _nobgWebAutomation?.Dispose(); } catch { }
        _nobgWebAutomation = null;

        try { _rembgOnlineWebAutomation?.Dispose(); } catch { }
        _rembgOnlineWebAutomation = null;

        try { _bgEraserWebAutomation?.Dispose(); } catch { }
        _bgEraserWebAutomation = null;
    }

    public async Task InitializeAsync()
    {
        // No async init needed anymore
        await Task.CompletedTask;
    }

    public async Task ProcessImageAsync(PixelcutFileItem item, string jobType)
    {
        await ProcessImageAsync(item, jobType, CancellationToken.None);
    }

    public async Task ProcessImageAsync(PixelcutFileItem item, string jobType, CancellationToken ct)
    {
        if (string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RemoveBgEngine, "REMBG", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessViaRembgAsync(item, jobType, ct);
            return;
        }
        if (string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RemoveBgEngine, "NOBG_SPACE", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessViaNobgSpaceAsync(item, jobType, ct);
            return;
        }
        if (string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RemoveBgEngine, "REMBG_ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessViaRembgOnlineAsync(item, jobType, ct);
            return;
        }
        if (string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RemoveBgEngine, "BG_ERASER", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessViaBgEraserAsync(item, jobType, ct);
            return;
        }

        await ProcessViaWebModeAsync(item, jobType, ct);
    }

    private async Task ProcessViaWebModeAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        if (_webAutomation == null)
        {
            _webAutomation = new PixaWebAutomationService(null, ShowBrowser);
            await _webAutomation.InitializeAsync();
        }

        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new PixaWebAutomationService(proxy, ShowBrowser);
                await svc.InitializeAsync();
                return await svc.ProcessImageAsync(item.FilePath, job, ct);
            });
        }
        else
        {
            resultBytes = await _webAutomation.ProcessImageAsync(item.FilePath, job, ct);
        }
        
        string resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes);
    }

    private async Task ProcessViaRembgAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        var resultPath = GetResultPath(item.FilePath, job);
        _rembgCli.Model = RembgModel;
        _rembgCli.ExecutablePath = RembgExecutablePath;
        _rembgCli.UseGpu = UseGpuForRembg;
        _rembgCli.AlphaMattingEnabled = AlphaMattingEnabled;
        _rembgCli.AlphaMattingErodeSize = AlphaMattingErodeSize;
        _rembgCli.AlphaMattingForegroundThreshold = AlphaMattingForegroundThreshold;
        _rembgCli.AlphaMattingBackgroundThreshold = AlphaMattingBackgroundThreshold;
        await _rembgCli.ProcessImageAsync(item.FilePath, resultPath, ct);
    }

    private async Task ProcessViaNobgSpaceAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new NobgSpaceWebAutomationService(proxy, ShowBrowser);
                await svc.InitializeAsync();
                return await svc.ProcessImageAsync(item.FilePath, job, ct);
            });
        }
        else
        {
            if (_nobgWebAutomation == null)
            {
                _nobgWebAutomation = new NobgSpaceWebAutomationService(null, ShowBrowser);
                await _nobgWebAutomation.InitializeAsync();
            }
            resultBytes = await _nobgWebAutomation.ProcessImageAsync(item.FilePath, job, ct);
        }

        var resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes, ct);
    }

    private async Task ProcessViaRembgOnlineAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new RembgOnlineWebAutomationService(proxy, ShowBrowser);
                await svc.InitializeAsync();
                return await svc.ProcessImageAsync(item.FilePath, job, ct);
            });
        }
        else
        {
            if (_rembgOnlineWebAutomation == null)
            {
                _rembgOnlineWebAutomation = new RembgOnlineWebAutomationService(null, ShowBrowser);
                await _rembgOnlineWebAutomation.InitializeAsync();
            }
            resultBytes = await _rembgOnlineWebAutomation.ProcessImageAsync(item.FilePath, job, ct);
        }

        var resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes, ct);
    }

    private async Task ProcessViaBgEraserAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new BgEraserWebAutomationService(proxy, ShowBrowser);
                await svc.InitializeAsync();
                return await svc.ProcessImageAsync(item.FilePath, job, ct);
            });
        }
        else
        {
            if (_bgEraserWebAutomation == null)
            {
                _bgEraserWebAutomation = new BgEraserWebAutomationService(null, ShowBrowser);
                await _bgEraserWebAutomation.InitializeAsync();
            }
            resultBytes = await _bgEraserWebAutomation.ProcessImageAsync(item.FilePath, job, ct);
        }

        var resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes, ct);
    }

    private async Task<byte[]> ProcessWithProxyRetriesAsync(Func<string?, Task<byte[]>> attempt)
    {
        var proxies = ParseProxyList(MixProxyList);
        if (proxies.Count == 0)
            proxies = await GetAutomaticProxyCandidatesAsync();

        if (proxies.Count == 0)
            throw new Exception("Mix Proxy aktif, tapi proxy otomatis tidak tersedia. Isi daftar proxy manual atau coba lagi.");

        var errors = new List<string>();
        foreach (var proxy in Shuffle(proxies))
        {
            try
            {
                return await attempt(proxy);
            }
            catch (Exception ex)
            {
                var tag = string.IsNullOrWhiteSpace(proxy) ? "NO_PROXY" : proxy;
                errors.Add($"{tag}: {ex.Message}");
            }
        }

        throw new Exception("Semua proxy gagal. " + string.Join(" | ", errors.Take(3)));
    }

    private static List<string> ParseProxyList(string? raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        var parts = raw
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (!string.IsNullOrWhiteSpace(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase))
                list.Add(p);
        }
        return list;
    }

    private static async Task<List<string>> GetAutomaticProxyCandidatesAsync()
    {
        // Cache to avoid repeated fetching for each file.
        if (_autoProxyCache != null && (DateTime.UtcNow - _autoProxyCacheAtUtc) < TimeSpan.FromMinutes(10))
            return new List<string>(_autoProxyCache);

        var sources = new[]
        {
            "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&proxytype=http&timeout=4000&country=all&ssl=all&anonymity=all",
            "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt"
        };

        var collected = new List<string>();
        foreach (var url in sources)
        {
            try
            {
                var text = await Http.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    // Normalize "ip:port" into Playwright proxy format.
                    var proxy = line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)
                        ? line
                        : $"http://{line}";

                    if (!collected.Contains(proxy, StringComparer.OrdinalIgnoreCase))
                        collected.Add(proxy);
                }
            }
            catch
            {
                // Best effort: skip unavailable source.
            }
        }

        // Keep it bounded; randomization happens later.
        _autoProxyCache = collected.Take(80).ToList();
        _autoProxyCacheAtUtc = DateTime.UtcNow;
        return new List<string>(_autoProxyCache);
    }

    private static IEnumerable<string> Shuffle(IEnumerable<string> source)
    {
        var arr = source.ToList();
        for (int i = arr.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    public Task<string> GetCreditsAsync()
    {
        return Task.FromResult("API mode dinonaktifkan");
    }

    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale")
        {
             // Match input extension
             var ext = Path.GetExtension(input);
             // Default to png if no extension
             if (string.IsNullOrEmpty(ext)) ext = ".png";
             return Path.Combine(dir, $"{name}_up{ext}");
        }
        return Path.Combine(dir, $"{name}.png");
    }

    public void Dispose()
    {
        _webAutomation?.Dispose();
        _nobgWebAutomation?.Dispose();
        _rembgOnlineWebAutomation?.Dispose();
        _bgEraserWebAutomation?.Dispose();
    }
}

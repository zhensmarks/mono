using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace PixelcutCompact.Services;

public sealed class NobgSpaceWebAutomationService : IDisposable
{
    private readonly string? _proxyServer;
    private readonly bool _showBrowser;
    private readonly string? _profileSuffix;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private bool _isInitialized;

    // Guard init supaya aman saat beberapa file diproses paralel.
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NobgSpaceWebAutomationService(string? proxyServer = null, bool showBrowser = false, string? profileSuffix = null)
    {
        _proxyServer = string.IsNullOrWhiteSpace(proxyServer) ? null : proxyServer.Trim();
        _showBrowser = showBrowser;
        _profileSuffix = profileSuffix;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Browser context cukup dibuat sekali walau beberapa file mulai bersamaan.
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _playwright = await Playwright.CreateAsync();

            try
            {
                _context = await LaunchContextAsync(_playwright);
            }
            catch (Exception ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exitCode != 0) throw new Exception("Gagal install browser Playwright untuk NOBG.");
                _context = await LaunchContextAsync(_playwright);
            }

            await PixelcutCompact.Helpers.PlaywrightStealthHelper.ApplyStealthSettingsAsync(_context);
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<IBrowserContext> LaunchContextAsync(IPlaywright playwright)
    {
        var channel = DetectDefaultBrowserChannel();
        var profileName = string.IsNullOrEmpty(_profileSuffix) ? "NobgProfile" : $"NobgProfile_{_profileSuffix}";
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, profileName);
        if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir);

        var baseArgs = new[] { "--disable-blink-features=AutomationControlled", "--no-sandbox", "--disable-dev-shm-usage" };

        var opts = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = !_showBrowser,
            Channel = channel,
            Args = baseArgs,
            IgnoreDefaultArgs = new[] { "--enable-automation" },
            Proxy = string.IsNullOrWhiteSpace(_proxyServer) ? null : new Proxy { Server = _proxyServer },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
        };

        try
        {
            return await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, opts);
        }
        catch when (!string.IsNullOrWhiteSpace(channel))
        {
            opts.Channel = null;
            return await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, opts);
        }
    }

    private static string? DetectDefaultBrowserChannel()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var userChoice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = userChoice?.GetValue("ProgId")?.ToString()?.ToLowerInvariant() ?? "";
            if (progId.Contains("msedge")) return "msedge";
            if (progId.Contains("chrome")) return "chrome";
        }
        catch { }

        return null;
    }

    public async Task<byte[]> ProcessImageAsync(string filePath, string jobType, CancellationToken ct)
    {
        if (!string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("NOBG_SPACE hanya mendukung remove_bg.");

        await InitializeAsync();
        if (_context == null) throw new Exception("Browser NOBG belum siap.");

        // Satu page (tab browser) per file - aman dijalankan paralel.
        var page = await _context.NewPageAsync();
        page.SetDefaultTimeout(120000);
        try
        {
            return await ProcessInPage(page, filePath, ct);
        }
        finally
        {
            try { await page.CloseAsync(); } catch { }
        }
    }

    private async Task<byte[]> ProcessInPage(IPage page, string filePath, CancellationToken ct)
    {
        await page.GotoAsync("https://nobg.space", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120000
        });

        var uploadInput = page.Locator("input[type=\"file\"]").First;
        if (await uploadInput.CountAsync() == 0)
            throw new Exception("Input file tidak ditemukan di nobg.space.");

        await uploadInput.SetInputFilesAsync(filePath);
        await Task.Delay(4500, ct);

        var downloadButton = page.Locator("text=Download").First;
        await downloadButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 120000 });
            await downloadButton.ClickAsync();
            var download = await downloadTask;

            await download.SaveAsAsync(tempPath);
            if (!File.Exists(tempPath))
                throw new Exception("Download NOBG gagal (file output tidak ada).");

            return await File.ReadAllBytesAsync(tempPath, ct);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    public void Dispose()
    {
        _context?.CloseAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}

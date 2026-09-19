using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace PixelcutCompact.Services;

public sealed class RembgOnlineWebAutomationService : IDisposable
{
    private readonly string? _proxyServer;
    private readonly bool _showBrowser;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _isInitialized;

    public RembgOnlineWebAutomationService(string? proxyServer = null, bool showBrowser = false)
    {
        _proxyServer = string.IsNullOrWhiteSpace(proxyServer) ? null : proxyServer.Trim();
        _showBrowser = showBrowser;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        _playwright = await Playwright.CreateAsync();
        _context = await LaunchContextWithFallbackAsync(_playwright);
        await PixelcutCompact.Helpers.PlaywrightStealthHelper.ApplyStealthSettingsAsync(_context);
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(120000);
        _isInitialized = true;
    }

    private async Task<IBrowserContext> LaunchContextWithFallbackAsync(IPlaywright playwright)
    {
        var channel = DetectDefaultBrowserChannel();
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RembgOnlineProfile");
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
            throw new NotSupportedException("REMBG online hanya mendukung remove_bg.");

        await InitializeAsync();
        if (_page == null) throw new Exception("Browser REMBG online belum siap.");

        await _page.GotoAsync("https://www.rembg.com/en/free-background-remover", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120000
        });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var frame = await FindFrameWithUploadAsync(_page);
        if (frame == null)
            throw new Exception("Input upload tidak ditemukan di rembg.com.");

        var uploadInput = frame.Locator("input[type=\"file\"]");
        if (await uploadInput.CountAsync() > 0)
        {
            await uploadInput.First.SetInputFilesAsync(filePath);
        }
        else
        {
            var chooser = await _page.RunAndWaitForFileChooserAsync(async () =>
            {
                var uploadTrigger = frame.Locator(
                    "text=Drag & drop an image or click to browse, " +
                    "button:has-text(\"Upload\"), button:has-text(\"Upload Image\"), " +
                    "a:has-text(\"Upload\"), [role=\"button\"]:has-text(\"Upload\")").First;
                await uploadTrigger.ClickAsync();
            });
            await chooser.SetFilesAsync(filePath);
        }

        // Wait until "Download PNG" button is available (as in provided screenshot).
        var downloadButton = frame.Locator(
            "button:has-text(\"Download PNG\"), a:has-text(\"Download PNG\"), " +
            "button:has-text(\"Download\"), a:has-text(\"Download\")").First;
        await downloadButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 120000, State = WaitForSelectorState.Visible });

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            var downloadTask = _page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 120000 });
            await downloadButton.ClickAsync();
            var download = await downloadTask;
            await download.SaveAsAsync(tempPath);

            if (!File.Exists(tempPath))
                throw new Exception("Download PNG dari rembg.com gagal.");

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

    private static async Task<IFrame?> FindFrameWithUploadAsync(IPage page)
    {
        for (var i = 0; i < 40; i++)
        {
            foreach (var f in page.Frames)
            {
                try
                {
                    if (await f.Locator("input[type=\"file\"]").CountAsync() > 0)
                        return f;

                    if (await f.Locator("text=Drag & drop an image or click to browse").CountAsync() > 0)
                        return f;
                }
                catch
                {
                    // Ignore detached/cross-origin transient frame issues and continue scan.
                }
            }

            await Task.Delay(250);
        }

        return null;
    }
}

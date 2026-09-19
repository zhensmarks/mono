using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace PixelcutCompact.Services;

public sealed class BgEraserWebAutomationService : IDisposable
{
    private readonly string? _proxyServer;
    private readonly bool _showBrowser;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _isInitialized;

    public BgEraserWebAutomationService(string? proxyServer = null, bool showBrowser = false)
    {
        _proxyServer = string.IsNullOrWhiteSpace(proxyServer) ? null : proxyServer.Trim();
        _showBrowser = showBrowser;
    }

    public async Task InitializeAsync()
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
            if (exitCode != 0) throw new Exception("Gagal install browser Playwright untuk BG Eraser.");
            _context = await LaunchContextAsync(_playwright);
        }

        await PixelcutCompact.Helpers.PlaywrightStealthHelper.ApplyStealthSettingsAsync(_context);
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(120000);
        _isInitialized = true;
    }

    private async Task<IBrowserContext> LaunchContextAsync(IPlaywright playwright)
    {
        var channel = DetectDefaultBrowserChannel();
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BgEraserProfile");
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
            throw new NotSupportedException("BG_ERASER hanya mendukung remove_bg.");

        await InitializeAsync();
        if (_page == null) throw new Exception("Browser BG Eraser belum siap.");

        // 1. Navigate ke bgeraser.com
        await _page.GotoAsync("https://bgeraser.com/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120000
        });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 2. Dismiss banner/popup jika ada
        await DismissPopupsAsync();

        // 3. Upload file via input[type="file"]
        var uploadInput = _page.Locator("input[type=\"file\"]").First;
        await uploadInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000, State = WaitForSelectorState.Attached });
        await uploadInput.SetInputFilesAsync(filePath);
        await Task.Delay(2000, ct);

        // 4. Klik tombol "Remove Background"
        var removeBtn = _page.Locator("button:has-text(\"Remove Background\")").First;
        await removeBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000, State = WaitForSelectorState.Visible });
        await removeBtn.ClickAsync();

        // 5. Tunggu processing selesai — monitor "Processing..." hilang dan "Process image successfully" muncul
        try
        {
            await _page.WaitForFunctionAsync(@"
                () => {
                    const text = document.body.innerText;
                    return !text.includes('Processing...') && 
                           (text.includes('Process image successfully') || text.includes('Download'));
                }
            ", null, new PageWaitForFunctionOptions { Timeout = 120000 });
        }
        catch (TimeoutException)
        {
            throw new Exception("BG Eraser timeout: processing gambar terlalu lama.");
        }

        // Extra delay agar DOM benar-benar update
        await Task.Delay(2000, ct);

        // 6. Dismiss popup iklan yang mungkin muncul setelah processing
        await DismissPopupsAsync();

        // 7. Download hasil — klik tombol download individual di overlay gambar
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            // Coba klik tombol download individual (icon di overlay gambar)
            // Tombol download pertama di area hasil
            var downloadBtn = _page.Locator("button:has(svg), a:has(svg)")
                .Filter(new LocatorFilterOptions { HasText = "" });

            // Fallback: gunakan "Download All" button
            var downloadAllBtn = _page.Locator("button:has-text(\"Download All\"), button:has-text(\"Download\")").First;

            // Prioritaskan download individual via overlay
            // Cari tombol download di area gambar hasil (biasanya di group/overlay)
            var imgDownloadBtn = _page.Locator("[class*='group'] button").First;

            ILocator? targetBtn = null;

            // Cek apakah download individual ada
            if (await imgDownloadBtn.CountAsync() > 0 && await imgDownloadBtn.IsVisibleAsync())
            {
                targetBtn = imgDownloadBtn;
            }
            else if (await downloadAllBtn.CountAsync() > 0 && await downloadAllBtn.IsVisibleAsync())
            {
                targetBtn = downloadAllBtn;
            }

            if (targetBtn == null)
                throw new Exception("Tombol download tidak ditemukan di BG Eraser.");

            // 1. Coba download normal (bypassing overlay via JS click)
            try
            {
                var downloadTask = _page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 15000 });
                await targetBtn.EvaluateAsync("btn => btn.click()");
                var download = await downloadTask;

                await download.SaveAsAsync(tempPath);
                if (File.Exists(tempPath))
                    return await File.ReadAllBytesAsync(tempPath, ct);
            }
            catch (TimeoutException)
            {
                // Fallback: Ekstrak gambar langsung dari DOM menggunakan JS jika klik gagal/timeout
                var fallbackBytes = await ExtractImageViaJsAsync();
                if (fallbackBytes != null) return fallbackBytes;
                
                throw new Exception("Download BG Eraser gagal (Timeout) dan ekstraksi JS tidak menemukan gambar transparan.");
            }

            throw new Exception("Download BG Eraser gagal secara tidak terduga.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private async Task<byte[]?> ExtractImageViaJsAsync()
    {
        if (_page == null) return null;

        var candidateSrcs = await _page.EvaluateAsync<string[]>(@"
            () => {
                const imgs = [...document.images];
                const candidates = [];
                for (let i = 0; i < imgs.length; i++) {
                    const img = imgs[i];
                    if (!img || !img.src) continue;
                    const s = String(img.src).toLowerCase();
                    if (s.includes('svg') || s.includes('icon') || s.includes('logo')) continue;
                    const area = (img.naturalWidth || 0) * (img.naturalHeight || 0);
                    if (area < 10000) continue; // Abaikan gambar terlalu kecil
                    candidates.push({ src: img.src, area, idx: i });
                }
                candidates.sort((a, b) => b.area - a.area);
                return candidates.slice(0, 3).map(c => c.src);
            }
        ");

        if (candidateSrcs == null || candidateSrcs.Length == 0) return null;

        foreach (var resultSrc in candidateSrcs)
        {
            var base64Data = await _page.EvaluateAsync<string>(@"
                async (url) => {
                    try {
                        const response = await fetch(url);
                        const blob = await response.blob();
                        return await new Promise(resolve => {
                            const reader = new FileReader();
                            reader.onload = () => resolve(reader.result);
                            reader.onerror = () => resolve(null);
                            reader.readAsDataURL(blob);
                        });
                    } catch (e) { return null; }
                }
            ", resultSrc);

            if (string.IsNullOrEmpty(base64Data)) continue;

            var parts = base64Data.Split(',');
            if (parts.Length < 2) continue;

            var bytes = Convert.FromBase64String(parts[1]);

            if (IsPngSignature(bytes) && PngHasAlpha(bytes))
                return bytes;
        }

        // Kalau tidak ada yang transparan, kembalikan gambar terbesar (kandidat pertama) jika itu PNG
        if (candidateSrcs.Length > 0)
        {
            var fallbackBase64 = await _page.EvaluateAsync<string>(@"
                async (url) => {
                    try {
                        const response = await fetch(url);
                        const blob = await response.blob();
                        return await new Promise(resolve => {
                            const reader = new FileReader();
                            reader.onload = () => resolve(reader.result);
                            reader.onerror = () => resolve(null);
                            reader.readAsDataURL(blob);
                        });
                    } catch (e) { return null; }
                }
            ", candidateSrcs[0]);
            if (!string.IsNullOrEmpty(fallbackBase64))
            {
                var parts = fallbackBase64.Split(',');
                if (parts.Length >= 2)
                {
                     var bytes = Convert.FromBase64String(parts[1]);
                     if (IsPngSignature(bytes)) return bytes;
                }
            }
        }

        return null;
    }

    private static bool IsPngSignature(byte[] bytes)
    {
        return bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
    }

    private static bool PngHasAlpha(byte[] bytes)
    {
        if (bytes.Length < 33) return false;
        if (!(bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)) return false;

        int offset = 8;
        byte? colorType = null;

        while (offset + 8 <= bytes.Length)
        {
            int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            if (length < 0 || offset + 12 + length > bytes.Length) break;

            var type0 = bytes[offset + 4];
            var type1 = bytes[offset + 5];
            var type2 = bytes[offset + 6];
            var type3 = bytes[offset + 7];

            if (type0 == (byte)'t' && type1 == (byte)'R' && type2 == (byte)'N' && type3 == (byte)'S')
                return true;

            if (type0 == (byte)'I' && type1 == (byte)'H' && type2 == (byte)'D' && type3 == (byte)'R')
            {
                int ihdrDataStart = offset + 8;
                if (ihdrDataStart + 10 < bytes.Length)
                    colorType = bytes[ihdrDataStart + 9];
            }

            offset += 12 + length;
        }

        return colorType == 4 || colorType == 6;
    }

    /// <summary>Dismiss banner, popup iklan, atau dialog yang menghalangi.</summary>
    private async Task DismissPopupsAsync()
    {
        // 1. Google Vignette URL check (common for full-page ads)
        if (_page!.Url.Contains("#google_vignette"))
        {
            try
            {
                // Go back or reload usually clears vignette without losing state,
                // but clicking close is safer. Let's try to find the dismiss button in iframes.
                foreach (var frame in _page.Frames)
                {
                    var dismissBtn = frame.Locator("#dismiss-button, .ns-close-button, [aria-label=\"Close ad\"]").First;
                    if (await dismissBtn.CountAsync() > 0)
                    {
                        await dismissBtn.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                        break;
                    }
                }
            }
            catch { }
        }

        // 2. Nuke common overlay iframes just in case
        try
        {
            await _page.EvaluateAsync(@"() => {
                const iframes = document.querySelectorAll('iframe');
                iframes.forEach(f => {
                    if (f.src && (f.src.includes('google') || f.src.includes('ads'))) {
                        f.style.display = 'none';
                    }
                });
            }");
        }
        catch { }

        // 3. Normal banner close
        try
        {
            var bannerClose = _page.Locator("button.inline-block.focus\\:outline-none").First;
            if (await bannerClose.CountAsync() > 0 && await bannerClose.IsVisibleAsync())
                await bannerClose.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
        }
        catch { }

        // 4. "Close" text buttons (agresif)
        try
        {
            var closePopup = _page.Locator("text=\"Close\", [aria-label=\"Close\"]").First;
            if (await closePopup.CountAsync() > 0 && await closePopup.IsVisibleAsync())
                await closePopup.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
        }
        catch { }

        // 5. Generic modal close (X button)
        try
        {
            var modalClose = _page.Locator("div[role=\"dialog\"] button:first-child, .modal button:first-child").First;
            if (await modalClose.CountAsync() > 0 && await modalClose.IsVisibleAsync())
                await modalClose.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
        }
        catch { }
    }

    /// <summary>Reset halaman untuk proses gambar berikutnya.</summary>
    private async Task ClearPreviousResultAsync()
    {
        try
        {
            var clearBtn = _page!.Locator("button:has-text(\"Clear all\")").First;
            if (await clearBtn.CountAsync() > 0 && await clearBtn.IsVisibleAsync())
            {
                await clearBtn.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
                await Task.Delay(1000);
                await DismissPopupsAsync();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _context?.CloseAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}

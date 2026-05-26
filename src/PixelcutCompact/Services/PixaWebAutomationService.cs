using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System.Linq;
using Microsoft.Win32;

namespace PixelcutCompact.Services;

public class PixaWebAutomationService : IDisposable
{
    private readonly string? _proxyServer;
    private readonly bool _showBrowser;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _isInitialized;
    private string? _resolvedBrowserChannel;
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
    public bool ShowBrowser { get; set; }

    public PixaWebAutomationService(string? proxyServer = null, bool showBrowser = false)
    {
        _proxyServer = string.IsNullOrWhiteSpace(proxyServer) ? null : proxyServer.Trim();
        _showBrowser = showBrowser;
        ShowBrowser = showBrowser;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Ensure Playwright finds its driver when running as a single-file executable
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (exeDir != null)
            {
                // Find if .playwright folder is in the exe directory
                if (Directory.Exists(Path.Combine(exeDir, ".playwright")))
                {
                    Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_PATH", exeDir);
                }
                else
                {
                    // Fallback to AppContext BaseDirectory
                    Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_PATH", AppContext.BaseDirectory);
                }
            }
        }

        _playwright = await Playwright.CreateAsync();
        
        try 
        {
            await LaunchBrowserAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("Executable doesn't exist"))
        {
            // Install browsers if missing
            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0) throw new Exception("Gagal menginstal browser engine. Silakan cek koneksi internet.");
            
            await LaunchBrowserAsync();
        }

        _isInitialized = true;
    }
    private async Task LaunchBrowserAsync()
    {
        var preferredChannel = DetectDefaultBrowserChannel();
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserProfile");
        
        if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir);

        var baseArgs = new List<string>
        {
            "--disable-blink-features=AutomationControlled",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-web-security",
            "--mute-audio"
        };

        var opts = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = !_showBrowser,
            Channel = preferredChannel,
            Args = baseArgs,
            IgnoreDefaultArgs = new[] { "--enable-automation" },
            Proxy = string.IsNullOrWhiteSpace(_proxyServer) ? null : new Proxy { Server = _proxyServer },
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
        };

        try 
        {
            _context = await _playwright!.Chromium.LaunchPersistentContextAsync(userDataDir, opts);
            _resolvedBrowserChannel = preferredChannel ?? "chromium";
        }
        catch (Exception)
        {
            // Fallback jika browser channel bermasalah (misal edge/chrome tidak ada)
            if (!string.IsNullOrEmpty(preferredChannel))
            {
                opts.Channel = null;
                _context = await _playwright!.Chromium.LaunchPersistentContextAsync(userDataDir, opts);
                _resolvedBrowserChannel = "chromium";
            }
            else throw;
        }

        if (_context == null)
            throw new Exception("Browser context gagal dibuat.");

        await PixelcutCompact.Helpers.PlaywrightStealthHelper.ApplyStealthSettingsAsync(_context);

        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(180000);
    }

    private static string? DetectDefaultBrowserChannel()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;

            // Example:
            // HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice -> ProgId
            // MSEdgeHTM / ChromeHTML
            using var userChoice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");

            var progId = userChoice?.GetValue("ProgId")?.ToString()?.ToLowerInvariant() ?? "";

            if (progId.Contains("msedge")) return "msedge";
            if (progId.Contains("chrome")) return "chrome";
        }
        catch
        {
            // ignore, fallback to bundled Chromium
        }

        return null;
    }

    public async Task<byte[]> ProcessImageAsync(string filePath, string jobType, CancellationToken ct)
    {
        await InitializeAsync();
        if (_page == null) throw new Exception("Browser failed to initialize");

        // 1. Navigate to Pixa Batch Edit
        await _page.GotoAsync("https://www.pixa.com/t/batch-edit", new PageGotoOptions 
        { 
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 180000
        });

        // Diagnostic: Check for redirects (e.g. to login)
        if (_page.Url.Contains("/login") || _page.Url.Contains("/auth"))
            throw new Exception("Website Pixa meminta Login. Mode otomatis tidak bisa lanjut.");

        // Wait for the file input to appear (it might be dynamic)
        try 
        {
            await _page.WaitForSelectorAsync("input[type=\"file\"]", new PageWaitForSelectorOptions { Timeout = 10000 });
        }
        catch { }

        await Task.Delay(2000, ct);

        // 2. Upload File
        var fileInput = _page.Locator("input[type=\"file\"]").First;
        if (await fileInput.CountAsync() == 0)
        {
             // Try searching for any input file in case of iframes or dynamic IDs
             if (_page.Url.Contains("batch-edit"))
                 throw new Exception("Halaman editor terbuka tapi input file tidak ditemukan. Coba lagi.");
             else
                 throw new Exception($"Gagal memuat editor Pixa (URL saat ini: {_page.Url})");
        }

        await fileInput.SetInputFilesAsync(filePath);
        await Task.Delay(2000, ct);

        // 3. Click Remove Background
        // Wait a bit for the editor to load the image
        await Task.Delay(3000, ct);
        
        var removeBtn = _page.Locator("button:has-text(\"Remove Background\")").First;
        if (await removeBtn.CountAsync() == 0)
            removeBtn = _page.Locator("text=\"Remove Background\"").First;

        if (await removeBtn.CountAsync() > 0)
        {
            // Ensure button is ready
            await removeBtn.ScrollIntoViewIfNeededAsync();
            await removeBtn.ClickAsync(new LocatorClickOptions { Force = true });
        }

        // 4. Wait for AI Processing (Text 'Processing...' disappears)
        try
        {
            await _page.WaitForFunctionAsync(@"
                () => { 
                    const text = document.body.innerText;
                    return !text.includes('Processing...') && !text.includes('Working...');
                }
            ", null, new PageWaitForFunctionOptions { Timeout = 120000 });
        }
        catch { }

        // Extra wait for the image to actually render/swap in DOM
        await Task.Delay(5000, ct);

        // 5. Wait until processed image appears (same approach as remove v.2.py)
        try
        {
            await _page.WaitForFunctionAsync(@"
                () => {
                    const imgs = [...document.images];
                    return imgs.some(img => {
                        if(!img.src) return false;
                        const s = String(img.src).toLowerCase();
                        return (s.includes('pixelcut') || s.includes('pixa'));
                    });
                }
            ", null, new PageWaitForFunctionOptions { Timeout = 180000 });
        }
        catch { /* Best effort */ }

        // 6. Collect candidate result srcs (largest first, later DOM as tie-break).
        var candidateSrcs = await _page.EvaluateAsync<string[]>(@"
            () => {
                const imgs = [...document.images];
                const candidates = [];
                for (let i = 0; i < imgs.length; i++) {
                    const img = imgs[i];
                    if (!img || !img.src) continue;
                    const s = String(img.src).toLowerCase();
                    if (!s.includes('pixelcut') && !s.includes('pixa')) continue;
                    const area = (img.naturalWidth || 0) * (img.naturalHeight || 0);
                    candidates.push({ src: img.src, area, idx: i });
                }

                candidates.sort((a, b) => {
                    const areaDiff = b.area - a.area;
                    if (areaDiff !== 0) return areaDiff;
                    return b.idx - a.idx;
                });

                const out = [];
                const seen = new Set();
                for (const c of candidates) {
                    if (seen.has(c.src)) continue;
                    seen.add(c.src);
                    out.push(c.src);
                    if (out.length >= 8) break;
                }
                return out;
            }
        ");

        if (candidateSrcs == null || candidateSrcs.Length == 0)
            throw new Exception("Result image tidak ditemukan (candidate src kosong).");

        byte[]? firstPng = null;
        var debug = new List<string>();
        foreach (var resultSrc in candidateSrcs)
        {
            var base64Data = await _page.EvaluateAsync<string>(@"
                async (url) => {
                    const response = await fetch(url);
                    const blob = await response.blob();
                    return await new Promise(resolve => {
                        const reader = new FileReader();
                        reader.onload = () => { resolve(reader.result); };
                        reader.readAsDataURL(blob);
                    });
                }
            ", resultSrc);

            if (string.IsNullOrEmpty(base64Data))
                continue;

            var parts = base64Data.Split(',');
            if (parts.Length < 2)
                continue;

            var header = parts[0];
            var bytes = Convert.FromBase64String(parts[1]);
            var srcShort = resultSrc.Length > 72 ? (resultSrc.Substring(0, 72) + "...") : resultSrc;
            if (debug.Count < 4)
                debug.Add($"src={srcShort}, mime={header}");

            if (!IsPngSignature(bytes))
                continue;

            if (firstPng == null) firstPng = bytes;
            if (PngHasAlpha(bytes))
                return bytes;
        }

        if (firstPng != null)
            return firstPng;

        throw new Exception("Hasil terunduh bukan PNG (semua kandidat gagal). " + string.Join(" | ", debug));
    }

    private static bool IsPngSignature(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x89 &&
               bytes[1] == 0x50 &&
               bytes[2] == 0x4E &&
               bytes[3] == 0x47;
    }

    private static bool PngHasAlpha(byte[] bytes)
    {
        // PNG transparency can be encoded in two ways:
        // 1) via true alpha channel: color types 4 (grayscale+alpha) or 6 (RGBA)
        // 2) via tRNS chunk (palette/grayscale transparency without full alpha channel)
        if (bytes.Length < 33) return false;
        if (!(bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)) return false;

        // Signature (8 bytes) then: [length:4][type:4][data:length][crc:4] repeating
        // We'll parse chunks until we find IHDR or tRNS.
        int offset = 8;
        byte? colorType = null;

        while (offset + 8 <= bytes.Length)
        {
            int length = ReadUInt32BigEndian(bytes, offset);
            if (length < 0) break;
            if (offset + 12 + length > bytes.Length) break; // malformed

            var type0 = bytes[offset + 4];
            var type1 = bytes[offset + 5];
            var type2 = bytes[offset + 6];
            var type3 = bytes[offset + 7];

            // 'tRNS'
            if (type0 == (byte)'t' && type1 == (byte)'R' && type2 == (byte)'N' && type3 == (byte)'S')
                return true;

            // 'IHDR'
            if (type0 == (byte)'I' && type1 == (byte)'H' && type2 == (byte)'D' && type3 == (byte)'R')
            {
                // IHDR data layout (13 bytes):
                // width(4), height(4), bitDepth(1), colorType(1), compression(1), filter(1), interlace(1)
                // data start = offset + 8
                int ihdrDataStart = offset + 8;
                if (ihdrDataStart + 10 < bytes.Length)
                {
                    // colorType at dataStart + 9
                    colorType = bytes[ihdrDataStart + 9];
                }
            }

            offset += 12 + length;
        }

        // If there is a true alpha channel
        if (colorType == 4 || colorType == 6)
            return true;

        return false;
    }

    private static int ReadUInt32BigEndian(byte[] bytes, int offset)
    {
        // Safe-ish helper for PNG chunk parsing
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    public void Dispose()
    {
        _context?.CloseAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}

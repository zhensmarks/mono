using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace PixelcutCompact.Services;

public class PixaWebAutomationService : IDisposable
{
    // URL editor batch: satu halaman bisa menerima banyak file sekaligus.
    private const string BatchEditUrl = "https://www.pixelcut.ai/t/batch-edit";

    private readonly string? _proxyServer;
    private readonly bool _showBrowser;
    private readonly string? _profileSuffix;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private bool _isInitialized;
    // Guard init supaya aman saat beberapa file diproses paralel.
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _resolvedBrowserChannel;
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
    public bool ShowBrowser { get; set; }
    /// <summary>Nama akun yang memakai service ini (untuk pesan error limit).</summary>
    public string? AccountName { get; set; }
    /// <summary>Suffix folder profil browser akun ini (null = profil default).</summary>
    public string? ProfileSuffix => _profileSuffix;

    public PixaWebAutomationService(string? proxyServer = null, bool showBrowser = false, string? profileSuffix = null)
    {
        _proxyServer = string.IsNullOrWhiteSpace(proxyServer) ? null : proxyServer.Trim();
        _showBrowser = showBrowser;
        ShowBrowser = showBrowser;
        _profileSuffix = profileSuffix;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Lock: beberapa file bisa mulai bersamaan, browser context cukup dibuat sekali.
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            // Ensure Playwright finds its driver when running as a single-file executable
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var exeDir = Path.GetDirectoryName(exePath);
                if (exeDir != null)
                {
                    if (Directory.Exists(Path.Combine(exeDir, ".playwright")))
                    {
                        Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_PATH", exeDir);
                    }
                    else
                    {
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
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LaunchBrowserAsync()
    {
        var preferredChannel = DetectDefaultBrowserChannel();
        var profileName = string.IsNullOrEmpty(_profileSuffix) ? "BrowserProfile" : $"BrowserProfile_{_profileSuffix}";
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, profileName);

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

        // Catatan: page dibuat per-panggilan di ProcessImageAsync/ProcessBatchAsync
        // supaya beberapa file bisa dikerjakan bersamaan (tab terpisah).
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
        catch
        {
            // ignore, fallback to bundled Chromium
        }

        return null;
    }

    /// <summary>
    /// Proses SATU file (fallback / MixProxy). Upload 1 file ke 1 halaman.
    /// </summary>
    public async Task<byte[]> ProcessImageAsync(string filePath, string jobType, CancellationToken ct)
    {
        return await RunWithRetryAsync(async () =>
        {
            await InitializeAsync();
            if (_context == null) throw new Exception("Browser failed to initialize");

            // Satu page (tab browser) per file - aman dijalankan paralel.
            var page = await _context.NewPageAsync();
            page.SetDefaultTimeout(ComputeTimeoutMs(1));
            try
            {
                var results = await ProcessInPage(page, new[] { filePath }, ct);
                var first = results.Length > 0 ? results[0] : null;
                if (first == null) throw new Exception("Gagal memproses gambar: tidak ada output.");
                return first;
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
            }
        }, ct);
    }

    /// <summary>
    /// Proses BANYAK file sekaligus di satu halaman (fitur bawaan batch-edit).
    /// Hasil dikembalikan berurutan sesuai urutan input (null kalau gagal/tidak ketemu).
    /// </summary>
    public async Task<byte[]?[]> ProcessBatchAsync(IList<string> filePaths, string jobType, CancellationToken ct)
    {
        return await RunWithRetryAsync(async () =>
        {
            await InitializeAsync();
            if (_context == null) throw new Exception("Browser failed to initialize");

            var page = await _context.NewPageAsync();
            page.SetDefaultTimeout(ComputeTimeoutMs(filePaths.Count));
            try
            {
                return await ProcessInPage(page, filePaths, ct);
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
            }
        }, ct);
    }


    /// <summary>Timeout dinamis: basis 180s + 8s per gambar, dibatasi 30 menit.</summary>
    private static int ComputeTimeoutMs(int imageCount)
    {
        long ms = 180000L + 8000L * Math.Max(0, imageCount);
        if (ms > 1800000L) ms = 1800000L;
        return (int)ms;
    }

    /// <summary>Jalankan aksi; kalau target/tab ditutup (crash) coba sekali lagi dengan context fresh.</summary>
    private async Task<T> RunWithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (IsTargetClosed(ex) && !ct.IsCancellationRequested)
        {
            try { _context?.CloseAsync().GetAwaiter().GetResult(); } catch { }
            try { _playwright?.Dispose(); } catch { }
            _context = null;
            _playwright = null;
            _isInitialized = false;
            await InitializeAsync();
            return await action();
        }
    }

    private static bool IsTargetClosed(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            var m = e.Message ?? string.Empty;
            if (m.Contains("Target closed") ||
                m.Contains("Target page, context or browser has been closed") ||
                m.Contains("browser has been closed") ||
                m.Contains("has been closed"))
                return true;
        }
        return false;
    }

    /// <summary>Buka browser tampil (context persistent yang sama) ke URL tertentu, lalu tunggu window ditutup user.</summary>
    public async Task OpenInteractiveAsync(string url, CancellationToken ct)
    {
        if (_context == null)
        {
            _playwright = await Playwright.CreateAsync();
            await LaunchBrowserAsync();
            _isInitialized = true;
        }
        if (_context == null) throw new Exception("Browser failed to initialize");

        var page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120000 });
        }
        catch { }

        // Tunggu user menutup window (context close).
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClose(object? s, IBrowserContext e) => closed.TrySetResult(true);
        _context.Close += OnClose;
        try
        {
            await Task.WhenAny(closed.Task, Task.Delay(Timeout.Infinite, ct));
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { _context.Close -= OnClose; } catch { }
            _context = null;
            _isInitialized = false;
            try { _playwright?.Dispose(); } catch { }
            _playwright = null;
        }
    }

    /// <summary>Tutup context browser aktif (auto-close setelah antrian selesai).</summary>
    public async Task CloseAsync()
    {
        try { if (_context != null) await _context.CloseAsync(); } catch { }
        finally
        {
            _context = null;
            _isInitialized = false;
            try { _playwright?.Dispose(); } catch { }
            _playwright = null;
        }
    }
    /// <summary>
    /// Inti pemrosesan. Kalau filePaths berisi 1 file -> mode single.
    /// Kalau banyak -> upload semua sekaligus, klik Remove Background per gambar,
    /// lalu kumpulkan hasil berurutan sesuai urutan upload.
    /// </summary>
    private async Task<byte[]?[]> ProcessInPage(IPage page, IList<string> filePaths, CancellationToken ct)
    {
        var batchMode = filePaths.Count > 1;

        // 1. Navigate to batch edit
        await page.GotoAsync(BatchEditUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 180000
        });

        // Diagnostic: Check for redirects (e.g. to login)
        if (page.Url.Contains("/login") || page.Url.Contains("/auth"))
            throw new Exception("Website meminta Login. Mode otomatis tidak bisa lanjut.");

        // Wait for the file input to appear (it might be dynamic)
        try
        {
            await page.WaitForSelectorAsync("input[type=\"file\"]", new PageWaitForSelectorOptions { Timeout = 10000 });
        }
        catch { }

        await Task.Delay(2000, ct);

        // 2. Upload file(s) — kirim SEMUA sekaligus (ini kuncinya, bukan 1-1).
        var fileInput = page.Locator("input[type=\"file\"]").First;
        if (await fileInput.CountAsync() == 0)
        {
            if (page.Url.Contains("batch-edit"))
                throw new Exception("Halaman editor terbuka tapi input file tidak ditemukan. Coba lagi.");
            else
                throw new Exception($"Gagal memuat editor (URL saat ini: {page.Url})");
        }

        await fileInput.SetInputFilesAsync(filePaths.ToArray());
        await Task.Delay(2000, ct);

        // 2b. Mode batch: pilih semua elemen dulu. Tombol "Remove Background"
        //     di editor batch hanya benar-benar memproses kalau ada elemen terpilih.
        if (batchMode)
        {
            await SelectAllElementsAsync(page);
            await Task.Delay(1000, ct);
        }

        // 3. Klik "Remove Background". Batch editor bisa punya satu tombol global
        //    atau satu tombol per gambar — klik semuanya agar diproses bersamaan.
        await Task.Delay(3000, ct);

        var clicked = await ClickAllRemoveBackgroundButtons(page);
        if (!clicked)
        {
            // Fallback: cari teks saja (struktur lama).
            var textBtn = page.Locator("text=\"Remove Background\"").First;
            if (await textBtn.CountAsync() > 0)
            {
                await textBtn.ScrollIntoViewIfNeededAsync();
                await textBtn.ClickAsync(new LocatorClickOptions { Force = true });
            }
        }

        // 4. Tunggu sampai tidak ada lagi teks "Processing..."/"Working..."
        try
        {
            await page.WaitForFunctionAsync(@"
                () => {
                    const text = document.body.innerText;
                    return !text.includes('Processing...') && !text.includes('Working...');
                }
            ", null, new PageWaitForFunctionOptions { Timeout = ComputeTimeoutMs(filePaths.Count) });
        }
        catch { }

        // Extra wait for the images to actually render/swap in DOM
        await Task.Delay(5000, ct);

        // 4b. Deteksi indikasi limit akun (best-effort). Kalau terdeteksi, lempar
        //     exception khusus supaya rotator akun bisa pindah ke akun berikutnya.
        await DetectAccountLimitAsync(page);

        // 5. Tunggu muncul gambar hasil (src mengandung pixelcut/pixa).
        //    Mode batch: tunggu jumlahnya >= jumlah file yang diupload.
        try
        {
            await page.WaitForFunctionAsync(@"
                (expected) => {
                    const imgs = [...document.images];
                    let n = 0;
                    for (const img of imgs) {
                        if (!img.src) continue;
                        const s = String(img.src).toLowerCase();
                        if (s.includes('pixelcut') || s.includes('pixa')) n++;
                    }
                    return n >= expected;
                }
            ", filePaths.Count, new PageWaitForFunctionOptions { Timeout = ComputeTimeoutMs(filePaths.Count) });
        }
        catch { /* Best effort */ }

        if (!batchMode)
        {
            // Mode single: kumpulkan src hasil, pilih PNG terbaik (logika lama).
            var candidateSrcs = await page.EvaluateAsync<string[]>(@"
                () => {
                    const imgs = [...document.images];
                    const out = [];
                    const seen = new Set();
                    for (let i = 0; i < imgs.length; i++) {
                        const img = imgs[i];
                        if (!img || !img.src) continue;
                        const s = String(img.src).toLowerCase();
                        if (!s.includes('pixelcut') && !s.includes('pixa')) continue;
                        if (seen.has(img.src)) continue;
                        seen.add(img.src);
                        out.push(img.src);
                    }
                    return out;
                }
            ");

            if (candidateSrcs == null || candidateSrcs.Length == 0)
                return new byte[]?[1];

            var downloaded = new List<byte[]?>();
            foreach (var resultSrc in candidateSrcs)
                downloaded.Add(await DownloadSrcAsBytes(page, resultSrc));

            var picked = PickBestSingle(downloaded.Where(b => b != null).Select(b => b!).ToList());
            return new byte[]?[] { picked };
        }

        // 6. Mode batch: ambil hasil RESOLUSI PENUH per item.
        //    Grid thumbnail di batch editor hanya 1024px (inilah yang bikin hasil
        //    "terkompres"). Gambar full-res hanya muncul di canvas utama
        //    (img.object-contain) setelah item di-double-click. Jadi untuk tiap
        //    item: double-click di grid -> tunggu canvas memuat full-res -> ambil bytes.
        var result = new byte[]?[filePaths.Count];

        var elementIds = await page.EvaluateAsync<string[]>(@"
            () => [...document.querySelectorAll('[data-element-id]')]
                    .map(e => e.getAttribute('data-element-id'))
                    .filter(Boolean)
        ");

        if (elementIds == null || elementIds.Length == 0)
        {
            // Fallback: struktur berubah, pakai thumbnail grid berurutan seperti semula.
            var fallbackSrcs = await page.EvaluateAsync<string[]>(@"
                () => {
                    const imgs = [...document.images];
                    const out = [];
                    const seen = new Set();
                    for (const img of imgs) {
                        if (!img || !img.src) continue;
                        const s = String(img.src).toLowerCase();
                        if (!s.includes('pixelcut') && !s.includes('pixa')) continue;
                        if (seen.has(img.src)) continue;
                        seen.add(img.src);
                        out.push(img.src);
                    }
                    return out;
                }
            ");

            var fb = new List<byte[]?>();
            foreach (var src in fallbackSrcs)
                fb.Add(await DownloadSrcAsBytes(page, src));

            for (int i = 0; i < filePaths.Count && i < fb.Count; i++)
                result[i] = fb[i];
            return result;
        }

        int itemCount = Math.Min(elementIds.Length, filePaths.Count);
        for (int i = 0; i < itemCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                result[i] = await LoadFullResolutionForElementAsync(page, elementIds[i]);
            }
            catch { /* biarkan null, ditandai gagal di atas */ }
        }

        return result;
    }

    /// <summary>
    /// Deteksi indikasi akun kena limit dari teks halaman (best-effort).
    /// Kalau ragu, TIDAK dianggap limit supaya tidak salah pindah akun.
    /// </summary>
    private async Task DetectAccountLimitAsync(IPage page)
    {
        string bodyText;
        try
        {
            bodyText = await page.EvaluateAsync<string>("() => (document.body && document.body.innerText) ? document.body.innerText : ''");
        }
        catch
        {
            return; // Best effort: gagal baca halaman -> jangan anggap limit.
        }

        if (string.IsNullOrWhiteSpace(bodyText)) return;

        var text = bodyText.ToLowerInvariant();
        var keywords = new[]
        {
            "out of credits",
            "no credits left",
            "reached your limit",
            "reached your free",
            "daily limit",
            "limit reached",
            "you've reached",
            "quota exceeded",
            "upgrade to continue",
            "upgrade your plan",
            "free plan limit",
            "sign up to continue",
            "limit exceeded",
            "try again later",
            "too many requests"
        };

        foreach (var kw in keywords)
        {
            if (text.Contains(kw))
            {
                throw new PixaAccountLimitException(
                    AccountName ?? "akun",
                    $"Limit terdeteksi di halaman (\"{kw}\").");
            }
        }
    }

    /// <summary>
    /// Pilih semua elemen di editor batch. Tombol "Remove Background" hanya
    /// memproses kalau minimal ada satu elemen terpilih.
    /// </summary>
    private static async Task SelectAllElementsAsync(IPage page)
    {
        try
        {
            var els = page.Locator("[data-element-id]");
            var count = await els.CountAsync();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    await els.Nth(i).ClickAsync(new LocatorClickOptions
                    {
                        Force = true,
                        Position = new Position { X = 5, Y = 5 }
                    });
                    await Task.Delay(250);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Ambil gambar hasil RESOLUSI PENUH untuk satu item batch.
    /// Item di-double-click di grid supaya gambar full-res dimuat ke canvas utama,
    /// lalu bytes diambil dari &lt;img.object-contain&gt;.
    /// </summary>
    private static async Task<byte[]?> LoadFullResolutionForElementAsync(IPage page, string elementId)
    {
        // Catat src canvas sebelum dipilih, supaya bisa deteksi perubahan.
        var beforeSrc = await page.EvaluateAsync<string?>(@"
            () => { const im = document.querySelector('img.object-contain'); return im ? im.src : null; }
        ");

        // Double-click elemen (mousedown+mouseup+click+dblclick) -> muat full-res ke canvas.
        var changed = await page.EvaluateAsync<bool>(@"
            (eid) => {
                const el = document.querySelector('[data-element-id=""' + eid + '""]');
                if (!el) return false;
                const r = el.getBoundingClientRect();
                const opt = {
                    bubbles: true, cancelable: true, view: window,
                    clientX: r.x + r.width / 2, clientY: r.y + r.height / 2, button: 0
                };
                el.dispatchEvent(new MouseEvent('mousedown', opt));
                el.dispatchEvent(new MouseEvent('mouseup', opt));
                el.dispatchEvent(new MouseEvent('click', opt));
                el.dispatchEvent(new MouseEvent('dblclick', opt));
                return true;
            }
        ", elementId);

        if (!changed) return null;

        // Tunggu canvas memuat gambar (src berubah atau minimal naturalWidth besar).
        try
        {
            await page.WaitForFunctionAsync(@"
                (prev) => {
                    const im = document.querySelector('img.object-contain');
                    if (!im || !im.src || !im.complete) return false;
                    if (im.naturalWidth === 0) return false;
                    return im.src !== prev;
                }
            ", beforeSrc, new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch { /* best effort; tetap coba ambil */ }

        await Task.Delay(600);

        // Ambil bytes dari canvas utama (full-res), validasi PNG.
        var base64 = await page.EvaluateAsync<string?>(@"
            async () => {
                const im = document.querySelector('img.object-contain');
                if (!im || !im.src) return null;
                const response = await fetch(im.src);
                const blob = await response.blob();
                return await new Promise(resolve => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => resolve(null);
                    reader.readAsDataURL(blob);
                });
            }
        ");

        if (string.IsNullOrEmpty(base64)) return null;

        var parts = base64.Split(',');
        if (parts.Length < 2) return null;

        var bytes = Convert.FromBase64String(parts[1]);
        if (!IsPngSignature(bytes)) return null;
        return bytes;
    }

    /// <summary>Klik semua tombol "Remove Background" (global atau per gambar).</summary>
    private static async Task<bool> ClickAllRemoveBackgroundButtons(IPage page)
    {
        try
        {
            var buttons = page.Locator("button:has-text(\"Remove Background\")");
            var count = await buttons.CountAsync();
            if (count == 0) return false;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var btn = buttons.Nth(i);
                    await btn.ScrollIntoViewIfNeededAsync();
                    await btn.ClickAsync(new LocatorClickOptions { Force = true });
                    await Task.Delay(400);
                }
                catch { }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Fetch URL gambar lewat page, kembalikan bytes (null kalau gagal/bukan PNG).</summary>
    private static async Task<byte[]?> DownloadSrcAsBytes(IPage page, string resultSrc)
    {
        try
        {
            var base64Data = await page.EvaluateAsync<string>(@"
                async (url) => {
                    const response = await fetch(url);
                    const blob = await response.blob();
                    return await new Promise(resolve => {
                        const reader = new FileReader();
                        reader.onload = () => { resolve(reader.result); };
                        reader.onerror = () => { resolve(null); };
                        reader.readAsDataURL(blob);
                    });
                }
            ", resultSrc);

            if (string.IsNullOrEmpty(base64Data)) return null;

            var parts = base64Data.Split(',');
            if (parts.Length < 2) return null;

            var bytes = Convert.FromBase64String(parts[1]);
            if (!IsPngSignature(bytes)) return null;
            return bytes;
        }
        catch { return null; }
    }

    /// <summary>Pilih PNG terbaik untuk mode single: utamakan yang punya alpha channel.</summary>
    private static byte[]? PickBestSingle(List<byte[]> candidates)
    {
        if (candidates.Count == 0) return null;
        byte[]? firstPng = null;
        foreach (var bytes in candidates)
        {
            if (firstPng == null) firstPng = bytes;
            if (PngHasAlpha(bytes)) return bytes;
        }
        return firstPng;
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

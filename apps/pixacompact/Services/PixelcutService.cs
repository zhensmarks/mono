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

    public bool UseWebMode { get; set; } = true;
    public string RemoveBgEngine { get; set; } = "PIXA";
    public string RembgModel { get; set; } = "u2netp";
    public string? RembgExecutablePath { get; set; }
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
    public bool ShowBrowser { get; set; }
    /// <summary>Aktifkan rotasi round-robin antar akun pixelcut.</summary>
    public bool UseAccountRotation { get; set; }
    /// <summary>Pengelola rotasi akun (di-set dari ViewModel). Null = rotasi nonaktif.</summary>
    public PixaAccountRotator? AccountRotator { get; set; }
    /// <summary>Browser per akun (keyed by ProfileSuffix) supaya tiap akun punya sesi sendiri.</summary>
    private readonly Dictionary<string, PixaWebAutomationService> _accountBrowsers = new();
    /// <summary>Ukuran batch (jumlah file per kirim). Default 100 = limit akun gratis pixelcut.</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>Ukuran sub-batch per tab (batch besar dipecah supaya tidak crash).</summary>
    public int BatchSubSize { get; set; } = 25;

    /// <summary>
    /// Callback pelaporan progres per sub-batch supaya UI hanya menandai item
    /// yang SEDANG diproses (bukan semua item sekaligus). Menerima daftar item
    /// aktif pada sub-batch berjalan, atau null bila selesai/di luar sub-batch.
    /// Dipanggil dari thread background; penerima wajib marshalling ke UI thread.
    /// </summary>
    public Action<IReadOnlyList<PixelcutFileItem>?>? OnSubBatchActiveChanged { get; set; }

    /// <summary>
    /// Callback real-time saat satu item selesai diproses (berhasil/gagal).
    /// Parameter: item, resultPath (null=gagal), resultSize (0=gagal).
    /// Dipanggil dari thread background; penerima wajib marshalling ke UI thread.
    /// </summary>
    public Action<PixelcutFileItem, string?, long>? OnItemCompleted { get; set; }

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

        DisposeAccountBrowsers();
    }

    /// <summary>Tutup browser PIXA yang sedang aktif (dipakai auto-close setelah antrian selesai).</summary>
    public async Task CloseWebAutomationAsync()
    {
        var svc = _webAutomation;
        if (svc != null)
        {
            try { await svc.CloseAsync(); } catch { }
        }

        foreach (var kv in _accountBrowsers)
        {
            try { await kv.Value.CloseAsync(); } catch { }
        }
    }

    /// <summary>Dispose & hapus semua browser akun.</summary>
    private void DisposeAccountBrowsers()
    {
        foreach (var kv in _accountBrowsers)
        {
            try { kv.Value.Dispose(); } catch { }
        }
        _accountBrowsers.Clear();
    }

    /// <summary>
    /// Ambil (atau buat) browser untuk akun tertentu. Tiap akun punya folder profil
    /// browser sendiri (ProfileSuffix) sehingga sesi login tidak saling menimpa.
    /// </summary>
    private PixaWebAutomationService GetBrowserForAccount(PixaAccount account)
    {
        var suffix = account.EnsureProfileSuffix();
        if (!_accountBrowsers.TryGetValue(suffix, out var svc) || svc == null)
        {
            svc = new PixaWebAutomationService(null, ShowBrowser, suffix)
            {
                AccountName = account.Name
            };
            _accountBrowsers[suffix] = svc;
        }
        return svc;
    }

    /// <summary>Buka browser tampil untuk login manual akun tertentu (sesi tersimpan di profil akun).</summary>
    public async Task LoginAccountAsync(PixaAccount account, string url, CancellationToken ct)
    {
        var svc = GetBrowserForAccount(account);
        await svc.OpenInteractiveAsync(url, ct);
        // Setelah browser login ditutup, service sudah melepas context; buang dari cache.
        if (_accountBrowsers.TryGetValue(account.EnsureProfileSuffix(), out var cached) && ReferenceEquals(cached, svc))
        {
            try { svc.Dispose(); } catch { }
            _accountBrowsers.Remove(account.EnsureProfileSuffix());
        }
    }

    /// <summary>Buka browser tampil untuk login manual (sesi tersimpan di profil persistent).</summary>
    public async Task OpenInteractiveAsync(string url, CancellationToken ct)
    {
        if (_webAutomation == null)
        {
            _webAutomation = new PixaWebAutomationService(null, true);
        }
        await _webAutomation.OpenInteractiveAsync(url, ct);
    }

    /// <summary>Apakah rotasi akun sedang aktif dan punya akun yang bisa dipakai.</summary>
    private bool RotationActive => UseAccountRotation && AccountRotator != null && AccountRotator.Accounts.Count > 0;

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

    /// <summary>
    /// Proses batch file sekaligus (khusus engine PIXA/web mode).
    /// Memecah item jadi sub-batch kecil (BatchSubSize) supaya tiap tab tidak menahan
    /// terlalu banyak gambar sekaligus. Hasil tiap sub-batch LANGSUNG ditulis ke disk
    /// sebelum lanjut, sehingga 1 crash tidak menghilangkan hasil yang sudah didapat.
    /// Sub-batch yang gagal diulang sekali; item yang masih null dicoba per-file.
    /// </summary>
    public async Task ProcessBatchAsync(IList<PixelcutFileItem> items, string jobType, CancellationToken ct)
    {
        if (items.Count == 0) return;

        bool canBatch = string.Equals(jobType, "remove_bg", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(RemoveBgEngine, "PIXA", StringComparison.OrdinalIgnoreCase);

        // Ukuran batch efektif. Server pixelcut membatasi 100 gambar/sesi untuk akun gratis.
        int chunkSize = Math.Max(1, Math.Min(BatchSize, 10000));
        // Sub-batch: tiap tab hanya menahan sedikit gambar supaya tidak crash.
        int subSize = Math.Max(1, Math.Min(BatchSubSize, chunkSize));

        if (!canBatch || MixProxyEnabled)
        {
            // Fallback: proses satu-satu (dipanggil paralel dari luar via Task.Run).
            // Tetap laporkan item aktif per-langkah supaya UI konsisten (spinner
            // hanya pada item yang sedang dikerjakan).
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                OnSubBatchActiveChanged?.Invoke(new[] { item });
                try
                {
                    await ProcessImageAsync(item, jobType, ct);
                    var rp = ResolveResultPath(item, jobType);
                    if (File.Exists(rp))
                        OnItemCompleted?.Invoke(item, rp, new FileInfo(rp).Length);
                    else
                        OnItemCompleted?.Invoke(item, null, 0);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    OnItemCompleted?.Invoke(item, null, 0);
                }
            }
            OnSubBatchActiveChanged?.Invoke(null);
            return;
        }

        if (!RotationActive && _webAutomation == null)
        {
            _webAutomation = new PixaWebAutomationService(null, ShowBrowser);
            await _webAutomation.InitializeAsync();
        }

        int total = items.Count;
        int failed = 0;

        // Bagi item jadi kelompok, lalu tiap kelompok dipecah lagi
        for (int start = 0; start < total; start += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int end = Math.Min(start + chunkSize, total);

            for (int sub = start; sub < end; sub += subSize)
            {
                ct.ThrowIfCancellationRequested();
                int subEnd = Math.Min(sub + subSize, end);
                var chunk = items.Skip(sub).Take(subEnd - sub).ToList();
                var filePaths = chunk.Select(i => i.FilePath).ToList();

                // Tandai item sub-batch ini sebagai aktif (sedang diproses) supaya
                // UI hanya menyalakan spinner/progress pada item yang benar-benar
                // sedang dikerjakan, bukan seluruh antrian sekaligus.
                OnSubBatchActiveChanged?.Invoke(chunk);

                byte[]?[] results;
                if (RotationActive)
                {
                    // Rotasi aktif: tiap sub-batch memakai akun bergiliran, dan otomatis
                    // pindah akun saat limit (proaktif counter / reaktif deteksi halaman).
                    results = await RunSubBatchRotatingAsync(chunk, jobType, ct);
                }
                else
                {
                    results = await RunSubBatchSafeAsync(filePaths, jobType, ct);

                    // Kalau semua null, ulangi sub-batch ini sekali lagi.
                    if (results.All(r => r == null || r.Length == 0) && !ct.IsCancellationRequested)
                    {
                        results = await RunSubBatchSafeAsync(filePaths, jobType, ct);
                    }
                }

                // Tulis hasil sub-batch ini ke disk SEGERA (sebelum sub-batch berikutnya).
                for (int i = 0; i < chunk.Count; i++)
                {
                    var item = chunk[i];
                    if (i < results.Length && results[i] != null && results[i]!.Length > 0)
                    {
                        try
                        {
                            var rp = ResolveResultPath(item, jobType);
                            await File.WriteAllBytesAsync(rp, results[i]!, ct);
                            // Tandai selesai real-time segera setelah file ditulis.
                            OnItemCompleted?.Invoke(item, rp, results[i]!.Length);
                            continue;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }

                    // Hasil kosong - proses ulang item ini satu-satu.
                    try
                    {
                        await ProcessImageAsync(item, jobType, ct);
                        // ProcessImageAsync menulis ke disk sendiri; cek hasilnya.
                        var rpFallback = ResolveResultPath(item, jobType);
                        if (File.Exists(rpFallback))
                            OnItemCompleted?.Invoke(item, rpFallback, new FileInfo(rpFallback).Length);
                        else
                            OnItemCompleted?.Invoke(item, null, 0);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        failed++;
                        OnItemCompleted?.Invoke(item, null, 0);
                    }
                }

                // Sub-batch selesai: tidak ada item aktif sampai sub-batch berikutnya.
                OnSubBatchActiveChanged?.Invoke(null);
            }
        }

        // Tidak ada sub-batch aktif lagi setelah seluruh batch selesai.
        OnSubBatchActiveChanged?.Invoke(null);

        if (failed > 0 && failed == total)
        {
            throw new Exception($"Batch gagal: tidak ada hasil untuk semua {total} file.");
        }
    }

    /// <summary>Jalankan satu sub-batch; kalau context crash, buat browser baru lalu ulang sub-batch ini.</summary>
    private async Task<byte[]?[]> RunSubBatchSafeAsync(List<string> filePaths, string jobType, CancellationToken ct)
    {
        try
        {
            return await _webAutomation!.ProcessBatchAsync(filePaths, jobType, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Browser mungkin sudah mati; reset supaya sub-batch berikutnya fresh.
            try { _webAutomation?.Dispose(); } catch { }
            _webAutomation = null;
            try
            {
                _webAutomation = new PixaWebAutomationService(null, ShowBrowser);
                await _webAutomation.InitializeAsync();
                return await _webAutomation.ProcessBatchAsync(filePaths, jobType, ct);
            }
            catch
            {
                return new byte[]?[filePaths.Count];
            }
        }
    }

    /// <summary>
    /// Sisa kuota akun pada "ronde" berjalan. Kalau counter sudah mencapai batas,
    /// mulai ronde baru untuk akun ini (wrap) supaya rotasi bisa berputar terus
    /// sesuai skenario 5/5/2 (2 akun, limit 5, 12 file).
    /// </summary>
    private static int RemainingQuota(PixaAccount account)
    {
        int cap = Math.Max(1, account.MaxImagesPerSession);
        int used = account.ImagesUsedThisSession;
        if (used >= cap)
        {
            // Ronde baru untuk akun ini; counter di-wrap agar tetap dalam rentang 0..cap.
            used %= cap;
            account.ImagesUsedThisSession = used;
        }
        return cap - used;
    }

    /// <summary>
    /// Jalankan satu sub-batch dengan rotasi akun. Potong sub-batch sesuai sisa kuota
    /// tiap akun, bergiliran round-robin. Kalau akun kena limit (deteksi halaman),
    /// tandai limit lalu ulangi bagian yang sama dengan akun berikutnya. Kalau semua
    /// akun limit, lempar error yang jelas.
    /// </summary>
    private async Task<byte[]?[]> RunSubBatchRotatingAsync(IList<PixelcutFileItem> chunk, string jobType, CancellationToken ct)
    {
        var rotator = AccountRotator!;
        var results = new byte[]?[chunk.Count];
        int index = 0;
        int guard = 0;
        int maxGuard = Math.Max(8, chunk.Count * (rotator.Accounts.Count + 1));

        while (index < chunk.Count)
        {
            ct.ThrowIfCancellationRequested();
            if (++guard > maxGuard)
            {
                throw new Exception("Semua akun mencapai limit. Tambah akun lagi atau tunggu limit reset.");
            }

            var account = rotator.CurrentOrNext();
            if (account == null)
            {
                throw new Exception("Semua akun mencapai limit. Tambah akun lagi atau tunggu limit reset.");
            }

            // Proaktif: ambil bagian sebesar sisa kuota akun ini.
            int take = Math.Min(RemainingQuota(account), chunk.Count - index);
            var segment = chunk.Skip(index).Take(take).ToList();
            var paths = segment.Select(i => i.FilePath).ToList();

            var browser = GetBrowserForAccount(account);
            try
            {
                var segResults = await browser.ProcessBatchAsync(paths, jobType, ct);

                bool allFailed = segResults.All(r => r == null || r.Length == 0);
                if (allFailed && !ct.IsCancellationRequested)
                {
                    // Bukan limit eksplisit; coba sekali lagi dengan akun yang sama.
                    segResults = await browser.ProcessBatchAsync(paths, jobType, ct);
                    allFailed = segResults.All(r => r == null || r.Length == 0);
                }

                if (allFailed)
                {
                    // Akun ini dianggap bermasalah/limit — pindah ke akun berikutnya.
                    rotator.MarkLimited(account, "Tidak ada hasil dari akun ini");
                    try { browser.Dispose(); } catch { }
                    _accountBrowsers.Remove(account.EnsureProfileSuffix());
                    continue;
                }

                for (int i = 0; i < take; i++) results[index + i] = segResults[i];
                account.ImagesUsedThisSession += take;
                index += take;

                // Proaktif: kalau kuota akun ini habis, giliran akun berikutnya.
                if (account.ImagesUsedThisSession >= account.MaxImagesPerSession)
                {
                    rotator.Next();
                }
            }
            catch (PixaAccountLimitException ex)
            {
                rotator.MarkLimited(account, ex.Message);
                // buang browser akun yang limit supaya sesi fresh kalau dipakai lagi nanti.
                try { browser.Dispose(); } catch { }
                _accountBrowsers.Remove(account.EnsureProfileSuffix());
                continue;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Error lain (mis. browser crash): tandai akun bermasalah lalu coba akun berikutnya.
                rotator.MarkLimited(account, "Browser akun gagal");
                try { browser.Dispose(); } catch { }
                _accountBrowsers.Remove(account.EnsureProfileSuffix());
                continue;
            }
        }

        return results;
    }

    private async Task ProcessViaWebModeAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;

        if (RotationActive)
        {
            resultBytes = await ProcessSingleWithRotationAsync(item, job, ct);
        }
        else if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new PixaWebAutomationService(proxy, ShowBrowser, Guid.NewGuid().ToString("N").Substring(0, 8));
                await svc.InitializeAsync();
                return await svc.ProcessImageAsync(item.FilePath, job, ct);
            });
        }
        else
        {
            if (_webAutomation == null)
            {
                _webAutomation = new PixaWebAutomationService(null, ShowBrowser);
                await _webAutomation.InitializeAsync();
            }
            resultBytes = await _webAutomation.ProcessImageAsync(item.FilePath, job, ct);
        }

        // BUG FIX: hormati nama output yang sudah di-resolve unik saat scan (anti tabrakan).
        string resultPath = ResolveResultPath(item, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes);
    }

    /// <summary>Proses satu file dengan rotasi akun; pindah akun saat limit.</summary>
    private async Task<byte[]> ProcessSingleWithRotationAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        var rotator = AccountRotator!;
        int maxAttempts = Math.Max(1, rotator.Accounts.Count + 1);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var account = rotator.CurrentOrNext();
            if (account == null)
                throw new Exception("Semua akun mencapai limit. Tambah akun lagi atau tunggu limit reset.");

            // Proaktif: kalau kuota akun ini sudah habis, giliran akun berikutnya (wrap).
            if (account.ImagesUsedThisSession >= account.MaxImagesPerSession)
            {
                account.ImagesUsedThisSession %= Math.Max(1, account.MaxImagesPerSession);
                rotator.Next();
                continue;
            }

            var browser = GetBrowserForAccount(account);
            try
            {
                var bytes = await browser.ProcessImageAsync(item.FilePath, job, ct);
                account.ImagesUsedThisSession++;
                // Proaktif: kuota akun ini habis → giliran akun berikutnya.
                if (account.ImagesUsedThisSession >= account.MaxImagesPerSession)
                {
                    rotator.Next();
                }
                return bytes;
            }
            catch (PixaAccountLimitException ex)
            {
                rotator.MarkLimited(account, ex.Message);
                try { browser.Dispose(); } catch { }
                _accountBrowsers.Remove(account.EnsureProfileSuffix());
                continue;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                rotator.MarkLimited(account, "Browser akun gagal");
                try { browser.Dispose(); } catch { }
                _accountBrowsers.Remove(account.EnsureProfileSuffix());
                continue;
            }
        }

        throw new Exception("Semua akun mencapai limit. Tambah akun lagi atau tunggu limit reset.");
    }

    private async Task ProcessViaRembgAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        var resultPath = ResolveResultPath(item, job);
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
                using var svc = new NobgSpaceWebAutomationService(proxy, ShowBrowser, Guid.NewGuid().ToString("N").Substring(0, 8));
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

        var resultPath = ResolveResultPath(item, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes, ct);
    }

    private async Task ProcessViaRembgOnlineAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new RembgOnlineWebAutomationService(proxy, ShowBrowser, Guid.NewGuid().ToString("N").Substring(0, 8));
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

        var resultPath = ResolveResultPath(item, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes, ct);
    }

    private async Task ProcessViaBgEraserAsync(PixelcutFileItem item, string job, CancellationToken ct)
    {
        byte[] resultBytes;
        if (MixProxyEnabled)
        {
            resultBytes = await ProcessWithProxyRetriesAsync(async proxy =>
            {
                using var svc = new BgEraserWebAutomationService(proxy, ShowBrowser, Guid.NewGuid().ToString("N").Substring(0, 8));
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

        var resultPath = ResolveResultPath(item, job);
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
    /// <summary>Pakai ExpectedResultPath item (diresolve unik saat scan) kalau ada; fallback ke GetResultPath.</summary>
    private string ResolveResultPath(PixelcutFileItem item, string job)
    {
        return !string.IsNullOrWhiteSpace(item.ExpectedResultPath)
            ? item.ExpectedResultPath
            : GetResultPath(item.FilePath, job);
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
        DisposeAccountBrowsers();
    }
}

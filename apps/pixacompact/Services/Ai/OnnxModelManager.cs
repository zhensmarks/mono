using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PixelcutCompact.Services;

namespace PixelcutCompact.Services.Ai;

/// <summary>
/// Spesifikasi satu model ONNX matting (untuk Refine Hair).
/// </summary>
public sealed class MattingModelSpec
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required string License { get; init; }

    /// <summary>Nama file relatif di Resources/AiModels.</summary>
    public string FileName => Id + ".onnx";

    /// <summary>Ukuran sisi masukan. 0 = "shortest edge" (lihat <see cref="ShortestEdge"/>).</summary>
    public int InputSize { get; init; }

    /// <summary>Bila &gt; 0, resize ke shortest-edge ini (mode MODNet).</summary>
    public int ShortestEdge { get; init; }

    /// <summary>Pembagi ukuran (pad) — mis. 32 untuk MODNet.</summary>
    public int SizeDivisibility { get; init; } = 1;

    /// <summary>Mode resize persegi (true) vs shortest-edge (false).</summary>
    public bool SquareResize => InputSize > 0;

    /// <summary>Mean normalisasi per kanal RGB.</summary>
    public required double[] Mean { get; init; }

    /// <summary>Std normalisasi per kanal RGB.</summary>
    public required double[] Std { get; init; }

    /// <summary>True bila model menerima trimap sebagai input kedua.</summary>
    public bool AcceptsTrimap { get; init; }

    /// <summary>Catatan tambahan untuk tooltip.</summary>
    public string Notes { get; init; } = "";
}

/// <summary>
/// Manifest model matting yang didukung, plus unduh/cache/verifikasi.
/// Mengikuti pola <see cref="RembgResourceManager"/> untuk progres unduhan.
/// </summary>
public static class OnnxModelManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>Per-user writable model cache; never writes into the app install folder.</summary>
    public static string ModelsDirectory
    {
        get
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(localData, "PixaCompact", "AiModels");
        }
    }

    private static string CachedPathFor(MattingModelSpec spec) => Path.Combine(ModelsDirectory, spec.FileName);

    private static IEnumerable<string> CandidatePaths(MattingModelSpec spec)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", "AiModels", spec.FileName);
        yield return bundled;
        var cached = CachedPathFor(spec);
        if (!string.Equals(bundled, cached, StringComparison.OrdinalIgnoreCase)) yield return cached;
    }

    public static string PathFor(MattingModelSpec spec)
    {
        foreach (var path in CandidatePaths(spec))
            if (IsVerifiedFile(spec, path)) return path;
        return CachedPathFor(spec);
    }

    /// <summary>Model default (MODNet) — cepat, 24.7 MiB, Apache-2.0.</summary>
    public static MattingModelSpec ModNet { get; } = new()
    {
        Id = "modnet",
        DisplayName = "MODNet (cepat, 24.7 MiB)",
        // Pinned Xenova ONNX conversion of upstream ZHKKKe/MODNet. This is a community
        // conversion (not an upstream ONNX release); the exact Hub LFS SHA-256 is verified.
        Url = "https://huggingface.co/Xenova/modnet/resolve/fa2fa546052fba4c08921230a26cc69a333fca12/onnx/model.onnx",
        Sha256 = "07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9",
        SizeBytes = 25_888_640,
        License = "Apache-2.0",
        ShortestEdge = 512,
        SizeDivisibility = 32,
        Mean = new[] { 0.5, 0.5, 0.5 },
        Std = new[] { 0.5, 0.5, 0.5 },
        AcceptsTrimap = false,
        Notes = "Xenova/modnet community ONNX conversion, pinned to Hub revision fa2fa546; upstream architecture is ZHKKKe/MODNet. Resize shortest-edge 512, rounded to a multiple of 32."
    };

    /// <summary>Model kualitas maksimum (BiRefNet_lite) — 213 MB, MIT.</summary>
    public static MattingModelSpec BiRefNetLite { get; } = new()
    {
        Id = "birefnet_lite",
        DisplayName = "BiRefNet lite (kualitas maksimum, 214 MB)",
        // SHA-256 remains blank until this exact upstream artifact is independently reviewed.
        Url = "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/main/onnx/model.onnx",
        Sha256 = "",
        SizeBytes = 224_017_000,
        License = "MIT",
        InputSize = 1024,
        SizeDivisibility = 1,
        Mean = new[] { 0.485, 0.456, 0.406 },
        Std = new[] { 0.229, 0.224, 0.225 },
        AcceptsTrimap = false,
        Notes = "Resize 1024x1024 persegi, normalisasi ImageNet."
    };

    public static IReadOnlyList<MattingModelSpec> All { get; } = new[] { ModNet, BiRefNetLite };

    public static MattingModelSpec? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var m in All)
            if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    /// <summary>True only when a bundled or cached model matches its pinned SHA-256.</summary>
    public static bool HasVerifiedSha256(MattingModelSpec? spec)
        => spec?.Sha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit);

    /// <summary>True only when the artifact length exactly matches its pinned manifest size.</summary>
    public static bool HasExpectedSize(MattingModelSpec? spec, long fileSizeBytes)
        => spec is not null && spec.SizeBytes > 0 && fileSizeBytes == spec.SizeBytes;

    private static bool IsVerifiedFile(MattingModelSpec spec, string path)
    {
        if (!HasVerifiedSha256(spec) || !File.Exists(path)) return false;
        try
        {
            if (!HasExpectedSize(spec, new FileInfo(path).Length)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 20, FileOptions.SequentialScan);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, spec.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsInstalled(MattingModelSpec spec)
    {
        if (!HasVerifiedSha256(spec)) return false;
        return CandidatePaths(spec).Any(path => IsVerifiedFile(spec, path));
    }

    /// <summary>Hapus model dari disk. Mengembalikan true bila ada yang dihapus.</summary>
    public static bool Uninstall(MattingModelSpec spec)
    {
        var path = CachedPathFor(spec);
        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
        }
        catch { /* diamkan: file mungkin terkunci */ }
        return false;
    }

    /// <summary>
    /// Pastikan model tersedia. Bila belum ada, unduh ke &lt;file&gt;.part lalu
    /// verifikasi ukuran tepat dan SHA-256 sebelum memindahkan ke nama final.
    /// </summary>
    public static async Task EnsureAvailableAsync(MattingModelSpec spec,
        IProgress<InstallProgressInfo>? progress, CancellationToken ct)
    {
        if (!HasVerifiedSha256(spec))
            throw new InvalidOperationException(
                $"Model {spec.DisplayName} has no valid pinned SHA-256 checksum; refusing to download or run unverified weights.");
        if (spec.SizeBytes <= 0)
            throw new InvalidOperationException($"Model {spec.DisplayName} has no valid expected file size.");

        if (IsInstalled(spec))
        {
            progress?.Report(new InstallProgressInfo { Percentage = 100, Message = "Model siap" });
            return;
        }

        var finalPath = CachedPathFor(spec);

        Directory.CreateDirectory(ModelsDirectory);
        var partPath = finalPath + ".part";

        progress?.Report(new InstallProgressInfo
        {
            Percentage = 0,
            Message = $"Mengunduh {spec.DisplayName}..."
        });

        try
        {
            using var resp = await Http.GetAsync(spec.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var contentLength = resp.Content.Headers.ContentLength;
            if (contentLength.HasValue && !HasExpectedSize(spec, contentLength.Value))
                throw new InvalidDataException(
                    $"Model response length mismatch: expected {spec.SizeBytes} bytes, received {contentLength.Value}.");
            long total = spec.SizeBytes;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                int lastPct = -1;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    if (read + n > spec.SizeBytes)
                        throw new InvalidDataException($"Model download exceeded the expected {spec.SizeBytes} bytes.");
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    int pct = total > 0 ? (int)(read * 100 / total) : 0;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        double mbRead = read / 1048576.0;
                        double mbTotal = total / 1048576.0;
                        progress?.Report(new InstallProgressInfo
                        {
                            Percentage = pct,
                            Message = $"Mengunduh {spec.DisplayName} ({mbRead:F1} / {mbTotal:F1} MB)..."
                        });
                    }
                }
            }

            var downloadedLength = new FileInfo(partPath).Length;
            if (!HasExpectedSize(spec, downloadedLength))
                throw new InvalidDataException(
                    $"Model response length mismatch: expected {spec.SizeBytes} bytes, received {downloadedLength}.");

            // SHA is mandatory (validated before the request) and must match before install.
            progress?.Report(new InstallProgressInfo { Percentage = 99, Message = "Memverifikasi berkas..." });
            var actual = await ComputeSha256Async(partPath, ct);
            if (!string.Equals(actual, spec.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(partPath);
                throw new Exception(
                    "Verifikasi model gagal (hash tidak cocok). Unduhan dibatalkan demi keamanan. " +
                    $"Diharapkan {spec.Sha256}, didapat {actual}.");
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(partPath, finalPath);

            progress?.Report(new InstallProgressInfo { Percentage = 100, Message = "Model siap" });
        }
        catch (OperationCanceledException)
        {
            SafeDelete(partPath);
            throw;
        }
        catch
        {
            SafeDelete(partPath);
            throw;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

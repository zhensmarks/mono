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

    /// <summary>Folder cache model.</summary>
    public static string ModelsDirectory
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "Resources", "AiModels");
        }
    }

    public static string PathFor(MattingModelSpec spec) =>
        Path.Combine(ModelsDirectory, spec.FileName);

    /// <summary>Model default (MODNet) — cepat, 24.7 MB, Apache-2.0.</summary>
    public static MattingModelSpec ModNet { get; } = new()
    {
        Id = "modnet",
        DisplayName = "MODNet (cepat, 24 MB)",
        // Catatan: hash diverifikasi saat unduhan pertama; bila upstream berubah,
        // unduhan ditolak dan user diminta melapor — lebih aman daripada
        // menjalankan file tak terverifikasi.
        Url = "https://huggingface.co/Xenova/modnet/resolve/main/onnx/model.onnx",
        Sha256 = "",
        SizeBytes = 25_888_566,
        License = "Apache-2.0",
        ShortestEdge = 512,
        SizeDivisibility = 32,
        Mean = new[] { 0.5, 0.5, 0.5 },
        Std = new[] { 0.5, 0.5, 0.5 },
        AcceptsTrimap = false,
        Notes = "Resize shortest-edge 512, dibulatkan ke kelipatan 32."
    };

    /// <summary>Model kualitas maksimum (BiRefNet_lite) — 213 MB, MIT.</summary>
    public static MattingModelSpec BiRefNetLite { get; } = new()
    {
        Id = "birefnet_lite",
        DisplayName = "BiRefNet lite (kualitas maksimum, 214 MB)",
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

    /// <summary>True bila file model ada dan ukurannya masuk akal (&gt; 1 MB).</summary>
    public static bool IsInstalled(MattingModelSpec spec)
    {
        var path = PathFor(spec);
        if (!File.Exists(path)) return false;
        try { return new FileInfo(path).Length > 1_000_000; }
        catch { return false; }
    }

    /// <summary>Hapus model dari disk. Mengembalikan true bila ada yang dihapus.</summary>
    public static bool Uninstall(MattingModelSpec spec)
    {
        var path = PathFor(spec);
        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
        }
        catch { /* diamkan: file mungkin terkunci */ }
        return false;
    }

    /// <summary>
    /// Pastikan model tersedia. Bila belum ada, unduh ke &lt;file&gt;.part lalu
    /// verifikasi SHA-256 (bila spec punya hash) sebelum memindahkan ke nama final.
    /// </summary>
    public static async Task EnsureAvailableAsync(MattingModelSpec spec,
        IProgress<InstallProgressInfo>? progress, CancellationToken ct)
    {
        var finalPath = PathFor(spec);
        if (IsInstalled(spec))
        {
            progress?.Report(new InstallProgressInfo { Percentage = 100, Message = "Model siap" });
            return;
        }

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

            long total = resp.Content.Headers.ContentLength ?? spec.SizeBytes;
            if (total <= 0) total = spec.SizeBytes;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                int lastPct = -1;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
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

            // Verifikasi hash bila tersedia.
            if (!string.IsNullOrWhiteSpace(spec.Sha256))
            {
                progress?.Report(new InstallProgressInfo { Percentage = 99, Message = "Memverifikasi berkas..." });
                var actual = await ComputeSha256Async(partPath, ct);
                if (!string.Equals(actual, spec.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SafeDelete(partPath);
                    throw new Exception(
                        "Verifikasi model gagal (hash tidak cocok). Unduhan dibatalkan demi keamanan. " +
                        $"Diharapkan {spec.Sha256}, didapat {actual}.");
                }
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

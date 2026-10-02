using System;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Utilitas orientasi untuk restore masking.
///
/// Masalah yang dipecahkan: bila dimensi gambar asli tertukar terhadap hasil
/// (W×H vs H×W), kode lama SELALU memutar 90° searah jarum jam secara buta.
/// Bila hubungan sebenarnya berlawanan arah (atau ada orientasi EXIF),
/// restore memetakan koordinat ke area cerminan — user melihat area lain
/// yang kembali. Class ini menormalkan orientasi EXIF dulu, lalu
/// mendeteksi otomatis arah rotasi (CW vs CCW) lewat skor kemiripan.
/// Bila tidak ada rotasi yang cocok dengan baik, restore DINONAKTIFKAN
/// dengan pesan jujur (tidak restore diam-diam ke area salah).
/// </summary>
public static class RestoreOrientation
{
    /// <summary>Toleransi per kanal warna (0-255) saat skor kemiripan.</summary>
    public const int MatchTolerance = 32;

    /// <summary>Skor minimal (0-1) agar suatu rotasi diterima.</summary>
    public const double MinScore = 0.55;

    /// <summary>Selisih skor minimal antara kandidat terbaik dan kedua.</summary>
    public const double MinMargin = 0.10;

    /// <summary>
    /// Skor di atas ini dianggap "hampir sempurna" dan diterima langsung
    /// tanpa syarat margin: pada konten simetris pun kandidat yang salah
    /// tetap benar secara piksel bila skornya setinggi ini.
    /// </summary>
    public const double HighConfidenceScore = 0.90;

    private const int MaxSamplePixels = 20000;

    /// <summary>Putar 90° searah jarum jam. src W×H → H×W.</summary>
    public static PixelBuffer Rotate90Clockwise(PixelBuffer src)
    {
        // rotated(xr, yr) = src(yr, H-1-xr).
        var dst = new PixelBuffer(src.Height, src.Width);
        var s = src.Bgra; var d = dst.Bgra;
        int w = src.Width, h = src.Height;
        for (int yr = 0; yr < w; yr++)
        {
            for (int xr = 0; xr < h; xr++)
            {
                int xo = yr;
                int yo = h - 1 - xr;
                int si = (yo * w + xo) * 4;
                int di = (yr * h + xr) * 4;
                d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = s[si + 3];
            }
        }
        return dst;
    }

    /// <summary>Putar 90° berlawanan arah jarum jam. src W×H → H×W.</summary>
    public static PixelBuffer Rotate90CounterClockwise(PixelBuffer src)
    {
        // dst(xr, yr) = src(W-1-yr, xr).
        var dst = new PixelBuffer(src.Height, src.Width);
        var s = src.Bgra; var d = dst.Bgra;
        int w = src.Width, h = src.Height;
        for (int yr = 0; yr < w; yr++)
        {
            for (int xr = 0; xr < h; xr++)
            {
                int xo = w - 1 - yr;
                int yo = xr;
                int si = (yo * w + xo) * 4;
                int di = (yr * h + xr) * 4;
                d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = s[si + 3];
            }
        }
        return dst;
    }

    /// <summary>Putar 180°. Dimensi tetap.</summary>
    public static PixelBuffer Rotate180(PixelBuffer src)
    {
        var dst = new PixelBuffer(src.Width, src.Height);
        var s = src.Bgra; var d = dst.Bgra;
        int w = src.Width, h = src.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int si = ((h - 1 - y) * w + (w - 1 - x)) * 4;
                int di = (y * w + x) * 4;
                d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = s[si + 3];
            }
        }
        return dst;
    }

    /// <summary>
    /// Terapkan orientasi EXIF ke buffer sehingga cocok dengan ruang display
    /// (sama seperti <c>LoadBitmapWithOrientation</c> di jalur preview).
    /// 6 = 90° CW, 8 = 90° CCW, 3 = 180°, lainnya = tanpa perubahan.
    /// </summary>
    public static PixelBuffer ApplyExifOrientation(PixelBuffer src, int orientation)
    {
        return orientation switch
        {
            6 => Rotate90Clockwise(src),
            8 => Rotate90CounterClockwise(src),
            3 => Rotate180(src),
            _ => src,
        };
    }

    /// <summary>
    /// Skor kemiripan (0-1): fraksi piksel OPAQUE result yang warnanya cocok
    /// dengan kandidat dalam <see cref="MatchTolerance"/> per kanal.
    /// Hanya piksel opaque result yang dinilai (area transparan = objek
    /// terhapus, tidak ada padanannya di original).
    /// </summary>
    public static double ScoreSimilarity(PixelBuffer candidate, PixelBuffer result)
    {
        if (candidate.Width != result.Width || candidate.Height != result.Height)
            return 0;
        int w = result.Width, h = result.Height;
        // Sampling grid agar cepat untuk gambar besar.
        int stride = 1;
        long total = (long)w * h;
        if (total > MaxSamplePixels)
            stride = Math.Max(1, (int)Math.Sqrt((double)total / MaxSamplePixels));

        var cb = candidate.Bgra; var rb = result.Bgra;
        long match = 0, opaque = 0;
        for (int y = 0; y < h; y += stride)
        {
            for (int x = 0; x < w; x += stride)
            {
                int bi = (y * w + x) * 4;
                if (rb[bi + 3] <= 128) continue;
                opaque++;
                int dr = Math.Abs(cb[bi + 2] - rb[bi + 2]);
                int dg = Math.Abs(cb[bi + 1] - rb[bi + 1]);
                int db = Math.Abs(cb[bi + 0] - rb[bi + 0]);
                if (dr <= MatchTolerance && dg <= MatchTolerance && db <= MatchTolerance)
                    match++;
            }
        }
        if (opaque == 0) return 0;
        return (double)match / opaque;
    }

    /// <summary>
    /// Bila dimensi original tertukar terhadap result, coba kedua arah rotasi
    /// dan pilih yang paling mirip result. Kembalikan null bila tidak ada
    /// yang cukup meyakinkan (kemungkinan crop/scale, bukan rotasi murni).
    /// </summary>
    public static (PixelBuffer Buffer, string Name, double Score)? DetectSwappedRotation(
        PixelBuffer original, PixelBuffer result)
    {
        if (original.Width != result.Height || original.Height != result.Width)
            return null;
        var cw = Rotate90Clockwise(original);
        var ccw = Rotate90CounterClockwise(original);
        double sCw = ScoreSimilarity(cw, result);
        double sCcw = ScoreSimilarity(ccw, result);

        var best = sCw >= sCcw
            ? (Buffer: cw, Name: "CW", Score: sCw, Other: sCcw)
            : (Buffer: ccw, Name: "CCW", Score: sCcw, Other: sCw);

        if (best.Score < MinScore) return null;
        // Skor hampir sempurna = tidak ambigu (konten simetris pun aman).
        if (best.Score >= HighConfidenceScore) return (best.Buffer, best.Name, best.Score);
        if (best.Score - best.Other < MinMargin) return null;
        return (best.Buffer, best.Name, best.Score);
    }

    /// <summary>
    /// Pipeline lengkap penyiapan original untuk session restore:
    /// (1) normalkan orientasi EXIF agar selaras dengan yang user lihat,
    /// (2) bila dimensi tertukar, deteksi otomatis arah rotasi,
    /// (3) bila tidak selaras, kembalikan Issue jujur (restore dinonaktifkan).
    /// </summary>
    public static (PixelBuffer? Buffer, string? Issue) NormalizeOriginalForSession(
        PixelBuffer rawOriginal, int exifOrientation, PixelBuffer result)
    {
        var oriented = ApplyExifOrientation(rawOriginal, exifOrientation);
        if (oriented.Width == result.Width && oriented.Height == result.Height)
            return (oriented, null);

        var detected = DetectSwappedRotation(oriented, result);
        if (detected != null)
            return (detected.Value.Buffer, null);

        return (null,
            $"gambar asli ({rawOriginal.Width}×{rawOriginal.Height}, orientasi EXIF {exifOrientation}) " +
            $"tidak selaras dengan hasil ({result.Width}×{result.Height}); " +
            $"arah rotasi tidak dapat dipastikan sehingga restore dinonaktifkan");
    }
}

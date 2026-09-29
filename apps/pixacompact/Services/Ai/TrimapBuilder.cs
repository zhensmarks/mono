using System;

namespace PixelcutCompact.Services.Ai;

/// <summary>
/// Hasil trimap: 0 = background pasti, 128 = unknown (band), 255 = foreground pasti.
/// </summary>
public sealed class TrimapResult
{
    public required byte[] Trimap { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Bounding box area "unknown" (band) — untuk membatasi inferensi ke crop.</summary>
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int MaxX { get; init; }
    public required int MaxY { get; init; }

    public bool HasUnknown => MaxX >= MinX && MaxY >= MinY;
    public int UnknownWidth => HasUnknown ? MaxX - MinX + 1 : 0;
    public int UnknownHeight => HasUnknown ? MaxY - MinY + 1 : 0;
}

/// <summary>
/// Membangun trimap dari mask alpha: foreground = erode(radius),
/// background = dilate(radius), sisanya unknown band.
/// Band inilah satu-satunya wilayah yang akan ditulis ulang oleh matting,
/// sehingga area solid tidak bergeser (mencegah "seam"/drift).
/// </summary>
public static class TrimapBuilder
{
    /// <summary>
    /// Bangun trimap dari mask. <paramref name="bandRadius"/> = lebar band unknown
    /// dalam piksel (mis. 12 → erode/dilate 12 px).
    /// </summary>
    public static TrimapResult Build(byte[] mask, int width, int height, int bandRadius)
    {
        if (mask == null) throw new ArgumentNullException(nameof(mask));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (mask.Length < width * height) throw new ArgumentException("Panjang mask tidak sesuai.", nameof(mask));

        int r = Math.Max(1, bandRadius);
        var trimap = new byte[width * height];

        // Binarisasi mask ke FG/BG tegas dulu.
        var fg = new byte[width * height];
        for (int i = 0; i < trimap.Length; i++)
            fg[i] = mask[i] >= 128 ? (byte)1 : (byte)0;

        var eroded = Erode(fg, width, height, r);   // FG pasti (menyusut r px)
        var dilated = Dilate(fg, width, height, r); // BG pasti = komplemen FG yang mengembang

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                byte v;
                if (eroded[i] != 0) v = 255;          // pasti foreground
                else if (dilated[i] == 0) v = 0;      // pasti background
                else v = 128;                          // unknown band
                trimap[i] = v;

                if (v == 128)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return new TrimapResult
        {
            Trimap = trimap,
            Width = width,
            Height = height,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY
        };
    }

    /// <summary>
    /// Versi yang membatasi band unknown ke dalam region seleksi: di luar
    /// seleksi, piksel dipaksa FG/BG pasti sesuai mask aslinya.
    /// </summary>
    public static TrimapResult BuildWithin(byte[] mask, byte[] selection, int width, int height, int bandRadius)
    {
        var result = Build(mask, width, height, bandRadius);
        if (selection == null || selection.Length < width * height) return result;

        var trimap = result.Trimap;
        // Reklasifikasi band unknown yang berada di luar selection → FG/BG penuh,
        // lalu hitung ulang bounding box dari unknown yang tersisa. Tanpa ini,
        // crop di RefineHairService akan mencakup seluruh band (lambat + tak
        // menghormati selection).
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < trimap.Length; i++)
        {
            if (trimap[i] != 128) continue;
            if (selection[i] == 0)
            {
                trimap[i] = mask[i] >= 128 ? (byte)255 : (byte)0;
                continue;
            }
            int x = i % width, y = i / width;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return new TrimapResult
        {
            Trimap = trimap,
            Width = width,
            Height = height,
            MinX = minX == int.MaxValue ? 0 : minX,
            MinY = minY == int.MaxValue ? 0 : minY,
            MaxX = maxX == int.MinValue ? -1 : maxX,
            MaxY = maxY == int.MinValue ? -1 : maxY
        };
    }

    // ========================
    // MORFOLOGI (box max/min, separable)
    // ========================

    private static byte[] Erode(byte[] src, int width, int height, int radius)
    {
        // Erode = min filter: hasil 1 hanya bila seluruh radius 1.
        var tmp = new byte[src.Length];
        var dst = new byte[src.Length];

        // Pass horizontal: hitung jarak ke tepi terdekat (bukan sekadar min)
        // untuk akurasi erode disk. Di sini dipakai min box (chebyshev) —
        // cukup baik untuk band dan O(n).
        MinFilterH(src, tmp, width, height, radius);
        MinFilterV(tmp, dst, width, height, radius);
        return dst;
    }

    private static byte[] Dilate(byte[] src, int width, int height, int radius)
    {
        var tmp = new byte[src.Length];
        var dst = new byte[src.Length];
        MaxFilterH(src, tmp, width, height, radius);
        MaxFilterV(tmp, dst, width, height, radius);
        return dst;
    }

    private static void MinFilterH(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte m = 1;
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r);
                for (int sx = x0; sx <= x1; sx++) { if (src[row + sx] == 0) { m = 0; break; } }
                dst[row + x] = m;
            }
        }
    }

    private static void MinFilterV(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - r), y1 = Math.Min(h - 1, y + r);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte m = 1;
                for (int sy = y0; sy <= y1; sy++) { if (src[sy * w + x] == 0) { m = 0; break; } }
                dst[row + x] = m;
            }
        }
    }

    private static void MaxFilterH(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte m = 0;
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r);
                for (int sx = x0; sx <= x1; sx++) { if (src[row + sx] != 0) { m = 1; break; } }
                dst[row + x] = m;
            }
        }
    }

    private static void MaxFilterV(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - r), y1 = Math.Min(h - 1, y + r);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte m = 0;
                for (int sy = y0; sy <= y1; sy++) { if (src[sy * w + x] != 0) { m = 1; break; } }
                dst[row + x] = m;
            }
        }
    }
}

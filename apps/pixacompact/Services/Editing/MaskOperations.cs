using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Operasi piksel pada mask 8-bit (1 byte/piksel) dan buffer RGBA.
/// Semua operasi bekerja in-place pada byte[] mask, kecuali dinyatakan lain.
/// </summary>
public static class MaskOperations
{
    /// <summary>Balik nilai mask (alpha): 255 - a.</summary>
    public static void Invert(byte[] mask)
    {
        if (mask == null) return;
        for (int i = 0; i < mask.Length; i++)
            mask[i] = (byte)(255 - mask[i]);
    }

    /// <summary>Isi seluruh mask dengan nilai tertentu.</summary>
    public static void Fill(byte[] mask, byte value)
    {
        if (mask == null) return;
        for (int i = 0; i < mask.Length; i++) mask[i] = value;
    }

    /// <summary>
    /// Shift Edge: dilate (expand) atau erode (contract) mask memakai kernel
    /// kotak dengan jari-jari <paramref name="radius"/> piksel.
    /// <paramref name="expand"/> = true → expand (max filter), false → contract (min filter).
    /// </summary>
    public static void ShiftEdge(byte[] mask, int width, int height, int radius, bool expand)
    {
        if (mask == null || radius <= 0 || width <= 0 || height <= 0) return;
        var src = (byte[])mask.Clone();

        // Separable: pass horizontal lalu vertical untuk O(n*r) bukan O(n*r^2).
        var tmp = new byte[src.Length];
        PassH(src, tmp, width, height, radius, expand);
        PassV(tmp, mask, width, height, radius, expand);
    }

    private static void PassH(byte[] src, byte[] dst, int w, int h, int r, bool expand)
    {
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte best = src[row + x];
                int x0 = Math.Max(0, x - r);
                int x1 = Math.Min(w - 1, x + r);
                for (int sx = x0; sx <= x1; sx++)
                {
                    byte v = src[row + sx];
                    if (expand) { if (v > best) best = v; }
                    else { if (v < best) best = v; }
                }
                dst[row + x] = best;
            }
        }
    }

    private static void PassV(byte[] src, byte[] dst, int w, int h, int r, bool expand)
    {
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - r);
            int y1 = Math.Min(h - 1, y + r);
            for (int x = 0; x < w; x++)
            {
                byte best = src[y * w + x];
                for (int sy = y0; sy <= y1; sy++)
                {
                    byte v = src[sy * w + x];
                    if (expand) { if (v > best) best = v; }
                    else { if (v < best) best = v; }
                }
                dst[y * w + x] = best;
            }
        }
    }

    /// <summary>
    /// Feather: box blur 3x (mendekati Gaussian) pada mask.
    /// Radius dalam piksel.
    /// </summary>
    public static void Feather(byte[] mask, int width, int height, int radius)
    {
        if (mask == null || radius <= 0 || width <= 0 || height <= 0) return;

        // 3x box blur ≈ Gaussian; gunakan radius box = max(1, round(radius/1.5)).
        int r = Math.Max(1, (int)Math.Round(radius / 1.5));
        var tmp = new byte[mask.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurH(mask, tmp, width, height, r);
            BoxBlurV(tmp, mask, width, height, r);
        }
    }

    private static void BoxBlurH(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            long sum = 0;
            for (int x = -r; x <= r; x++)
                sum += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = (byte)(sum / window);
                int add = Math.Clamp(x + r + 1, 0, w - 1);
                int sub = Math.Clamp(x - r, 0, w - 1);
                sum += src[row + add] - src[row + sub];
            }
        }
    }

    private static void BoxBlurV(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        for (int x = 0; x < w; x++)
        {
            long sum = 0;
            for (int y = -r; y <= r; y++)
                sum += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (byte)(sum / window);
                int add = Math.Clamp(y + r + 1, 0, h - 1);
                int sub = Math.Clamp(y - r, 0, h - 1);
                sum += src[add * w + x] - src[sub * w + x];
            }
        }
    }

    /// <summary>
    /// Defringe / decontaminate: untuk tiap piksel tepi (alpha parsial),
    /// ganti warna RGB dengan hasil blend warna dari piksel alpha tertinggi di
    /// sekitarnya, dibobot oleh alpha. Menghilangkan halo warna (green screen).
    /// </summary>
    public static void Defringe(PixelBuffer buffer, ReadOnlySpan<byte> mask, int radius)
    {
        if (buffer == null || radius <= 0) return;
        int w = buffer.Width, h = buffer.Height;
        if (mask.Length < w * h) return;

        // Bangun peta warna FG terdekat: untuk tiap piksel, cari piksel sekitar
        // yang paling opaque, lalu tarik warnanya sebagai sumber.
        var src = buffer.Bgra;
        var fgR = new byte[w * h];
        var fgG = new byte[w * h];
        var fgB = new byte[w * h];
        var fgA = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            fgB[i] = src[bi + 0];
            fgG[i] = src[bi + 1];
            fgR[i] = src[bi + 2];
            // Bobot = alpha mask × alpha piksel itu sendiri.
            int a = mask[i] * src[bi + 3] / 255;
            fgA[i] = (byte)a;
        }

        // Ambil warna terbaik dari radius kotak, preferensikan alpha tertinggi.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                byte aHere = mask[idx];
                // Hanya proses piksel pinggir (bukan solid FG/BG murni).
                if (aHere == 0 || aHere == 255) continue;

                int bestA = -1;
                byte br = fgR[idx], bg = fgG[idx], bb = fgB[idx];
                int x0 = Math.Max(0, x - radius), x1 = Math.Min(w - 1, x + radius);
                int y0 = Math.Max(0, y - radius), y1 = Math.Min(h - 1, y + radius);
                for (int sy = y0; sy <= y1; sy++)
                {
                    for (int sx = x0; sx <= x1; sx++)
                    {
                        int si = sy * w + sx;
                        int a = fgA[si];
                        if (a > bestA)
                        {
                            bestA = a;
                            br = fgR[si]; bg = fgG[si]; bb = fgB[si];
                        }
                    }
                }
                if (bestA <= 0) continue;

                // Blend ke warna sumber terdekat sesuai bobot alpha mask:
                // makin transparan → makin banyak warna FG terdekat dipakai.
                int bi = idx * 4;
                double t = 1.0 - aHere / 255.0;
                src[bi + 2] = Lerp(src[bi + 2], br, t);
                src[bi + 1] = Lerp(src[bi + 1], bg, t);
                src[bi + 0] = Lerp(src[bi + 0], bb, t);
            }
        }
    }

    private static byte Lerp(byte a, byte b, double t)
    {
        double v = a + (b - a) * t;
        if (v < 0) v = 0; else if (v > 255) v = 255;
        return (byte)(v + 0.5);
    }

    /// <summary>Bounding box piksel dengan mask di rentang (lo, hi) eksklusif.</summary>
    public static bool GetMaskBounds(byte[] mask, int width, int height,
        byte loExclusive, byte hiExclusive, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = int.MaxValue;
        maxX = maxY = int.MinValue;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                byte v = mask[row + x];
                if (v > loExclusive && v < hiExclusive)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }
        return maxX >= minX && maxY >= minY;
    }

    /// <summary>Bounding box seluruh piksel yang alpha > 0 (bukan fully transparent).</summary>
    public static bool GetAlphaBounds(byte[] mask, int width, int height,
        out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = int.MaxValue;
        maxX = maxY = int.MinValue;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (mask[row + x] == 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        return maxX >= minX && maxY >= minY;
    }
    /// <summary>
    /// Ambang biner coverage: 255 bila nilai &gt;= <paramref name="t"/>, else 0.
    /// Dipakai untuk menyiapkan input kontur / operasi biner.
    /// </summary>
    public static void Threshold(byte[] coverage, byte t)
    {
        if (coverage == null) return;
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = coverage[i] >= t ? (byte)255 : (byte)0;
    }

    /// <summary>
    /// Refine Edge brush (non-AI): rapikan tepi mask secara LOKAL di dalam
    /// lingkaran kuas. Mask yang sudah di-shift-edge + di-feather dihitung pada
    /// sub-rectangle (crop) agar tetap murah, lalu di-blend ke mask asli dengan
    /// bobot kuas (hardness → falloff lembut). Tidak menyentuh area solid jauh
    /// dari sapuan karena bobot di luar kuas = 0.
    /// </summary>
    public static void RefineEdgeBand(byte[] mask, int width, int height,
        double cx, double cy, double radius, int shiftPx, bool expand, int featherPx, double hardness)
    {
        if (mask == null || width <= 0 || height <= 0 || radius <= 0.5) return;

        double r2 = radius * radius;
        int minX = (int)Math.Floor(cx - radius);
        int maxX = (int)Math.Ceiling(cx + radius);
        int minY = (int)Math.Floor(cy - radius);
        int maxY = (int)Math.Ceiling(cy + radius);
        if (minX < 0) minX = 0;
        if (minY < 0) minY = 0;
        if (maxX > width - 1) maxX = width - 1;
        if (maxY > height - 1) maxY = height - 1;
        if (maxX < minX || maxY < minY) return;

        int bw = maxX - minX + 1;
        int bh = maxY - minY + 1;

        // Crop mask ke bounding box kuas.
        var crop = new byte[bw * bh];
        for (int y = 0; y < bh; y++)
        {
            int src = (minY + y) * width + minX;
            Buffer.BlockCopy(mask, src, crop, y * bw, bw);
        }

        // Versi "bersih": shift edge lalu feather, dihitung pada crop.
        var cleaned = (byte[])crop.Clone();
        if (shiftPx > 0) ShiftEdge(cleaned, bw, bh, shiftPx, expand);
        if (featherPx > 0) Feather(cleaned, bw, bh, featherPx);

        double hh = Math.Clamp(hardness, 0.0, 1.0);
        for (int y = 0; y < bh; y++)
        {
            double dy = (minY + y) - cy;
            for (int x = 0; x < bw; x++)
            {
                double dx = (minX + x) - cx;
                double d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;

                double d = Math.Sqrt(d2) / radius;      // 0 di pusat, 1 di tepi kuas
                double w = hh >= 0.999 ? 1.0 : 1.0 - Math.Clamp((d - hh) / (1.0 - hh), 0.0, 1.0);
                if (w <= 0) continue;

                int ci = y * bw + x;
                int gi = (minY + y) * width + (minX + x);
                int a = crop[ci];
                int b = cleaned[ci];
                mask[gi] = (byte)(a + (b - a) * w + 0.5);
            }
        }
    }
}

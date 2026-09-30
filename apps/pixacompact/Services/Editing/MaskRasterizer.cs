using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Rasterizer jalur tertutup → buffer mask 8-bit.
/// Mendukung winding rule non-zero & even-odd, anti-alias via supersampling,
/// serta flattening cubic Bézier (Pen Tool).
/// </summary>
public static class MaskRasterizer
{
    private const int DefaultSupersample = 4;

    /// <summary>
    /// Isi region ke mask. <paramref name="op"/> menentukan bagaimana nilai
    /// digabung dengan mask yang ada (Replace/Add/Subtract/Intersect).
    /// </summary>
    public static void FillRegion(MaskRegion region, byte[] mask, int width, int height,
        byte value, MaskCombineOp op = MaskCombineOp.Replace, bool antiAlias = true, double feather = 0)
    {
        if (region == null || region.IsEmpty || mask == null) return;
        if (width <= 0 || height <= 0) return;
        long totalPixels = (long)width * height;
        if (mask.LongLength < totalPixels) return;

        region.GetBounds(out double minX, out double minY, out double maxX, out double maxY);
        if (!double.IsFinite(minX) || !double.IsFinite(minY) ||
            !double.IsFinite(maxX) || !double.IsFinite(maxY) ||
            minX > maxX || minY > maxY) return;

        // Allocate coverage only for the padded selection bounds, not the full frame.
        double featherRadius = double.IsFinite(feather)
            ? Math.Clamp(feather, 0, Math.Max(width, height))
            : 0;
        int pad = Math.Max(1, (int)Math.Ceiling(featherRadius));
        int x0 = (int)Math.Clamp(Math.Floor(minX) - pad, 0, width - 1);
        int y0 = (int)Math.Clamp(Math.Floor(minY) - pad, 0, height - 1);
        int x1 = (int)Math.Clamp(Math.Ceiling(maxX) + pad, 0, width - 1);
        int y1 = (int)Math.Clamp(Math.Ceiling(maxY) + pad, 0, height - 1);
        if (x1 < x0 || y1 < y0) return;

        int localWidth = x1 - x0 + 1;
        int localHeight = y1 - y0 + 1;
        long coverageLength = (long)localWidth * localHeight;
        if (coverageLength > int.MaxValue) return;
        int ss = antiAlias ? DefaultSupersample : 1;
        if (localHeight > int.MaxValue / ss) return;
        var coverage = new float[(int)coverageLength];

        var subpaths = region.GetSubpaths();
        int n = 0;
        foreach (var s in subpaths) if (s.Count >= 3) n += s.Count;
        if (n < 3) return;
        double sampleOffsetScale = 1.0 / ss;
        var crossings = new List<(double x, int dir)>(16);
        // Scanline: untuk tiap baris (sub-scanline), cari perpotongan.
        int rows = localHeight * ss;
        for (int sy = 0; sy < rows; sy++)
        {
            double y = y0 + (sy + 0.5) * sampleOffsetScale;

            // Kumpulkan crossing (x, arah) dari semua subpath tanpa alokasi per baris.
            crossings.Clear();
            foreach (var sub in subpaths)
            {
                int m = sub.Count;
                if (m < 3) continue;
                for (int i = 0; i < m; i++)
                {
                    var a = sub[i];
                    var b = sub[(i + 1) % m];
                    double ay = a.Y, by = b.Y;
                    if (ay == by) continue;

                    // Half-open rule [min,max) menghindari double-count vertex.
                    double ymin = Math.Min(ay, by);
                    double ymax = Math.Max(ay, by);
                    if (y < ymin || y >= ymax) continue;

                    double t = (y - ay) / (by - ay);
                    double x = a.X + (b.X - a.X) * t;
                    crossings.Add((x, by > ay ? 1 : -1));
                }
            }

            if (crossings.Count < 2) continue;
            crossings.Sort((p, q) => p.x.CompareTo(q.x));

            int rowIndex = sy / ss;
            double weight = sampleOffsetScale; // kontribusi satu sub-scanline per piksel

            int winding = 0;
            for (int k = 0; k < crossings.Count - 1; k++)
            {
                winding += crossings[k].dir;
                bool inside = region.EvenOdd ? ((k & 1) == 0) : (winding != 0);
                if (!inside) continue;

                double xa = crossings[k].x;
                double xb = crossings[k + 1].x;
                if (xb <= x0 || xa >= x1 + 1) continue;

                // Tambah coverage ke piksel pada baris rowIndex
                int pxStart = (int)Math.Floor(xa);
                int pxEnd = (int)Math.Ceiling(xb) - 1;
                if (pxStart < x0) pxStart = x0;
                if (pxEnd > x1) pxEnd = x1;
                int rowOff = rowIndex * localWidth;
                for (int px = pxStart; px <= pxEnd; px++)
                {
                    double cellL = px;
                    double cellR = px + 1;
                    double overlap = Math.Min(xb, cellR) - Math.Max(xa, cellL);
                    if (overlap <= 0) continue;
                    coverage[rowOff + px - x0] += (float)(overlap * weight);
                }
            }
            _ = y;
        }

        // Feather (blur) pada coverage bila diminta.
        if (featherRadius > 0.5)
            BlurCoverage(coverage, localWidth, localHeight, featherRadius);

        // Terapkan coverage ke mask sesuai op.
        for (int localY = 0; localY < localHeight; localY++)
        {
            int maskRow = (y0 + localY) * width + x0;
            int coverageRow = localY * localWidth;
            for (int localX = 0; localX < localWidth; localX++)
            {
                float c = coverage[coverageRow + localX];
                if (c <= 0f) continue;
                if (c > 1f) c = 1f;
                ApplyCoverage(mask, maskRow + localX, c, value, op);
            }
        }
    }

    private static void ApplyCoverage(byte[] mask, int index, float coverage, byte value, MaskCombineOp op)
    {
        switch (op)
        {
            case MaskCombineOp.Replace:
                mask[index] = Lerp(mask[index], value, coverage);
                break;
            case MaskCombineOp.Add:
                mask[index] = (byte)Math.Min(255, mask[index] + value * coverage);
                break;
            case MaskCombineOp.Subtract:
                mask[index] = (byte)Math.Max(0, mask[index] - value * coverage);
                break;
            case MaskCombineOp.Intersect:
                mask[index] = Lerp(mask[index], (byte)(mask[index] * value / 255.0), coverage);
                break;
        }
    }

    private static byte Lerp(byte a, byte b, float t)
    {
        float v = a + (b - a) * t;
        if (v < 0) v = 0;
        if (v > 255) v = 255;
        return (byte)(v + 0.5f);
    }

    // ========================
    // BÉZIER FLATTENING
    // ========================

    /// <summary>
    /// Flatten cubic Bézier menjadi polyline dengan toleransi jarak (px).
    /// Adaptive subdivision berbasis flatness.
    /// </summary>
    public static void FlattenCubic(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tolerance,
        List<Vec2> output, int depth = 0)
    {
        if (depth > 16 || IsFlat(p0, p1, p2, p3, tolerance))
        {
            output.Add(p3);
            return;
        }

        // De Casteljau split at t = 0.5
        Vec2 p01 = (p0 + p1) * 0.5;
        Vec2 p12 = (p1 + p2) * 0.5;
        Vec2 p23 = (p2 + p3) * 0.5;
        Vec2 p012 = (p01 + p12) * 0.5;
        Vec2 p123 = (p12 + p23) * 0.5;
        Vec2 mid = (p012 + p123) * 0.5;

        FlattenCubic(p0, p01, p012, mid, tolerance, output, depth + 1);
        FlattenCubic(mid, p123, p23, p3, tolerance, output, depth + 1);
    }

    private static bool IsFlat(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tol)
    {
        // Jarak kontrol terhadap garis p0-p3.
        double dx = p3.X - p0.X, dy = p3.Y - p0.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-9)
        {
            double d1 = Dist(p1, p0), d2 = Dist(p2, p0);
            return d1 <= tol && d2 <= tol;
        }
        double d = Math.Abs((p1.X - p0.X) * dy - (p1.Y - p0.Y) * dx) / Math.Sqrt(len2);
        double e = Math.Abs((p2.X - p0.X) * dy - (p2.Y - p0.Y) * dx) / Math.Sqrt(len2);
        double maxD = Math.Max(d, e);
        return maxD * maxD * len2 <= tol * tol * (len2) || maxD <= tol;
    }

    private static double Dist(Vec2 a, Vec2 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ========================
    // COVERAGE BLUR (feather)
    // ========================

    public static void BlurCoverage(float[] coverage, int width, int height, double radius)
    {
        if (radius < 0.5) return;
        int r = (int)Math.Round(radius);
        if (r < 1) return;

        var tmp = new float[coverage.Length];
        // Horizontal pass (box blur)
        BoxBlurH(coverage, tmp, width, height, r);
        // Vertical pass
        BoxBlurV(tmp, coverage, width, height, r);
    }

    private static void BoxBlurH(float[] src, float[] dst, int w, int h, int r)
    {
        double norm = 1.0 / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int dx = -r; dx <= r; dx++)
                {
                    int sx = x + dx;
                    if (sx < 0) sx = 0;
                    else if (sx >= w) sx = w - 1;
                    sum += src[row + sx];
                }
                dst[row + x] = (float)(sum * norm);
            }
        }
    }

    private static void BoxBlurV(float[] src, float[] dst, int w, int h, int r)
    {
        double norm = 1.0 / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    int sy = y + dy;
                    if (sy < 0) sy = 0;
                    else if (sy >= h) sy = h - 1;
                    sum += src[sy * w + x];
                }
                dst[y * w + x] = (float)(sum * norm);
            }
        }
    }
}

public enum MaskCombineOp
{
    Replace,
    Add,
    Subtract,
    Intersect
}

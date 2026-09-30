using System;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Satu cap (stamp) brush: posisi, radius, kekerasan, opacity, dan mode.
/// </summary>
public struct BrushStamp
{
    public double X;
    public double Y;
    public double Radius;      // jari-jari dalam piksel gambar
    public double Hardness;    // 0 = sangat lembut, 1 = tajam
    public double Opacity;     // 0..1 batas akumulasi per stroke (opacity cap)
    public double Flow;        // 0..1 penambahan per cap (buildup); default 1 = perilaku lama
    /// <summary>true = restore (menuju 255), false = erase (menuju 0).</summary>
    public bool Restore;
}

/// <summary>
/// Brush mask: cap disk dengan falloff halus. Erase menurunkan alpha menuju 0,
/// Restore menaikkan menuju 255. Akumulasi Flow disimpan dalam tile sparse.
/// </summary>
public static class BrushTool
{
    /// <summary>Terapkan satu cap ke mask.</summary>
    public static void Stamp(byte[] mask, int width, int height, BrushStamp stamp)
    {
        if (!HasValidMask(mask, width, height)) return;
        var bounds = GetStampBounds(stamp, width, height);
        if (bounds.IsEmpty) return;

        double radius = stamp.Radius;
        double hardness = FiniteClamp(stamp.Hardness, 0.0, 1.0);
        double inner = radius * hardness;
        double falloff = Math.Max(0.5, radius - inner);
        double opacity = FiniteClamp(stamp.Opacity, 0.0, 1.0);
        double target = stamp.Restore ? 255.0 : 0.0;

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            double dy = y - stamp.Y;
            int row = y * width;
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                double dx = x - stamp.X;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;

                double falloffT = dist <= inner ? 1.0 : 1.0 - (dist - inner) / falloff;
                if (falloffT <= 0) continue;
                falloffT = falloffT * falloffT * (3 - 2 * falloffT);
                double amount = falloffT * opacity;
                if (amount <= 0) continue;

                int idx = row + x;
                double value = mask[idx] + (target - mask[idx]) * amount;
                mask[idx] = (byte)Math.Clamp((int)(value + 0.5), 0, 255);
            }
        }
    }

    /// <summary>
    /// Terapkan satu cap dengan model Opacity + Flow buildup. The accumulator
    /// allocates only 128x128 tiles touched by this stroke.
    /// </summary>
    public static void StampFlow(byte[] mask, BrushStrokeAccumulator? accum, int width, int height, BrushStamp stamp)
    {
        if (!HasValidMask(mask, width, height)) return;
        if (accum == null) { Stamp(mask, width, height, stamp); return; }
        if (accum.Width != width || accum.Height != height) return;
        var bounds = GetStampBounds(stamp, width, height);
        if (bounds.IsEmpty) return;

        double radius = stamp.Radius;
        double hardness = FiniteClamp(stamp.Hardness, 0.0, 1.0);
        double inner = radius * hardness;
        double falloff = Math.Max(0.5, radius - inner);
        double opacity = FiniteClamp(stamp.Opacity, 0.0, 1.0);
        double flow = FiniteClamp(stamp.Flow, 0.0, 1.0);
        double target = stamp.Restore ? 255.0 : 0.0;

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            double dy = y - stamp.Y;
            int row = y * width;
            int x = bounds.X;
            while (x < bounds.Right)
            {
                int tileX = x / BrushStrokeAccumulator.TileSize;
                int tileEnd = Math.Min(bounds.Right, (tileX + 1) * BrushStrokeAccumulator.TileSize);
                var tile = accum.GetOrCreateTile(tileX, y / BrushStrokeAccumulator.TileSize);
                int tileRow = (y % BrushStrokeAccumulator.TileSize) * BrushStrokeAccumulator.TileSize;

                for (; x < tileEnd; x++)
                {
                    double dx = x - stamp.X;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radius) continue;
                    double falloffT = dist <= inner ? 1.0 : 1.0 - (dist - inner) / falloff;
                    if (falloffT <= 0) continue;
                    falloffT = falloffT * falloffT * (3 - 2 * falloffT);

                    int maskIndex = row + x;
                    int accumIndex = tileRow + x % BrushStrokeAccumulator.TileSize;
                    double priorFlow = tile[accumIndex] / 255.0;
                    double oldEffect = opacity * priorFlow;
                    double newFlow = Math.Min(1.0, priorFlow + falloffT * flow);
                    double newEffect = opacity * newFlow;
                    tile[accumIndex] = (byte)Math.Clamp((int)(newFlow * 255 + 0.5), 0, 255);
                    if (oldEffect >= 1.0) continue;

                    double amount = (newEffect - oldEffect) / (1.0 - oldEffect);
                    if (amount <= 0) continue;
                    double value = mask[maskIndex] + (target - mask[maskIndex]) * amount;
                    mask[maskIndex] = (byte)Math.Clamp((int)(value + 0.5), 0, 255);
                }
            }
        }
    }

    /// <summary>Bounds of pixels the rasterizer may touch for a stamp.</summary>
    public static PixelBounds GetStampBounds(BrushStamp stamp, int width, int height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(stamp.X) || !double.IsFinite(stamp.Y))
            return PixelBounds.Empty;
        double radius = stamp.Radius;
        if (!double.IsFinite(radius) || radius < 0.5) return PixelBounds.Empty;

        double cx = Math.Round(stamp.X), cy = Math.Round(stamp.Y);
        double extent = Math.Ceiling(radius);
        double left = Math.Max(0, cx - extent), top = Math.Max(0, cy - extent);
        double right = Math.Min(width - 1.0, cx + extent), bottom = Math.Min(height - 1.0, cy + extent);
        if (right < left || bottom < top) return PixelBounds.Empty;

        int x0 = (int)left, y0 = (int)top;
        int x1 = (int)right, y1 = (int)bottom;
        return new PixelBounds(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static bool HasValidMask(byte[]? mask, int width, int height)
        => mask != null && width > 0 && height > 0 && (long)width * height <= mask.Length;

    private static double FiniteClamp(double value, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    /// <summary>
    /// Bikin daftar cap di sepanjang segmen garis (untuk interpolasi gerakan
    /// pointer sehingga goresan mulus, bukan titik terpisah).
    /// </summary>
    public static void Interpolate(System.Collections.Generic.List<BrushStamp> output,
        BrushStamp from, BrushStamp to, double spacingPx)
    {
        if (output == null) return;
        double spacing = double.IsFinite(spacingPx) ? Math.Max(1.0, spacingPx) : 1.0;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(dist)) return;
        if (dist <= spacing)
        {
            // Segmen pendek tetap butuh satu cap; versi lama menjatuhkan cap ini.
            output.Add(to);
            return;
        }
        int steps = (int)Math.Min(100_000, Math.Ceiling(dist / spacing));
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            var s = to;
            s.X = from.X + dx * t;
            s.Y = from.Y + dy * t;
            output.Add(s);
        }
    }
}

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
/// Restore menaikkan menuju 255. Akumulasi per-stroke dilakukan oleh caller
/// (satu cap per posisi interpolasi), sehingga opacity tidak menumpuk gelap.
/// </summary>
public static class BrushTool
{
    /// <summary>
    /// Terapkan satu cap ke mask.
    /// </summary>
    public static void Stamp(byte[] mask, int width, int height, BrushStamp stamp)
    {
        if (mask == null) return;
        double radius = stamp.Radius;
        if (radius < 0.5) return;

        int cx = (int)Math.Round(stamp.X);
        int cy = (int)Math.Round(stamp.Y);
        int r = (int)Math.Ceiling(radius);
        int x0 = Math.Max(0, cx - r), x1 = Math.Min(width - 1, cx + r);
        int y0 = Math.Max(0, cy - r), y1 = Math.Min(height - 1, cy + r);
        if (x1 < x0 || y1 < y0) return;

        // Hardness mengatur radius "inti" penuh. hardness=1 → inti hampir seluruh radius.
        double hardness = Math.Clamp(stamp.Hardness, 0.0, 1.0);
        double inner = radius * hardness;
        double falloff = Math.Max(0.5, radius - inner);

        double opacity = Math.Clamp(stamp.Opacity, 0.0, 1.0);
        double target = stamp.Restore ? 255.0 : 0.0;

        for (int y = y0; y <= y1; y++)
        {
            double dy = y - stamp.Y;
            int row = y * width;
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - stamp.X;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;

                double falloffT;
                if (dist <= inner) falloffT = 1.0;
                else falloffT = 1.0 - (dist - inner) / falloff;
                if (falloffT <= 0) continue;
                // Kurva halus agar tepi brush tidak "banding".
                falloffT = falloffT * falloffT * (3 - 2 * falloffT);

                double a = falloffT * opacity;
                if (a <= 0) continue;

                int idx = row + x;
                double cur = mask[idx];
                double v = cur + (target - cur) * a;
                mask[idx] = (byte)Math.Clamp((int)(v + 0.5), 0, 255);
            }
        }
    }

    /// <summary>
    /// Terapkan satu cap dengan model Opacity cap + Flow buildup (Photoshop).
    /// param accum menyimpan akumulasi goresan per-piksel (0..255) sejak 
    /// awal stroke; dengan buffer ini Flow dapat menumpuk antar-cap, sedangkan 
    /// Opacity membatasi hasil akhir stroke. Bila accum null, fallback ke Stamp.
    /// </summary>
    public static void StampFlow(byte[] mask, byte[]? accum, int width, int height, BrushStamp stamp)
    {
        if (mask == null) return;
        if (accum == null || accum.Length < mask.Length) { Stamp(mask, width, height, stamp); return; }
        double radius = stamp.Radius;
        if (radius < 0.5) return;
        int cx = (int)Math.Round(stamp.X);
        int cy = (int)Math.Round(stamp.Y);
        int r = (int)Math.Ceiling(radius);
        int x0 = Math.Max(0, cx - r), x1 = Math.Min(width - 1, cx + r);
        int y0 = Math.Max(0, cy - r), y1 = Math.Min(height - 1, cy + r);
        if (x1 < x0 || y1 < y0) return;
        double hardness = Math.Clamp(stamp.Hardness, 0.0, 1.0);
        double inner = radius * hardness;
        double falloff = Math.Max(0.5, radius - inner);
        double opacity = Math.Clamp(stamp.Opacity, 0.0, 1.0);
        double flow = Math.Clamp(stamp.Flow, 0.0, 1.0);
        double target = stamp.Restore ? 255.0 : 0.0;
        for (int y = y0; y <= y1; y++)
        {
            double dy = y - stamp.Y;
            int row = y * width;
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - stamp.X;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                double falloffT;
                if (dist <= inner) falloffT = 1.0;
                else falloffT = 1.0 - (dist - inner) / falloff;
                if (falloffT <= 0) continue;
                falloffT = falloffT * falloffT * (3 - 2 * falloffT);
                int idx = row + x;
                double s = accum[idx] / 255.0;
                double oldE = opacity * s;
                double newS = s + falloffT * flow;
                if (newS > 1.0) newS = 1.0;
                double newE = opacity * newS;
                accum[idx] = (byte)Math.Clamp((int)(newS * 255 + 0.5), 0, 255);
                if (oldE >= 1.0) continue;
                double a = (newE - oldE) / (1.0 - oldE);
                if (a <= 0) continue;
                double cur = mask[idx];
                double v = cur + (target - cur) * a;
                mask[idx] = (byte)Math.Clamp((int)(v + 0.5), 0, 255);
            }
        }
    }
    /// <summary>
    /// Bikin daftar cap di sepanjang segmen garis (untuk interpolasi gerakan
    /// pointer sehingga goresan mulus, bukan titik terpisah).
    /// </summary>
    public static void Interpolate(System.Collections.Generic.List<BrushStamp> output,
        BrushStamp from, BrushStamp to, double spacingPx)
    {
        if (output == null) return;
        double spacing = Math.Max(1.0, spacingPx);
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= spacing)
        {
            // Segmen pendek tetap butuh satu cap; versi lama menjatuhkan cap ini.
            output.Add(to);
            return;
        }
        int steps = (int)(dist / spacing);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i * spacing / dist;
            if (t > 1) t = 1;
            var s = to;
            s.X = from.X + dx * t;
            s.Y = from.Y + dy * t;
            output.Add(s);
        }
    }
}

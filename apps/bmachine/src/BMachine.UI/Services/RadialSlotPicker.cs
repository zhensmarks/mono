using System;
using System.Collections.Generic;

namespace BMachine.UI.Services;

/// <summary>
/// Menentukan slot radial yang dituju dari posisi pointer memakai SUDUT sektor, bukan jarak
/// ke pusat tombol. Penyebab "aturan aneh" sebelumnya: drop hanya diterima kalau pointer
/// ≤25 px dari pusat tombol, jadi menggeser item ke kuadran tertentu terasa tidak mau.
///
/// Koordinat mengikuti canvas roda pengaturan: 200×200, pusat (100,100), item radius 72.
/// </summary>
public static class RadialSlotPicker
{
    public const double CenterX = 100.0;
    public const double CenterY = 100.0;

    /// <summary>Lebih kecil dari ini = deadzone tengah (tidak memilih apa pun).</summary>
    public const double MinRadius = 34.0;

    /// <summary>Lebih jauh dari ini = di luar roda (drop dibatalkan).</summary>
    public const double MaxRadius = 118.0;

    /// <summary>Setengah lebar sektor 45°.</summary>
    public const double HalfSectorDeg = 22.5;

    /// <summary>
    /// Index slot dengan sudut terdekat ke arah pointer, atau -1 kalau pointer di deadzone /
    /// luar roda / tidak ada slot dalam setengah sektor.
    /// </summary>
    public static int FindNearest(IReadOnlyList<double> slotAngles, double x, double y)
    {
        if (slotAngles == null || slotAngles.Count == 0) return -1;

        double dx = x - CenterX;
        double dy = y - CenterY;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < MinRadius || dist > MaxRadius) return -1;

        // 0 = atas, bertambah searah jarum jam (sama dengan Angle slot).
        double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI) + 90.0;
        if (angle < 0) angle += 360.0;

        int best = -1;
        double bestDiff = HalfSectorDeg;
        for (int i = 0; i < slotAngles.Count; i++)
        {
            double diff = Math.Abs(angle - slotAngles[i]);
            if (diff > 180.0) diff = 360.0 - diff;
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }

        return best;
    }
}
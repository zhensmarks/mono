using System;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Services;

/// <summary>
/// Memilih slot radial yang dituju dari posisi pointer: JARAK ke pusat tiap tombol dengan radius
/// longgar, sehingga seluruh bidang sektor + celah antar tombol tetap bisa jadi target.
///
/// Kenapa bukan sudut dengan pusat canvas hardcode (100,100): <c>RadialSlotsControl</c> adalah
/// ItemsControl yang membentang di dalam kartu, sedangkan canvas 200x200 ada di tengahnya. Akibatnya
/// koordinat pointer bergeser dan arah tertentu selalu meleset. Pusat tombol (slot.X/Y + setengah
/// tombol) adalah acuan yang benar-benar dipakai menggambar, jadi itu yang dipakai di sini.
/// </summary>
public static class RadialSlotPicker
{
    public const double ButtonHalf = 18.0;

    /// <summary>
    /// Radius penerimaan (px). Tombol 36 px, jarak tombol ke pusat roda 72 px, jadi 60 px menutup
    /// seluruh sektor dan celah antar tombol tanpa mengambil deadzone tengah roda.
    /// </summary>
    public const double MaxDistance = 60.0;

    /// <summary>Pusat tombol slot; slot.X/slot.Y adalah pojok kiri-atas.</summary>
    public static (double X, double Y) Center(double slotX, double slotY)
        => (slotX + ButtonHalf, slotY + ButtonHalf);

    /// <summary>Index slot terdekat ke pointer, atau -1 kalau semuanya lebih jauh dari maxDistance.</summary>
    public static int FindNearest(IReadOnlyList<(double X, double Y)> centers, double x, double y,
        double maxDistance = MaxDistance)
    {
        if (centers == null || centers.Count == 0) return -1;

        int best = -1;
        double bestDist = maxDistance;
        for (int i = 0; i < centers.Count; i++)
        {
            double dx = x - centers[i].X;
            double dy = y - centers[i].Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Varian praktis: daftar slot dengan X,Y = pojok kiri-atas.</summary>
    public static int FindNearestForSlots(IReadOnlyList<(double X, double Y)> slotTops, double x, double y,
        double maxDistance = MaxDistance)
        => FindNearest(slotTops.Select(s => Center(s.X, s.Y)).ToList(), x, y, maxDistance);
}

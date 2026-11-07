using System.Collections.Generic;
using BMachine.UI.Services;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Target drop di roda pengaturan = slot yang tombolnya TERDEKAT ke pointer (radius longgar),
/// dihitung dari pusat tombol. Versi lama memakai radius 25 px dari pusat tombol sehingga jalur
/// dari kiri-atas ke kanan-atas (melewati celah antar tombol) tidak pernah dapat target.
/// </summary>
public class RadialSlotPickerTests
{
    // Posisi tombol roda pengaturan: radius 72, tombol 36, canvas 200x200 (packedAngles).
    private static (double X, double Y) Slot(double angleDeg)
    {
        double rad = angleDeg * System.Math.PI / 180.0;
        double cx = 100 + 72 * System.Math.Sin(rad);
        double cy = 100 - 72 * System.Math.Cos(rad);
        return RadialSlotPicker.Center(cx - RadialSlotPicker.ButtonHalf, cy - RadialSlotPicker.ButtonHalf);
    }

    private static readonly double[] Angles = { 0, 45, 315, 90, 270, 135, 225 };
    private static readonly List<(double X, double Y)> Centers = BuildCenters();

    private static List<(double X, double Y)> BuildCenters()
    {
        var list = new List<(double X, double Y)>();
        foreach (var a in Angles) list.Add(Slot(a));
        return list;
    }

    [Fact]
    public void EachQuadrantHitsItsOwnSlot()
    {
        for (int i = 0; i < Angles.Length; i++)
        {
            var (x, y) = Centers[i];
            Assert.Equal(i, RadialSlotPicker.FindNearest(Centers, x, y));
        }
    }

    [Fact]
    public void PointerInTheGapStillGetsATarget()
    {
        // Titik yang dulu gagal: jarak > 25 px dari pusat tombol kanan-atas, tapi jelas mengarah ke sana.
        Assert.Equal(1, RadialSlotPicker.FindNearest(Centers, 120, 70));

        // Titik tepat di celah antara dua tombol: tetap dapat salah satu (bukan -1).
        Assert.InRange(RadialSlotPicker.FindNearest(Centers, 69, 26), 0, 6);
    }

    [Fact]
    public void MiddleDeadzoneAndOutsideGiveNoTarget()
    {
        Assert.Equal(-1, RadialSlotPicker.FindNearest(Centers, 100, 100));   // pusat roda
        Assert.Equal(-1, RadialSlotPicker.FindNearest(Centers, 100, -200));  // jauh di luar
        Assert.Equal(-1, RadialSlotPicker.FindNearest(new List<(double, double)>(), 120, 70));
    }
}

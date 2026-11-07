using System.Collections.Generic;
using BMachine.UI.Services;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Drop slot radial harus ditentukan oleh SUDUT sektor, bukan jarak ke pusat tombol.
/// Keluhan: "mau pindah ke kanan atas tidak mau, kalau ke bawah mau" — versi lama menolak
/// drop yang > 25 px dari pusat tombol, jadi kuadran tertentu terasa mati.
/// </summary>
public class RadialSlotPickerTests
{
    // Urutan sudut yang dipakai roda pengaturan (packedAngles).
    private static readonly double[] Slots = { 0, 45, 315, 90, 270, 135, 225 };

    [Fact]
    public void PointsInQuadrantAreSelectedByAngle()
    {
        Assert.Equal(0, RadialSlotPicker.FindNearest(Slots, 100, 40));   // atas      -> 0°
        Assert.Equal(1, RadialSlotPicker.FindNearest(Slots, 150, 50));   // kanan atas -> 45°
        Assert.Equal(2, RadialSlotPicker.FindNearest(Slots, 50, 50));    // kiri atas -> 315°
        Assert.Equal(3, RadialSlotPicker.FindNearest(Slots, 160, 100));  // kanan     -> 90°
        Assert.Equal(4, RadialSlotPicker.FindNearest(Slots, 40, 100));   // kiri      -> 270°
        Assert.Equal(5, RadialSlotPicker.FindNearest(Slots, 150, 150));  // kanan bwh -> 135°
        Assert.Equal(6, RadialSlotPicker.FindNearest(Slots, 50, 150));   // kiri bwh  -> 225°
    }

    [Fact]
    public void OffCenterPointerStillHitsTheSector()
    {
        // Kasus yang DULU gagal: jarak ke pusat tombol kanan-atas > 25 px (≈37 px) tapi
        // arahnya jelas di sektor 45° -> harus tetap memilih slot kanan-atas.
        Assert.Equal(1, RadialSlotPicker.FindNearest(Slots, 120, 70));
        Assert.Equal(2, RadialSlotPicker.FindNearest(Slots, 80, 70));
    }

    [Fact]
    public void DeadzoneAndOutsideGiveNoTarget()
    {
        Assert.Equal(-1, RadialSlotPicker.FindNearest(Slots, 100, 95));   // deadzone tengah
        Assert.Equal(-1, RadialSlotPicker.FindNearest(Slots, 100, -40));  // di luar roda
        Assert.Equal(-1, RadialSlotPicker.FindNearest(new List<double>(), 150, 50));
    }

    [Fact]
    public void BetweenSectorsGivesNoTarget()
    {
        // Arah selatan (180°) tidak punya slot biasa -> tidak ada target.
        Assert.Equal(-1, RadialSlotPicker.FindNearest(Slots, 100, 180));
        // Slot navigasi punya sudut 180 sendiri.
        Assert.Equal(0, RadialSlotPicker.FindNearest(new List<double> { 180 }, 100, 180));
    }
}
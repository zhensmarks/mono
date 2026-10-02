using System;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Matematika brush HUD ala Photoshop (Alt + klik kanan + geser):
/// geser horizontal mengubah ukuran brush, geser vertikal mengubah hardness
/// (geser ke atas = lebih keras). Dipisah agar bisa di-unit-test.
/// </summary>
public static class BrushHudMath
{
    /// <param name="size0">Ukuran brush awal (px).</param>
    /// <param name="hardness0">Hardness awal (0..1, 1 = tajam).</param>
    /// <param name="dx">Delta horizontal pointer (px layar); positif = lebih besar.</param>
    /// <param name="dy">Delta vertikal pointer (px layar); negatif (ke atas) = lebih keras.</param>
    public static (int size, double hardness) Compute(int size0, double hardness0, double dx, double dy)
    {
        int size = Math.Clamp((int)Math.Round(size0 + dx * 0.75), 1, 500);
        double hardness = Math.Clamp(hardness0 - dy / 250.0, 0.0, 1.0);
        return (size, hardness);
    }
}

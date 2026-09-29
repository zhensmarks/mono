using System;
using Avalonia;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Konversi koordinat layar (relatif terhadap Image control) ↔ koordinat gambar
/// (piksel), dengan dukungan zoom, pan, dan rotasi.
///
/// Konvensi transformasi mengikuti viewer di PreviewWindow:
/// transformasi mengelilingi titik tengah viewport, sehingga urutan
/// terbalikkan adalah: translate(-pan) → scale(1/zoom) → rotate(-rotation).
/// </summary>
public sealed class EditorViewPort
{
    /// <summary>Ukuran gambar (px).</summary>
    public int ImageWidth { get; set; } = 1;
    public int ImageHeight { get; set; } = 1;

    /// <summary>Faktor zoom (1.0 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Rotasi dalam derajat (searah jarum jam, sama dengan UI).</summary>
    public double RotationDegrees { get; set; } = 0;

    /// <summary>Offset pan dalam koordinat layar (px).</summary>
    public double PanX { get; set; } = 0;
    public double PanY { get; set; } = 0;

    /// <summary>Ukuran area tampilan (px) tempat gambar dirender.</summary>
    public double ViewWidth { get; set; } = 1;
    public double ViewHeight { get; set; } = 1;

    private double Rad => RotationDegrees * Math.PI / 180.0;

    /// <summary>
    /// Transformasi layar → gambar (relatif pusat viewport).
    /// Kebalikan eksak dari <see cref="ImageToScreen"/>:
    /// screen = R(θ)·(zoom·img + pan)  ⟹  img = (R(-θ)·screen − pan)/zoom.
    /// Urutan ini meniru TransformGroup viewer (Scale → Translate → Rotate)
    /// yang mengelilingi titik tengah kontrol.
    /// </summary>
    public Vec2 ScreenToImage(Point screen)
    {
        // Batalkan rotasi lebih dulu (berlawanan arah rotasi tampilan).
        double rad = -Rad;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double rx = screen.X * cos - screen.Y * sin;
        double ry = screen.X * sin + screen.Y * cos;

        // Lalu batalkan pan (pan diterapkan SEBELUM rotasi), terakhir skala.
        double z = Zoom <= 0 ? 1 : Zoom;
        return new Vec2((rx - PanX) / z, (ry - PanY) / z);
    }

    public Vec2 ScreenToImage(double sx, double sy) => ScreenToImage(new Point(sx, sy));

    /// <summary>Transformasi gambar → layar (relatif pusat viewport).</summary>
    public Point ImageToScreen(Vec2 image)
    {
        double z = Zoom <= 0 ? 1 : Zoom;
        double x = image.X * z + PanX;
        double y = image.Y * z + PanY;

        double rad = Rad;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double rx = x * cos - y * sin;
        double ry = x * sin + y * cos;

        return new Point(rx, ry);
    }

    public Point ImageToScreen(double ix, double iy) => ImageToScreen(new Vec2(ix, iy));

    /// <summary>
    /// Hitung ulang pan agar gambar di-fit ke viewport (menghormati rotasi).
    /// Setara dengan tombol "Fit" yang sudah ada.
    /// </summary>
    public void FitToView(double padding = 0)
    {
        if (ViewWidth <= 0 || ViewHeight <= 0) return;
        double availW = Math.Max(1, ViewWidth - padding * 2);
        double availH = Math.Max(1, ViewHeight - padding * 2);

        // Bounding box gambar setelah rotasi.
        double rad = Rad;
        double cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
        double bw = ImageWidth * cos + ImageHeight * sin;
        double bh = ImageWidth * sin + ImageHeight * cos;

        double zoom = Math.Min(availW / Math.Max(1, bw), availH / Math.Max(1, bh));
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) zoom = 1;
        Zoom = zoom;

        // Pusatkan: gambar dirender dengan origin di (0,0) lokal lalu dirotasi
        // mengelilingi pusat, sehingga offset pusat = (view - img*zoom)/2 pada
        // koordinat yang belum dirotasi.
        double cx = ImageWidth * zoom / 2.0;
        double cy = ImageHeight * zoom / 2.0;
        double rx = cx * Math.Cos(rad) - cy * Math.Sin(rad);
        double ry = cx * Math.Sin(rad) + cy * Math.Cos(rad);
        PanX = ViewWidth / 2.0 - rx;
        PanY = ViewHeight / 2.0 - ry;
    }

    /// <summary>Kembalikan transformasi ke identitas (zoom 1, no rotasi, terpusat).</summary>
    public void Reset()
    {
        Zoom = 1;
        RotationDegrees = 0;
        PanX = (ViewWidth - ImageWidth) / 2.0;
        PanY = (ViewHeight - ImageHeight) / 2.0;
    }

    /// <summary>Ukuran brush yang tersimpan (koordinat gambar) → radius di layar.</summary>
    public double ImageRadiusToScreen(double radiusPx) => radiusPx * (Zoom <= 0 ? 1 : Zoom);

    /// <summary>Radius layar → radius gambar.</summary>
    public double ScreenRadiusToImage(double radiusPx) => radiusPx / (Zoom <= 0 ? 1 : Zoom);

    /// <summary>Batas gambar yang masih masuk akal (untuk clamp pan).</summary>
    public void ClampPan(double margin = 64)
    {
        double z = Zoom <= 0 ? 1 : Zoom;
        double w = ImageWidth * z;
        double h = ImageHeight * z;
        double limitX = w / 2 + ViewWidth / 2 + margin;
        double limitY = h / 2 + ViewHeight / 2 + margin;
        if (PanX < -limitX) PanX = -limitX;
        if (PanX > limitX) PanX = limitX;
        if (PanY < -limitY) PanY = -limitY;
        if (PanY > limitY) PanY = limitY;
    }
}

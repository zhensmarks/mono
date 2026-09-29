using System;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia;
namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Komposit sumber untuk pratinjau: menggambar RGB dari <see cref="PixelBuffer"/>
/// dengan alpha dari mask, opsional di atas warna background (transparan/kotak-kotak).
/// Dibuat terpisah dari <see cref="MaskEditSession"/> agar bisa di-downsample saat
/// zoom &lt; 1 dan dijaga cepat di UI thread.
/// </summary>
public static class CompositeRenderer
{
    /// <summary>
    /// Komposit mask ke RGB (preview). Mengembalikan <see cref="WriteableBitmap"/>
    /// baru; caller bertanggung jawab Dispose (atau reassign ke Image.Source).
    /// </summary>
    /// <param name="rgb">PixelBuffer RGB sumber (BGRA non-premultiplied).</param>
    /// <param name="mask">Mask alpha 1 byte/px (panjang W*H).</param>
    /// <param name="applyMask">Bila false, alpha diabaikan (tampilkan RGB penuh).</param>
    public static WriteableBitmap Compose(PixelBuffer rgb, byte[]? mask, bool applyMask = true)
    {
        var outBuf = new PixelBuffer(rgb.Width, rgb.Height);
        Buffer.BlockCopy(rgb.Bgra, 0, outBuf.Bgra, 0, rgb.Bgra.Length);

        if (applyMask && mask != null && mask.Length >= rgb.Width * rgb.Height)
            outBuf.ApplyAlphaMask(mask);

        var wb = outBuf.ToWriteableBitmap();
        return wb;
    }

    /// <summary>
    /// Komposit RGB dengan mask dan warna background solid (untuk pratinjau
    /// "checkerboard"/warna solid). Warna background dilebur memakai alpha linear.
    /// </summary>
    public static WriteableBitmap ComposeOnBackground(PixelBuffer rgb, byte[]? mask,
        Color background, bool applyMask = true)
    {
        int w = rgb.Width, h = rgb.Height;
        var outBuf = new PixelBuffer(w, h);
        double br = background.R, bg = background.G, bb = background.B;

        int total = w * h;
        for (int i = 0; i < total; i++)
        {
            int si = i * 4;
            int di = si;
            int a = applyMask && mask != null && i < mask.Length ? mask[i] : rgb.Bgra[si + 3];

            byte b = rgb.Bgra[si + 0];
            byte g = rgb.Bgra[si + 1];
            byte r = rgb.Bgra[si + 2];

            if (a == 255)
            {
                outBuf.Bgra[di + 0] = b;
                outBuf.Bgra[di + 1] = g;
                outBuf.Bgra[di + 2] = r;
            }
            else
            {
                double t = a / 255.0;
                outBuf.Bgra[di + 0] = (byte)Math.Clamp((int)(b * t + bb * (1 - t) + 0.5), 0, 255);
                outBuf.Bgra[di + 1] = (byte)Math.Clamp((int)(g * t + bg * (1 - t) + 0.5), 0, 255);
                outBuf.Bgra[di + 2] = (byte)Math.Clamp((int)(r * t + br * (1 - t) + 0.5), 0, 255);
            }
            outBuf.Bgra[di + 3] = 255;
        }

        return outBuf.ToWriteableBitmap();
    }

    /// <summary>
    /// Buat render quick-mask: overlay merah semi-transparan pada area mask &gt; 0
    /// (atau &lt; 255, tergantung <paramref name="invert"/>). Dikembalikan sebagai
    /// bitmap RGBA siap digambar di atas gambar.
    /// </summary>
    public static WriteableBitmap RenderMaskOverlay(byte[] mask, int width, int height,
        Color color, byte maxAlpha = 128, bool invert = false)
    {
        var buf = new PixelBuffer(width, height);
        int total = width * height;
        double cr = color.R, cg = color.G, cb = color.B;
        double ca = color.A / 255.0;

        for (int i = 0; i < total && i < mask.Length; i++)
        {
            int v = mask[i];
            int coverage = invert ? 255 - v : v;
            if (coverage <= 0) continue;

            double t = (coverage / 255.0) * ca * (maxAlpha / 255.0);
            int di = i * 4;
            buf.Bgra[di + 0] = (byte)Math.Clamp((int)(cb + 0.5), 0, 255);
            buf.Bgra[di + 1] = (byte)Math.Clamp((int)(cg + 0.5), 0, 255);
            buf.Bgra[di + 2] = (byte)Math.Clamp((int)(cr + 0.5), 0, 255);
            buf.Bgra[di + 3] = (byte)Math.Clamp((int)(t * 255 + 0.5), 0, 255);
        }

        return buf.ToWriteableBitmap();
    }

    /// <summary>Versi RenderMaskOverlay yang menulis ke buffer yang sudah ada
    /// (dipakai Quick Mask supaya tidak mengalokasikan PixelBuffer/bitmap tiap gerakan).</summary>
    public static void RenderMaskOverlayInto(PixelBuffer buf, byte[] mask, int width, int height,
        Color color, byte maxAlpha = 128, bool invert = false)
    {
        int total = width * height;
        double cr = color.R, cg = color.G, cb = color.B;
        double ca = color.A / 255.0;
        var bgra = buf.Bgra;
        Array.Clear(bgra, 0, bgra.Length);
        for (int i = 0; i < total && i < mask.Length; i++)
        {
            int v = mask[i];
            int coverage = invert ? 255 - v : v;
            if (coverage <= 0) continue;
            double t = (coverage / 255.0) * ca * (maxAlpha / 255.0);
            int di = i * 4;
            bgra[di + 0] = (byte)Math.Clamp((int)(cb + 0.5), 0, 255);
            bgra[di + 1] = (byte)Math.Clamp((int)(cg + 0.5), 0, 255);
            bgra[di + 2] = (byte)Math.Clamp((int)(cr + 0.5), 0, 255);
            bgra[di + 3] = (byte)Math.Clamp((int)(t * 255 + 0.5), 0, 255);
        }
    }

    /// <summary>
    /// Render marquee seleksi (outline) jadi bitmap overlay tipis. Outline
    /// digambar dengan mendeteksi transisi mask 0↔255 lalu menebalkan 1 px.
    /// </summary>
    public static WriteableBitmap RenderSelectionOutline(byte[] mask, int width, int height, Color color)
    {
        var buf = new PixelBuffer(width, height);
        int total = width * height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (i >= mask.Length) continue;
                bool inside = mask[i] > 127;
                bool edge = false;
                if (x + 1 < width) edge |= (mask[i + 1] > 127) != inside;
                if (x > 0) edge |= (mask[i - 1] > 127) != inside;
                if (y + 1 < height) edge |= (mask[i + width] > 127) != inside;
                if (y > 0) edge |= (mask[i - width] > 127) != inside;
                if (!edge) continue;

                int di = i * 4;
                buf.Bgra[di + 0] = color.B;
                buf.Bgra[di + 1] = color.G;
                buf.Bgra[di + 2] = color.R;
                buf.Bgra[di + 3] = 255;
            }
        }
        return buf.ToWriteableBitmap();
    }

    /// <summary>
    /// Downsample komposit untuk pratinjau cepat. Mengembalikan bitmap dengan
    /// ukuran <paramref name="targetW"/>×<paramref name="targetH"/>.
    /// </summary>
    public static Bitmap ComposeScaled(PixelBuffer rgb, byte[]? mask, int targetW, int targetH)
    {
        using var full = Compose(rgb, mask, true);
        if (full.PixelSize.Width == targetW && full.PixelSize.Height == targetH)
        {
            // Copy: Bitmap harus independen dari WriteableBitmap sumber.
            return CloneBitmap(full);
        }

        // Downscale via RenderTargetBitmap (bilinear-ish melalui Skia).
        var rtb = new RenderTargetBitmap(new PixelSize(targetW, targetH), new Avalonia.Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawImage(full, new Avalonia.Rect(0, 0, targetW, targetH));
        }
        return rtb;
    }

    private static Bitmap CloneBitmap(Bitmap source)
    {
        int w = source.PixelSize.Width, h = source.PixelSize.Height;
        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawImage(source, new Avalonia.Rect(0, 0, w, h));
        }
        return rtb;
    }
}

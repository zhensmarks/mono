using System;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Buffer piksel RGBA (premultiplied-aware) di memori, terpisah dari GPU bitmap.
/// Menyediakan konversi dari/ke Avalonia <see cref="Bitmap"/> serta akses span.
/// Format internal: BGRA8888 non-premultiplied di layout B,G,R,A (sesuai Avalonia
/// default <see cref="PixelFormat.Bgra8888"/>), disimpan sebagai byte[].
/// </summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA bytes, urutan B,G,R,A. Panjang = Width*Height*4.</summary>
    public byte[] Bgra { get; }

    public PixelBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Bgra = new byte[checked(width * height * 4)];
    }

    /// <summary>Buat buffer RGBA kosong (transparan hitam) dengan ukuran tertentu.</summary>
    public static PixelBuffer Create(int width, int height) => new(width, height);

    /// <summary>
    /// Buat buffer dari mask alpha 8-bit: RGB diisi hitam, alpha dari mask.
    /// Dipakai untuk resize mask hasil matting.
    /// </summary>
    public static PixelBuffer FromAlpha(byte[] alpha, int width, int height)
    {
        if (alpha == null) throw new ArgumentNullException(nameof(alpha));
        if (alpha.Length < width * height) throw new ArgumentException("Panjang alpha tidak sesuai.", nameof(alpha));
        var buf = new PixelBuffer(width, height);
        for (int i = 0; i < width * height; i++)
        {
            int bi = i * 4;
            buf.Bgra[bi + 0] = 0;
            buf.Bgra[bi + 1] = 0;
            buf.Bgra[bi + 2] = 0;
            buf.Bgra[bi + 3] = alpha[i];
        }
        return buf;
    }

    public int Stride => Width * 4;

    public int Index(int x, int y) => (y * Width + x) * 4;

    /// <summary>Ambil RGB pada piksel (0..255).</summary>
    public void GetRgb(int x, int y, out byte r, out byte g, out byte b)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) { r = g = b = 0; return; }
        int i = Index(x, y);
        b = Bgra[i];
        g = Bgra[i + 1];
        r = Bgra[i + 2];
    }

    public byte GetAlpha(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        return Bgra[Index(x, y) + 3];
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        int i = Index(x, y);
        Bgra[i] = b;
        Bgra[i + 1] = g;
        Bgra[i + 2] = r;
        Bgra[i + 3] = a;
    }

    public void SetAlpha(int x, int y, byte a)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        Bgra[Index(x, y) + 3] = a;
    }

    // ========================
    // KONVERSI AVALONIA
    // ========================

    /// <summary>
    /// Baca dari Avalonia Bitmap ke buffer kita. Cepat: salin piksel LANGSUNG
    /// via CopyPixels (tanpa PNG encode/decode). Data Bgra8888 premultiplied
    /// di-unpremultiply agar konsisten dengan format internal (non-premul).
    /// </summary>
    public static PixelBuffer FromBitmap(Bitmap bitmap)
    {
        int w = bitmap.PixelSize.Width;
        int h = bitmap.PixelSize.Height;
        var buffer = new PixelBuffer(w, h);

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer.Bgra,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, w, h),
                handle.AddrOfPinnedObject(), buffer.Bgra.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        UnpremultiplyInPlace(buffer.Bgra);
        return buffer;
    }

    /// <summary>
    /// Unpremultiply BGRA in-place (CopyPixels menghasilkan premultiplied).
    /// Alpha 0 → RGB dipaksa 0 agar cutout transparan tidak jadi hitam pekat.
    /// </summary>
    private static void UnpremultiplyInPlace(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a == 255) continue;
            if (a == 0)
            {
                bgra[i + 0] = 0; bgra[i + 1] = 0; bgra[i + 2] = 0;
                continue;
            }
            bgra[i + 0] = Unpremul(bgra[i + 0], a);
            bgra[i + 1] = Unpremul(bgra[i + 1], a);
            bgra[i + 2] = Unpremul(bgra[i + 2], a);
        }
    }

    private static byte Unpremul(byte c, byte a)
    {
        int v = (c * 255 + (a / 2)) / a;
        return v > 255 ? (byte)255 : (byte)v;
    }

    private static byte Premul(byte c, byte a)
    {
        return (byte)((c * a + 127) / 255);
    }

    /// <summary>Konversi ke WriteableBitmap siap-render (premultiplied BGRA).</summary>
    public WriteableBitmap ToWriteableBitmap()
    {
        var wb = new WriteableBitmap(new Avalonia.PixelSize(Width, Height),
            new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        WriteTo(wb);
        return wb;
    }

    /// <summary>Tulis isi buffer ke WriteableBitmap yang sudah ada (premultiplied).</summary>
    public void WriteTo(WriteableBitmap wb)
    {
        var locked = wb.Lock();
        try
        {
            unsafe
            {
                byte* dst = (byte*)locked.Address;
                int dstStride = locked.RowBytes;
                for (int y = 0; y < Height; y++)
                {
                    byte* row = dst + (long)y * dstStride;
                    int si = y * Width * 4;
                    for (int x = 0; x < Width; x++)
                    {
                        byte b = Bgra[si + 0];
                        byte g = Bgra[si + 1];
                        byte r = Bgra[si + 2];
                        byte a = Bgra[si + 3];
                        if (a == 255)
                        {
                            row[x * 4 + 0] = b; row[x * 4 + 1] = g; row[x * 4 + 2] = r; row[x * 4 + 3] = 255;
                        }
                        else if (a == 0)
                        {
                            row[x * 4 + 0] = 0; row[x * 4 + 1] = 0; row[x * 4 + 2] = 0; row[x * 4 + 3] = 0;
                        }
                        else
                        {
                            row[x * 4 + 0] = Premul(b, a);
                            row[x * 4 + 1] = Premul(g, a);
                            row[x * 4 + 2] = Premul(r, a);
                            row[x * 4 + 3] = a;
                        }
                        si += 4;
                    }
                }
            }
        }
        finally
        {
            locked.Dispose();
        }
    }

    /// <summary>Buat WriteableBitmap non-premultiplied (Skia meng-handle blend),
    /// sehingga isi buffer bisa disalin langsung tanpa konversi per-piksel.</summary>
    public WriteableBitmap ToWriteableBitmapUnpremul()
    {
        var wb = new WriteableBitmap(new Avalonia.PixelSize(Width, Height),
            new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        WriteToUnpremul(wb);
        return wb;
    }

    /// <summary>Salin langsung isi buffer (non-premul) ke WriteableBitmap non-premul.</summary>
    public void WriteToUnpremul(WriteableBitmap wb)
        => WriteToUnpremul(wb, PixelBounds.Full(Width, Height));

    /// <summary>Salin hanya region yang berubah ke WriteableBitmap non-premul.</summary>
    public void WriteToUnpremul(WriteableBitmap wb, PixelBounds bounds)
    {
        if (wb == null) throw new ArgumentNullException(nameof(wb));
        if (wb.PixelSize.Width != Width || wb.PixelSize.Height != Height)
            throw new ArgumentException("Ukuran bitmap tujuan harus sama dengan buffer.", nameof(wb));
        bounds = bounds.ClampTo(Width, Height);
        if (bounds.IsEmpty) return;

        var locked = wb.Lock();
        try
        {
            unsafe
            {
                byte* dst = (byte*)locked.Address;
                int dstStride = locked.RowBytes;
                int rowBytes = Width * 4;
                int copyBytes = bounds.Width * 4;
                int sourceX = bounds.X * 4;
                for (int y = bounds.Y; y < bounds.Bottom; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(Bgra, y * rowBytes + sourceX,
                        (IntPtr)(dst + (long)y * dstStride + sourceX), copyBytes);
                }
            }
        }
        finally
        {
            locked.Dispose();
        }
    }

    /// <summary>
    /// Ekspor ke PNG. Memakai encoder Skia LANGSUNG dari buffer BGRA (tanpa
    /// round-trip Avalonia) dengan level kompresi rendah agar save jauh lebih cepat.
    /// Fallback ke jalur Avalonia bila Skia gagal.
    /// </summary>
    public void SavePng(System.IO.Stream stream)
    {
        try
        {
            var info = new SkiaSharp.SKImageInfo(Width, Height,
                SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(Bgra,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                using var pixmap = new SkiaSharp.SKPixmap(info, handle.AddrOfPinnedObject(), Width * 4);
                // Level zlib rendah (1) = encode ~3-5x lebih cepat dari default 6,
                // ukuran file naik sedikit tapi save jauh lebih responsif.
                var opts = new SkiaSharp.SKPngEncoderOptions(
                    SkiaSharp.SKPngEncoderFilterFlags.AllFilters, 1);
                if (!pixmap.Encode(stream, opts)) SavePngViaAvalonia(stream);
            }
            finally { handle.Free(); }
        }
        catch
        {
            SavePngViaAvalonia(stream);
        }
    }

    /// <summary>Jalur lama (Avalonia Bitmap.Save) sebagai fallback.</summary>
    private void SavePngViaAvalonia(System.IO.Stream stream)
    {
        using var bmp = ToAvaloniaBitmap();
        bmp.Save(stream);
    }

    /// <summary>
    /// <summary>
    /// Buat Bitmap Avalonia mandiri dari buffer ini via PNG encode/decode.
    /// Lebih ringan daripada RenderTargetBitmap, dan hasilnya tidak pernah null/disposed.
    /// </summary>
    public Bitmap ToAvaloniaBitmap()
    {
        using var ms = new System.IO.MemoryStream();
        // Encode via WriteableBitmap.Save ke PNG
        using var wb = ToWriteableBitmap();
        wb.Save(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    /// <summary>Encode PNG ke byte[] (tanpa stream).</summary>
    public byte[] ToPngBytes()
    {
        using var ms = new System.IO.MemoryStream();
        SavePng(ms);
        return ms.ToArray();
    }

    public PixelBuffer Clone()
    {
        var copy = new PixelBuffer(Width, Height);
        Buffer.BlockCopy(Bgra, 0, copy.Bgra, 0, Bgra.Length);
        return copy;
    }

    /// <summary>Ambil salinan hanya kanal alpha sebagai mask 1 byte/piksel.</summary>
    public byte[] ExtractAlphaMask()
    {
        var mask = new byte[Width * Height];
        for (int i = 0, j = 3; i < mask.Length; i++, j += 4)
            mask[i] = Bgra[j];
        return mask;
    }

    /// <summary>Terapkan mask alpha (1 byte/piksel) ke kanal alpha buffer ini.</summary>
    public void ApplyAlphaMask(ReadOnlySpan<byte> mask)
    {
        int total = Width * Height;
        for (int i = 0, j = 3; i < total && i < mask.Length; i++, j += 4)
            Bgra[j] = mask[i];
    }

    /// <summary>Isi seluruh kanal alpha dengan nilai tertentu.</summary>
    public void FillAlpha(byte a)
    {
        for (int j = 3; j < Bgra.Length; j += 4) Bgra[j] = a;
    }

    /// <summary>Resize bilinear ke ukuran baru (untuk preview downscale).</summary>
    public PixelBuffer Resize(int newW, int newH)
    {
        var dst = new PixelBuffer(newW, newH);
        if (newW == Width && newH == Height) { Buffer.BlockCopy(Bgra, 0, dst.Bgra, 0, Bgra.Length); return dst; }
        double sx = (double)Width / newW;
        double sy = (double)Height / newH;
        for (int y = 0; y < newH; y++)
        {
            double fy = (y + 0.5) * sy - 0.5;
            int y0 = (int)Math.Floor(fy);
            double wy = fy - y0;
            if (y0 < 0) { y0 = 0; wy = 0; }
            int y1 = Math.Min(y0 + 1, Height - 1);
            if (y0 >= Height) y0 = Height - 1;
            for (int x = 0; x < newW; x++)
            {
                double fx = (x + 0.5) * sx - 0.5;
                int x0 = (int)Math.Floor(fx);
                double wx = fx - x0;
                if (x0 < 0) { x0 = 0; wx = 0; }
                int x1 = Math.Min(x0 + 1, Width - 1);
                if (x0 >= Width) x0 = Width - 1;

                int i00 = Index(x0, y0), i10 = Index(x1, y0);
                int i01 = Index(x0, y1), i11 = Index(x1, y1);
                int d = dst.Index(x, y);
                for (int c = 0; c < 4; c++)
                {
                    double top = Bgra[i00 + c] * (1 - wx) + Bgra[i10 + c] * wx;
                    double bot = Bgra[i01 + c] * (1 - wx) + Bgra[i11 + c] * wx;
                    double v = top * (1 - wy) + bot * wy;
                    dst.Bgra[d + c] = (byte)Math.Clamp((int)(v + 0.5), 0, 255);
                }
            }
        }
        return dst;
    }

    /// <summary>Salinan hanya kanal RGB dengan alpha penuh (untuk sumber matting).</summary>
    public PixelBuffer ToOpaqueRgb()
    {
        var copy = new PixelBuffer(Width, Height);
        Buffer.BlockCopy(Bgra, 0, copy.Bgra, 0, Bgra.Length);
        copy.FillAlpha(255);
        return copy;
    }
}

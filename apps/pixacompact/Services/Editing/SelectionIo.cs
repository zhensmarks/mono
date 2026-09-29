using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Simpan / muat lapisan seleksi ke file PNG grayscale: nilai coverage
/// (0..255) disimpan sebagai luminance, alpha penuh. Saat dimuat dan ukuran
/// gambar berbeda, coverage diskalakan bilinear.
/// </summary>
public static class SelectionIo
{
    /// <summary>Simpan seleksi ke PNG grayscale (coverage → luminance).</summary>
    public static async Task SaveAsync(SelectionState selection, string path)
    {
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path kosong.", nameof(path));

        var buf = new PixelBuffer(selection.Width, selection.Height);
        var bgra = buf.Bgra;
        var cov = selection.Coverage;
        for (int i = 0, bi = 0; i < cov.Length; i++, bi += 4)
        {
            byte v = cov[i];
            bgra[bi + 0] = v; // B
            bgra[bi + 1] = v; // G
            bgra[bi + 2] = v; // R
            bgra[bi + 3] = 255;
        }

        byte[] png = buf.ToPngBytes();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, png).ConfigureAwait(false);
    }

    /// <summary>
    /// Muat seleksi dari PNG grayscale. Bila ukuran berbeda dengan
    /// <paramref name="width"/>×<paramref name="height"/>, coverage diskalakan.
    /// </summary>
    public static async Task<SelectionState> LoadAsync(string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path kosong.", nameof(path));
        byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        using var ms = new MemoryStream(bytes);
        using var bitmap = new Bitmap(ms);
        var buf = PixelBuffer.FromBitmap(bitmap);
        return FromPixelBuffer(buf, width, height);
    }

    /// <summary>Konversi buffer RGBA (luminance/merah sebagai coverage) ke SelectionState.</summary>
    public static SelectionState FromPixelBuffer(PixelBuffer buf, int width, int height)
    {
        if (buf == null) throw new ArgumentNullException(nameof(buf));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        var scaled = buf;
        if (buf.Width != width || buf.Height != height)
            scaled = buf.Resize(width, height);

        var sel = new SelectionState(width, height);
        var cov = sel.Coverage;
        var bgra = scaled.Bgra;
        for (int i = 0, bi = 0; i < cov.Length; i++, bi += 4)
        {
            // Rata-rata saluran (biasanya identik untuk grayscale).
            int lum = (bgra[bi + 0] + bgra[bi + 1] + bgra[bi + 2]) / 3;
            cov[i] = (byte)lum;
        }
        sel.NotifyChanged();
        return sel;
    }
}

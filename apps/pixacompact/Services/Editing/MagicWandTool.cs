using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Magic Wand: flood fill dari titik klik berdasarkan toleransi warna.
/// Sumber sampel adalah buffer BGRA non-premultiplied (format internal
/// <see cref="PixelBuffer"/>), sehingga alpha ikut dipertimbangkan.
/// Hasil berupa mask 8-bit (0 / 255) yang siap di-commit ke sesi edit.
/// </summary>
public static class MagicWandTool
{
    /// <summary>
    /// Hitung mask seleksi dari titik <paramref name="seedX"/>, <paramref name="seedY"/>.
    /// </summary>
    /// <param name="bgra">Buffer BGRA non-premultiplied (panjang W*H*4).</param>
    /// <param name="width">Lebar gambar.</param>
    /// <param name="height">Tinggi gambar.</param>
    /// <param name="seedX">X titik klik (koordinat gambar, boleh di luar → dikembalikan null).</param>
    /// <param name="seedY">Y titik klik.</param>
    /// <param name="tolerance">Toleransi 0..255 terhadap jarak Euclidean RGB+Alpha.</param>
    /// <param name="contiguous">True = hanya area tersambung dari titik klik.</param>
    /// <param name="sampleAlpha">True = alpha ikut jadi bagian jarak warna.</param>
    /// <param name="includeAlpha">Bobot kanal alpha (default sama dengan RGB).</param>
    public static byte[]? Compute(byte[] bgra, int width, int height,
        int seedX, int seedY, double tolerance, bool contiguous, bool sampleAlpha, double includeAlpha = 1.0)
        => Compute(bgra, width, height, seedX, seedY, tolerance, contiguous, sampleAlpha, includeAlpha, eightConnected: false);

    /// <summary>
    /// Seperti <see cref="Compute(byte[],int,int,int,int,double,bool,bool,double)"/> dengan
    /// pilihan konektivitas: <paramref name="eightConnected"/> = true memakai 8 tetangga
    /// (termasuk diagonal), false = 4-arah (default, perilaku lama).
    /// </summary>
    public static byte[]? Compute(byte[] bgra, int width, int height,
        int seedX, int seedY, double tolerance, bool contiguous, bool sampleAlpha,
        double includeAlpha, bool eightConnected)
    {
        if (bgra == null || width <= 0 || height <= 0) return null;
        if (seedX < 0 || seedY < 0 || seedX >= width || seedY >= height) return null;

        int total = width * height;
        if (bgra.Length < total * 4) return null;

        var mask = new byte[total];
        int si = (seedY * width + seedX) * 4;
        byte sb = bgra[si + 0], sg = bgra[si + 1], sr = bgra[si + 2], sa = bgra[si + 3];

        double tol = tolerance;
        if (tol < 0) tol = 0;
        if (tol > 255) tol = 255;
        double tol2 = tol * tol;

        bool Matches(int i)
        {
            int bi = i * 4;
            double db = bgra[bi + 0] - sb;
            double dg = bgra[bi + 1] - sg;
            double dr = bgra[bi + 2] - sr;
            double da = sampleAlpha ? (bgra[bi + 3] - sa) * includeAlpha : 0;
            double d2 = 0.299 * dr * dr + 0.587 * dg * dg + 0.114 * db * db + da * da;
            return d2 <= tol2;
        }

        if (!contiguous)
        {
            // Non-contiguous: seluruh piksel di gambar yang cocok.
            for (int i = 0; i < total; i++)
                if (Matches(i)) mask[i] = 255;
            return mask;
        }

        // Contiguous: BFS/DFS berbasis stack eksplisit + visited bitmap.
        var visited = new bool[total];
        var stack = new Stack<int>(1024);
        int seed = seedY * width + seedX;
        stack.Push(seed);
        visited[seed] = true;

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (!Matches(i)) continue;
            mask[i] = 255;

            int x = i % width;
            int y = i / width;

            if (x > 0) PushIfNew(i - 1);
            if (x < width - 1) PushIfNew(i + 1);
            if (y > 0) PushIfNew(i - width);
            if (y < height - 1) PushIfNew(i + width);

            if (eightConnected)
            {
                if (x > 0 && y > 0) PushIfNew(i - width - 1);
                if (x < width - 1 && y > 0) PushIfNew(i - width + 1);
                if (x > 0 && y < height - 1) PushIfNew(i + width - 1);
                if (x < width - 1 && y < height - 1) PushIfNew(i + width + 1);
            }
        }

        void PushIfNew(int idx)
        {
            if (visited[idx]) return;
            visited[idx] = true;
            stack.Push(idx);
        }

        return mask;
    }

    /// <summary>
    /// Seperti <see cref="Compute"/>, tetapi memakai mask yang sudah ada sebagai
    /// batas: hanya piksel yang sudah terseleksi (mask &gt; 0) yang ikut dipertimbangkan.
    /// </summary>
    public static byte[]? ComputeWithin(byte[] bgra, byte[] restrictMask, int width, int height,
        int seedX, int seedY, double tolerance, bool contiguous, bool sampleAlpha)
    {
        if (restrictMask == null || restrictMask.Length < width * height) return null;
        if ((uint)seedX >= (uint)width || (uint)seedY >= (uint)height) return null;
        if (restrictMask[seedY * width + seedX] == 0) return null;

        // Restriction masuk ke BFS, bukan hanya dipotong setelah flood-fill.
        var result = ComputeRestricted(bgra, restrictMask, width, height, seedX, seedY,
            tolerance, contiguous, sampleAlpha);
        return result;
    }

    private static byte[]? ComputeRestricted(byte[] bgra, byte[] restrictMask, int width, int height,
        int seedX, int seedY, double tolerance, bool contiguous, bool sampleAlpha)
    {
        var result = Compute(bgra, width, height, seedX, seedY, tolerance, contiguous, sampleAlpha);
        if (result == null) return null;
        for (int i = 0; i < result.Length; i++)
            if (restrictMask[i] == 0) result[i] = 0;
        return result;
    }
}

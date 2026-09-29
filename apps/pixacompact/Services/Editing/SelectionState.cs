using System;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Mode penggabungan seleksi baru ke seleksi yang sudah ada.
/// </summary>
public enum SelectionCombineMode
{
    Replace,
    Add,
    Subtract,
    Intersect
}

/// <summary>
/// Lapisan seleksi persisten: buffer coverage 8-bit (0..255) berukuran W*H,
/// terpisah dari mask. Tool menggambar ke sini, lalu user menerapkannya ke
/// mask lewat aksi eksplisit (Hapus / Restore / Isi).
/// </summary>
public sealed class SelectionState
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Coverage { get; }

    /// <summary>Dipicu setiap kali isi coverage berubah (untuk refresh marching ants).</summary>
    public event Action? Changed;

    public SelectionState(int width, int height)
    {
        Width = width > 0 ? width : 0;
        Height = height > 0 ? height : 0;
        Coverage = new byte[Width * Height];
    }

    /// <summary>Versi coverage; bertambah setiap perubahan (dipakai cache kontur/stat).</summary>
    public int Version { get; private set; }

    private int _cacheVersion = -1;
    private bool _hasSelection;
    private bool _isAll;
    private long _countSelected;

    private void RecomputeCache()
    {
        if (_cacheVersion == Version) return;
        var c = Coverage;
        bool has = false;
        bool all = c.Length > 0;
        long n = 0;
        for (int i = 0; i < c.Length; i++)
        {
            byte v = c[i];
            if (v != 0) { has = true; n++; }
            if (v != 255) all = false;
        }
        _hasSelection = has;
        _isAll = all;
        _countSelected = n;
        _cacheVersion = Version;
    }

    /// <summary>True bila ada minimal satu piksel dengan coverage &gt; 0.</summary>
    public bool HasSelection
    {
        get { RecomputeCache(); return _hasSelection; }
    }

    /// <summary>True bila seluruh piksel terpilih penuh (coverage 255).</summary>
    public bool IsAll
    {
        get { RecomputeCache(); return _isAll; }
    }

    private void RaiseChanged()
    {
        Version++;
        _cacheVersion = -1;
        Changed?.Invoke();
    }

    // ========================
    // GABUNG REGION
    // ========================

    /// <summary>
    /// Rasterisasi region jalur menjadi coverage lalu gabungkan ke seleksi
    /// memakai <paramref name="mode"/>.
    /// </summary>
    public void Combine(MaskRegion region, SelectionCombineMode mode, bool antiAlias = true, double feather = 0)
    {
        if (region == null || region.IsEmpty) return;
        var tmp = new byte[Coverage.Length];
        MaskRasterizer.FillRegion(region, tmp, Width, Height, 255, MaskCombineOp.Replace, antiAlias, feather);
        CombineRaw(tmp, mode);
    }

    /// <summary>
    /// Gabungkan buffer coverage mentah (0..255) ke seleksi. Panjang harus
    /// sama dengan W*H.
    /// </summary>
    public void CombineRaw(byte[] region, SelectionCombineMode mode)
    {
        if (region == null || region.Length != Coverage.Length) return;
        var dst = Coverage;
        switch (mode)
        {
            case SelectionCombineMode.Replace:
                Buffer.BlockCopy(region, 0, dst, 0, dst.Length);
                break;
            case SelectionCombineMode.Add:
                for (int i = 0; i < dst.Length; i++)
                {
                    byte r = region[i];
                    if (r > dst[i]) dst[i] = r;
                }
                break;
            case SelectionCombineMode.Subtract:
                for (int i = 0; i < dst.Length; i++)
                {
                    int r = region[i];
                    if (r == 0) continue;
                    dst[i] = (byte)(dst[i] * (255 - r) / 255);
                }
                break;
            case SelectionCombineMode.Intersect:
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = (byte)(dst[i] * region[i] / 255);
                break;
        }
        RaiseChanged();
    }

    // ========================
    // OPERASI MORFOLOGI / BLUR
    // ========================

    /// <summary>Tumbuhkan (dilate) seleksi sejauh <paramref name="radius"/> piksel.</summary>
    public void Grow(int radius)
    {
        if (radius <= 0) return;
        MaskOperations.ShiftEdge(Coverage, Width, Height, radius, expand: true);
        RaiseChanged();
    }

    /// <summary>Kerutkan (erode) seleksi sejauh <paramref name="radius"/> piksel.</summary>
    public void Shrink(int radius)
    {
        if (radius <= 0) return;
        MaskOperations.ShiftEdge(Coverage, Width, Height, radius, expand: false);
        RaiseChanged();
    }

    /// <summary>Feather (blur) tepi seleksi sejauh <paramref name="radius"/> piksel.</summary>
    public void Feather(int radius)
    {
        if (radius <= 0) return;
        MaskOperations.Feather(Coverage, Width, Height, radius);
        RaiseChanged();
    }

    // ========================
    // OPERASI GLOBAL
    // ========================

    public void Invert()
    {
        var c = Coverage;
        for (int i = 0; i < c.Length; i++) c[i] = (byte)(255 - c[i]);
        RaiseChanged();
    }

    public void Clear()
    {
        Array.Clear(Coverage, 0, Coverage.Length);
        RaiseChanged();
    }

    public void SelectAll()
    {
        MaskOperations.Fill(Coverage, 255);
        RaiseChanged();
    }

    /// <summary>Geser seluruh seleksi (dx,dy) piksel; area di luar kanvas dipotong.</summary>
    public void Transform(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var src = (byte[])Coverage.Clone();
        Array.Clear(Coverage, 0, Coverage.Length);
        for (int y = 0; y < Height; y++)
        {
            int sy = y - dy;
            if (sy < 0 || sy >= Height) continue;
            int dstRow = y * Width;
            int srcRow = sy * Width;
            for (int x = 0; x < Width; x++)
            {
                int sx = x - dx;
                if (sx < 0 || sx >= Width) continue;
                Coverage[dstRow + x] = src[srcRow + sx];
            }
        }
        RaiseChanged();
    }

    // ========================
    // BOUNDS / COPY
    // ========================

    /// <summary>Bounding box piksel dengan coverage &gt; 0. False bila seleksi kosong.</summary>
    public bool GetBounds(out int minX, out int minY, out int maxX, out int maxY)
        => MaskOperations.GetAlphaBounds(Coverage, Width, Height, out minX, out minY, out maxX, out maxY);

    /// <summary>Jumlah piksel dengan coverage &gt; 0 (untuk info status).</summary>
    public long CountSelected()
    {
        RecomputeCache();
        return _countSelected;
    }

    /// <summary>Salin isi coverage dari buffer lain (panjang harus sama).</summary>
    public void CopyFrom(byte[] coverage)
    {
        if (coverage == null || coverage.Length != Coverage.Length) return;
        Buffer.BlockCopy(coverage, 0, Coverage, 0, Coverage.Length);
        RaiseChanged();
    }

    /// <summary>Salin isi coverage dari SelectionState lain (ukuran harus sama).</summary>
    public void CopyFrom(SelectionState other)
    {
        if (other == null || other.Width != Width || other.Height != Height) return;
        Buffer.BlockCopy(other.Coverage, 0, Coverage, 0, Coverage.Length);
        RaiseChanged();
    }

    /// <summary>Salin isi coverage ke buffer lain (panjang harus sama).</summary>
    public void CopyTo(byte[] dest)
    {
        if (dest == null || dest.Length != Coverage.Length) return;
        Buffer.BlockCopy(Coverage, 0, dest, 0, Coverage.Length);
    }

    /// <summary>Paksa pemicuan event Changed (mis. setelah modifikasi langsung).</summary>
    public void NotifyChanged() => RaiseChanged();
}

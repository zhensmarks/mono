using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Titik 2D sederhana (double) untuk geometri rasterisasi.
/// </summary>
public struct Vec2
{
    public double X;
    public double Y;
    public Vec2(double x, double y) { X = x; Y = y; }
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
}

/// <summary>
/// Region seleksi berbasis jalur tertutup (polyline of Vec2).
/// Rasterizer mengisi ke mask memakai winding rule (non-zero) atau even-odd.
/// </summary>
public sealed class MaskRegion
{
    /// <summary>
    /// Titik pada subpath yang sedang dibangun. Untuk region satu-jalur ini
    /// adalah seluruh jalur; untuk multi-subpath, ini subpath terakhir.
    /// </summary>
    public List<Vec2> Points { get; private set; } = new();

    /// <summary>Subpath-subpath yang sudah final (dipakai rasterizer).</summary>
    private readonly List<List<Vec2>> Subpaths = new();
    private List<Vec2> Current = new();

    public bool EvenOdd { get; set; }

    public MaskRegion() { }
    public MaskRegion(IEnumerable<Vec2> pts, bool evenOdd = false)
    {
        Points.AddRange(pts);
        Current = Points;
        EvenOdd = evenOdd;
    }

    /// <summary>True bila tidak ada titik untuk diisi.</summary>
    public bool IsEmpty
    {
        get
        {
            EndSubpath();
            return Subpaths.Count == 0 || AllSubpathsDegenerate();
        }
    }

    private bool AllSubpathsDegenerate()
    {
        // Sebuah subpath valid bila >= 3 titik.
        if (Subpaths.Count == 0) return true;
        foreach (var s in Subpaths) if (s.Count >= 3) return false;
        return true;
    }

    public void Add(Vec2 p) => Current.Add(p);
    public void Clear() { Subpaths.Clear(); Current = new List<Vec2>(); Points = Current; }

    /// <summary>
    /// Mulai subpath baru. Titik-titik berikutnya masuk ke subpath ini.
    /// Subpath terpisah membentuk lubang (hole) pada winding rule non-zero
    /// bila arahnya berlawanan, atau pada even-odd apa pun arahnya.
    /// </summary>
    public void BeginSubpath()
    {
        if (Current.Count > 0) Subpaths.Add(Current);
        Current = new List<Vec2>();
    }

    /// <summary>Tambahkan subpath tertutup sekaligus.</summary>
    public void AddSubpath(IEnumerable<Vec2> pts)
    {
        BeginSubpath();
        foreach (var p in pts) Current.Add(p);
    }

    /// <summary>Finalisasi subpath yang sedang dibangun (dipanggil otomatis oleh rasterizer).</summary>
    public void EndSubpath()
    {
        if (Current.Count > 0) { Subpaths.Add(Current); Current = new List<Vec2>(); }
    }

    /// <summary>
    /// Ambil daftar subpath final. Bila pemanggil hanya memakai <see cref="Add"/>
    /// tanpa <see cref="BeginSubpath"/>, seluruh titik jadi satu subpath
    /// (kompatibel dengan perilaku lama).
    /// </summary>
    public List<List<Vec2>> GetSubpaths()
    {
        EndSubpath();
        return Subpaths;
    }

    /// <summary>Bounding box seluruh subpath (min/max).</summary>
    public void GetBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = double.MaxValue; minY = double.MaxValue;
        maxX = double.MinValue; maxY = double.MinValue;
        foreach (var sub in GetSubpaths())
        {
            foreach (var p in sub)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
    }

    /// <summary>Translate all closed subpaths in place, preserving holes and winding.</summary>
    public void Translate(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
        foreach (var subpath in GetSubpaths())
        {
            for (int i = 0; i < subpath.Count; i++)
            {
                var point = subpath[i];
                point.X += dx;
                point.Y += dy;
                subpath[i] = point;
            }
        }
    }
}

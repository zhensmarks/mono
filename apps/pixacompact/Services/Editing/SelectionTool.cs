using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>Pointer-release policy for multi-step selection tools.</summary>
public static class SelectionInteractionPolicy
{
    /// <summary>Tools whose pointer press begins a geometric selection gesture.</summary>
    public static bool HandlesPointerPress(EditToolKind tool)
        => tool is EditToolKind.Lasso or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee
            or EditToolKind.PolyLasso or EditToolKind.Pen;

    /// <summary>Freehand lasso and marquee shapes commit on release; paths require explicit close.</summary>
    public static bool CommitsOnPointerRelease(EditToolKind tool)
        => tool is EditToolKind.Lasso or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee;
}

/// <summary>
/// Base untuk tool seleksi geometris (Lasso / Polygonal Lasso / Pen).
/// Semua koordinat di sini adalah <b>koordinat gambar</b> (piksel), bukan layar.
/// Konversi layar↔gambar dilakukan oleh <c>EditorViewPort</c> di code-behind.
/// </summary>
public abstract class SelectionTool
{
    /// <summary>Jalur sementara yang sedang dibangun (belum commit).</summary>
    protected readonly List<Vec2> Path = new();

    /// <summary>Selama drag, region "hidup" untuk pratinjau marquee.</summary>
    public bool IsActive { get; protected set; }

    /// <summary>True bila ada jalur yang bisa di-commit. Default: >= 3 titik.</summary>
    public bool CanCommit => CanCommitCore;

    /// <summary>Override pada tool yang menyimpan jalurnya sendiri (mis. Pen).</summary>
    protected virtual bool CanCommitCore => Path.Count >= 3;

    /// <summary>Titik jalur saat ini (read-only view untuk overlay).</summary>
    public IReadOnlyList<Vec2> CurrentPath => Path;

    public event Action? Changed;

    protected void RaiseChanged() => Changed?.Invoke();

    /// <summary>
    /// Tekan tombol pointer (koordinat gambar).
    /// <paramref name="breakHandle"/> = Alt ditekan (patahkan handle Bézier, hanya relevan untuk Pen).
    /// </summary>
    public virtual void PointerDown(Vec2 p, bool breakHandle = false) => PointerDown(p);

    /// <summary>Tekan tombol pointer (kompatibilitas tool lama).</summary>
    /// <summary>Tekan tombol pointer (kompatibilitas tool lama).</summary>
    public virtual void PointerDown(Vec2 p) { }
    /// <summary>Geser pointer. <paramml name="pressed"/> = tombol masih ditahan.</summary>
    public abstract void PointerMove(Vec2 p, bool pressed);

    /// <summary>Lepas tombol pointer.</summary>
    public abstract void PointerUp(Vec2 p);

    /// <summary>Double-click / aksi penutup (mis. pen tool menutup jalur).</summary>
    public virtual void ClosePath() { }

    /// <summary>Aksi Enter: commit jalur jadi region.</summary>
    public virtual MaskRegion? Commit() => BuildRegion();

    /// <summary>Esc: batalkan semua.</summary>
    public virtual void Cancel()
    {
        Path.Clear();
        IsActive = false;
        RaiseChanged();
    }

    /// <summary>Hapus titik terakhir (Backspace). Mengembalikan true bila ada yang dihapus.</summary>
    public virtual bool RemoveLastPoint()
    {
        if (Path.Count == 0) return false;
        Path.RemoveAt(Path.Count - 1);
        RaiseChanged();
        return true;
    }

    /// <summary>Bangun region tertutup dari <see cref="Path"/>.</summary>
    protected MaskRegion? BuildRegion()
    {
        if (Path.Count < 3) return null;
        var region = new MaskRegion(Path, EvenOdd);
        return region;
    }

    /// <summary>Winding rule untuk hasil rasterisasi.</summary>
    public bool EvenOdd { get; set; }
}

/// <summary>
/// Lasso bebas: tiap pointer-move menambah titik ke jalur; saat tombol
/// dilepas jalur otomatis ditutup (titik pertama terhubung terakhir).
/// </summary>
public sealed class LassoSelectionTool : SelectionTool
{
    /// <summary>Jarak minimum antar titik agar jalur tidak terlalu padat (px gambar).</summary>
    public double MinSpacing { get; set; } = 1.0;

    private Vec2 _last;

    public override void PointerDown(Vec2 p)
    {
        Path.Clear();
        Path.Add(p);
        _last = p;
        IsActive = true;
        RaiseChanged();
    }

    public override void PointerMove(Vec2 p, bool pressed)
    {
        if (!IsActive || !pressed) return;
        double dx = p.X - _last.X, dy = p.Y - _last.Y;
        if (dx * dx + dy * dy < MinSpacing * MinSpacing) return;
        Path.Add(p);
        _last = p;
        RaiseChanged();
    }

    public override void PointerUp(Vec2 p)
    {
        if (!IsActive) return;
        if (Path.Count > 0 && (Path[Path.Count - 1].X != p.X || Path[Path.Count - 1].Y != p.Y))
            Path.Add(p);
        IsActive = false;
        // Jalur lasso selalu implisit tertutup.
        if (Path.Count < 3) 
        {
            Path.Clear();
        }
        RaiseChanged();
    }
}

/// <summary>
/// Polygonal lasso: klik menambah vertex, klik dekat titik pertama atau
/// double-click / Enter menutup polygon.
/// </summary>
public sealed class PolygonalLassoSelectionTool : SelectionTool
{
    /// <summary>Radius (px gambar) untuk mendeteksi klik pada titik pertama.</summary>
    public double CloseHitRadius { get; set; } = 8.0;

    private Vec2 _cursor;

    public override void PointerDown(Vec2 p)
    {
        if (Path.Count >= 3)
        {
            // Klik dekat titik pertama → tutup polygon.
            double dx = p.X - Path[0].X, dy = p.Y - Path[0].Y;
            if (dx * dx + dy * dy <= CloseHitRadius * CloseHitRadius)
            {
                IsActive = false;
                RaiseChanged();
                return;
            }
        }
        Path.Add(p);
        _cursor = p;
        IsActive = true;
        RaiseChanged();
    }

    public override void PointerMove(Vec2 p, bool pressed)
    {
        _cursor = p;
        if (IsActive) RaiseChanged();
    }

    public override void PointerUp(Vec2 p) { }

    public override void ClosePath()
    {
        if (Path.Count >= 3) IsActive = false;
        RaiseChanged();
    }

    /// <summary>Titik kursor terakhir (untuk menggambar garis karet ke kursor).</summary>
    public Vec2 Cursor => _cursor;
}

/// <summary>
/// Pen tool: anchor + dua handle Bézier per segmen. Segment i dibentuk oleh
/// anchor i, handle-out i, handle-in i+1, anchor i+1.
/// </summary>
public sealed class PenTool : SelectionTool
{
    /// <summary>Toleransi flattening Bézier (px gambar).</summary>
    public double FlattenTolerance { get; set; } = 0.35;

    /// <summary>Radius (px gambar) untuk mendeteksi klik pada anchor pertama → menutup jalur.</summary>
    public double CloseHitRadius { get; set; } = 8.0;

    public sealed class Anchor
    {
        public Vec2 Point;
        public Vec2 HandleIn;   // relatif terhadap Point
        public Vec2 HandleOut;  // relatif terhadap Point

        public Anchor(Vec2 p) { Point = p; }

        /// <summary>True bila anchor ini punya handle (smooth), bukan corner tajam.</summary>
        public bool IsSmooth =>
            HandleIn.X * HandleIn.X + HandleIn.Y * HandleIn.Y > 0.25 ||
            HandleOut.X * HandleOut.X + HandleOut.Y * HandleOut.Y > 0.25;
    }

    private readonly List<Anchor> _anchors = new();
    private Vec2 _cursor;
    private int _dragIndex = -1;
    private bool _breakHandle;

    public IReadOnlyList<Anchor> Anchors => _anchors;
    public Vec2 Cursor => _cursor;
    public bool IsClosed { get; private set; }

    /// <summary>
    /// True bila anchor terakhir punya handle dan jalur belum tertutup — dipakai overlay
    /// untuk menggambar rubber band.
    /// </summary>
    public bool IsDrawing => IsActive && _anchors.Count > 0 && !IsClosed;

    /// <summary>
    /// Klik: tambah anchor baru; klik dekat anchor pertama (≥3 anchor) menutup jalur.
    /// Bila <paramref name="breakHandle"/> = true (Alt), handle tidak dibuat simetris
    /// sehingga memungkinkan sudut tajam setelah kurva (perilaku Photoshop).
    /// </summary>
    public override void PointerDown(Vec2 p, bool breakHandle = false)
    {
        _breakHandle = breakHandle;

        if (_anchors.Count >= 3)
        {
            double dx = p.X - _anchors[0].Point.X, dy = p.Y - _anchors[0].Point.Y;
            if (dx * dx + dy * dy <= CloseHitRadius * CloseHitRadius)
            {
                ClosePath();
                return;
            }
        }

        _anchors.Add(new Anchor(p));
        _dragIndex = _anchors.Count - 1;
        IsActive = true;
        RefreshPreviewPath();
        RaiseChanged();
    }

    /// <summary>Bentuk 1-argumen (kompatibilitas) — setara klik tanpa Alt.</summary>
    public override void PointerDown(Vec2 p) => PointerDown(p, breakHandle: false);

    /// <summary>Drag saat tombol ditahan: tarik handle-out dari anchor yang baru dibuat.</summary>
    public override void PointerMove(Vec2 p, bool pressed)
    {
        _cursor = p;

        if (pressed && _dragIndex >= 0)
        {
            var a = _anchors[_dragIndex];
            a.HandleOut = new Vec2(p.X - a.Point.X, p.Y - a.Point.Y);

            // Normalnya handle-in simetris → kurva mulus. Bila Alt ditekan saat drag,
            // biarkan handle-in apa adanya (sudut tajam / patah).
            if (!_breakHandle)
                a.HandleIn = new Vec2(-a.HandleOut.X, -a.HandleOut.Y);

            RefreshPreviewPath();
        }

        if (IsActive) RaiseChanged();
    }

    public override void PointerUp(Vec2 p)
    {
        _dragIndex = -1;
        _breakHandle = false;
    }

    /// <summary>Pindahkan anchor (dan handlenya) — dipakai direct-selection.</summary>
    public bool MoveAnchor(int index, Vec2 newPoint)
    {
        if (index < 0 || index >= _anchors.Count) return false;
        _anchors[index].Point = newPoint;
        RefreshPreviewPath();
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Hit-test knob handle Bézier (titik absolut = Point + HandleIn/Out), ala
    /// Photoshop direct-selection. Mengembalikan anchor dan sisi handle yang kena.
    /// </summary>
    public (bool hit, int anchorIndex, bool isIn) HitTestHandle(Vec2 p, double radius)
    {
        bool found = false;
        int bestAnchor = -1;
        bool bestIn = false;
        double bestD = radius * radius;
        for (int i = 0; i < _anchors.Count; i++)
        {
            var a = _anchors[i];
            if (IsHandleVec(a.HandleIn))
            {
                double d = Dist2(p, a.Point + a.HandleIn);
                if (d <= bestD) { bestD = d; bestAnchor = i; bestIn = true; found = true; }
            }
            if (IsHandleVec(a.HandleOut))
            {
                double d = Dist2(p, a.Point + a.HandleOut);
                if (d <= bestD) { bestD = d; bestAnchor = i; bestIn = false; found = true; }
            }
        }
        return (found, bestAnchor, bestIn);
    }

    /// <summary>
    /// Atur satu handle secara absolut (relatif terhadap anchor).
    /// <paramref name="mirror"/> = true menjaga handle berlawanan tetap simetris
    /// (smooth); false (tahan Alt) mematahkan simetri ala Photoshop.
    /// </summary>
    public bool SetHandle(int index, bool isIn, Vec2 relative, bool mirror)
    {
        if (index < 0 || index >= _anchors.Count) return false;
        var a = _anchors[index];
        if (isIn) a.HandleIn = relative;
        else a.HandleOut = relative;
        if (mirror)
        {
            var m = new Vec2(-relative.X, -relative.Y);
            if (isIn) a.HandleOut = m;
            else a.HandleIn = m;
        }
        RefreshPreviewPath();
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Ubah anchor menjadi corner tajam (hapus kedua handle) — setara Alt+klik
    /// Convert Point Tool di Photoshop.
    /// </summary>
    public bool ConvertToCorner(int index)
    {
        if (index < 0 || index >= _anchors.Count) return false;
        var a = _anchors[index];
        a.HandleIn = default;
        a.HandleOut = default;
        RefreshPreviewPath();
        RaiseChanged();
        return true;
    }

    private static bool IsHandleVec(Vec2 v) => v.X * v.X + v.Y * v.Y > 0.25;

    private static double Dist2(Vec2 a, Vec2 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Index anchor terdekat dalam radius (px gambar), atau -1.</summary>
    public int HitTestAnchor(Vec2 p, double radius)
    {
        int best = -1;
        double bestD = radius * radius;
        for (int i = 0; i < _anchors.Count; i++)
        {
            double dx = p.X - _anchors[i].Point.X, dy = p.Y - _anchors[i].Point.Y;
            double d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Tutup jalur (Enter / klik titik awal / double-click).</summary>
    public override void ClosePath()
    {
        if (_anchors.Count >= 3)
        {
            IsClosed = true;
            IsActive = false;
            RefreshPreviewPath();
            RaiseChanged();
        }
    }

    /// <summary>Enter: tutup jalur dan bangun region dari hasil flatten.</summary>
    public override MaskRegion? Commit()
    {
        if (_anchors.Count >= 3) IsClosed = true;
        IsActive = false;
        return BuildPenRegion();
    }

    protected override bool CanCommitCore => _anchors.Count >= 3;

    /// <summary>
    /// Bangun ulang <see cref="Path"/> (yang dipakai overlay) dari anchor + handle.
    /// Ini WAJIB: tanpa ini <c>CurrentPath</c> selalu kosong sehingga outline tidak
    /// pernah tergambar (bug lama).
    /// </summary>
    private void RefreshPreviewPath()
    {
        Path.Clear();
        if (_anchors.Count == 0) return;

        Path.Add(_anchors[0].Point);

        // Saat belum tertutup, hanya segmen antar-anchor yang ada (bukan segmen penutup).
        int segments = IsClosed ? _anchors.Count : _anchors.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = _anchors[i];
            var b = _anchors[(i + 1) % _anchors.Count];
            var c1 = a.Point + a.HandleOut;
            var c2 = b.Point + b.HandleIn;
            MaskRasterizer.FlattenCubic(a.Point, c1, c2, b.Point, FlattenTolerance, Path);
        }
    }

    /// <summary>Bangun region dengan mem-flatten semua segmen Bézier.</summary>
    private MaskRegion? BuildPenRegion()
    {
        if (_anchors.Count < 3) return null;
        var pts = new List<Vec2>();
        pts.Add(_anchors[0].Point);

        int segments = IsClosed ? _anchors.Count : _anchors.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = _anchors[i];
            var b = _anchors[(i + 1) % _anchors.Count];
            var c1 = a.Point + a.HandleOut;
            var c2 = b.Point + b.HandleIn;
            MaskRasterizer.FlattenCubic(a.Point, c1, c2, b.Point, FlattenTolerance, pts);
        }
        return new MaskRegion(pts, EvenOdd);
    }

    public override void Cancel()
    {
        _anchors.Clear();
        _dragIndex = -1;
        _breakHandle = false;
        IsClosed = false;
        base.Cancel();
    }

    public override bool RemoveLastPoint()
    {
        if (_anchors.Count == 0) return false;
        if (IsClosed) IsClosed = false;
        _anchors.RemoveAt(_anchors.Count - 1);
        RefreshPreviewPath();
        RaiseChanged();
        return true;
    }

    /// <summary>Hapus semua anchor (reset).</summary>
    public void Reset() => Cancel();
}
 
/// <summary>
/// Marquee seleksi Rect/Ellipse (Photoshop M / Shift+M).
/// Klik-drag membentuk region; Shift = bujur sangkar/lingkaran, Alt = dari tengah.
/// </summary>
public sealed class MarqueeSelectionTool : SelectionTool
{
    public bool Ellipse { get; set; }
    public bool Constrain { get; set; }
    public bool FromCenter { get; set; }
    private Vec2 _start;
    private Vec2 _end;

    public override void PointerDown(Vec2 p)
    {
        Path.Clear();
        _start = p; _end = p;
        IsActive = true;
        Rebuild();
        RaiseChanged();
    }

    public override void PointerMove(Vec2 p, bool pressed)
    {
        if (!IsActive) return;
        _end = p;
        Rebuild();
        RaiseChanged();
    }

    public override void PointerUp(Vec2 p)
    {
        if (!IsActive) return;
        _end = p;
        Rebuild();
        IsActive = false;
        RaiseChanged();
    }

    private void Rebuild()
    {
        Path.Clear();
        double x0 = _start.X, y0 = _start.Y, x1 = _end.X, y1 = _end.Y;
        if (Constrain)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
            x1 = x0 + (dx < 0 ? -m : m);
            y1 = y0 + (dy < 0 ? -m : m);
        }
        if (FromCenter)
        {
            double dx = x1 - x0, dy = y1 - y0;
            BuildShape(x0 - Math.Abs(dx), y0 - Math.Abs(dy), x0 + Math.Abs(dx), y0 + Math.Abs(dy));
        }
        else
        {
            BuildShape(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        }
    }

    private void BuildShape(double left, double top, double right, double bottom)
    {
        if (Ellipse)
        {
            double cx = (left + right) / 2.0, cy = (top + bottom) / 2.0;
            double rx = (right - left) / 2.0, ry = (bottom - top) / 2.0;
            const int N = 64;
            for (int i = 0; i < N; i++)
            {
                double a = 2.0 * Math.PI * i / N;
                Path.Add(new Vec2(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }
        }
        else
        {
            Path.Add(new Vec2(left, top));
            Path.Add(new Vec2(right, top));
            Path.Add(new Vec2(right, bottom));
            Path.Add(new Vec2(left, bottom));
        }
    }
}

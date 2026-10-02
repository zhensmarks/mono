using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using PixelcutCompact.Models;
using PixelcutCompact.Services;
using IOPath = System.IO.Path;
using PixelcutCompact.Services.Ai;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

/// <summary>Render ulang kanvas editor (dipisah dari PreviewWindow.Editor.cs agar file < batas push).</summary>
public partial class PreviewWindow
{
    // ========================
    // GAMBAR ULANG KANVAS
    // ========================


    /// <summary>Perbarui hanya area mask yang berubah pada bitmap persisten.</summary>
    private void RefreshResultBitmap()
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;

        try
        {
            EnsureResultBuffers();
            if (_resultBuf == null || _resultWb == null) return;

            var bounds = (_resultDirtyBounds ?? PixelBounds.Full(_session.Width, _session.Height))
                .ClampTo(_session.Width, _session.Height);
            _resultDirtyBounds = null;
            if (bounds.IsEmpty) return;

            if (_maskView)
                RenderMaskGrayscaleInto(_resultBuf, bounds);
            else
                _session.CompositeInto(_resultBuf, bounds);
            _resultBuf.WriteToUnpremul(_resultWb, bounds);
            if (!ReferenceEquals(img.Source, _resultWb)) img.Source = _resultWb;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Refresh komposit gagal: " + ex.Message);
        }
    }

    /// <summary>
    /// Gambar mask sebagai grayscale ke buffer pratinjau (untuk Mask view):
    /// hitam (0) = tersembunyi, putih (255) = tampil. Alpha selalu penuh agar
    /// mask terlihat solid seperti Alt+klik thumbnail mask di Photoshop.
    /// </summary>
    private void RenderMaskGrayscaleInto(PixelBuffer buf, PixelBounds bounds)
    {
        if (_session == null) return;
        var mask = _session.Mask;
        var dest = buf.Bgra;
        int w = _session.Width;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            int pixel = y * w + bounds.X;
            int end = pixel + bounds.Width;
            for (; pixel < end; pixel++)
            {
                byte v = pixel < mask.Length ? mask[pixel] : (byte)0;
                int bi = pixel * 4;
                dest[bi] = v;
                dest[bi + 1] = v;
                dest[bi + 2] = v;
                dest[bi + 3] = 255;
            }
        }
    }

    /// <summary>Siapkan ulang buffer pratinjau bila ukuran gambar berubah.</summary>
    private void EnsureResultBuffers()
    {
        if (_session == null) return;
        int w = _session.Width, h = _session.Height;
        if (_resultBuf != null && _previewW == w && _previewH == h) return;

        _resultBuf = new PixelBuffer(w, h);
        var pf = PixelFormat.Bgra8888;
        var sz = new Avalonia.PixelSize(w, h);
        var dpi = new Avalonia.Vector(96, 96);
        _resultWb?.Dispose();
        _resultWb = new WriteableBitmap(sz, dpi, pf, AlphaFormat.Unpremul);
        _previewW = w; _previewH = h;
        _resultDirtyBounds = PixelBounds.Full(w, h);
    }

    /// <summary>Coalesce refresh komposit (~30 FPS) saat stroke; force=true
    /// memaksa refresh final segera (dipakai saat stroke selesai).</summary>
    private void ScheduleComposite(bool force, bool updateResult = true, bool updateQuickMask = false)
    {
        _pendingResultRefresh |= updateResult;
        _pendingQuickMaskRefresh |= updateQuickMask;
        long now = Environment.TickCount64;
        if (force || (now - _lastRenderMs >= 33 && !_renderPending))
        {
            _lastRenderMs = now;
            _renderPending = false;
            FlushScheduledComposite();
            return;
        }
        if (_renderPending) return;
        _renderPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _renderPending = false;
            _lastRenderMs = Environment.TickCount64;
            FlushScheduledComposite();
        }, TimeSpan.FromMilliseconds(34));
    }

    private void FlushScheduledComposite()
    {
        if (_pendingResultRefresh)
        {
            _pendingResultRefresh = false;
            RefreshResultBitmap();
        }
        if (_pendingQuickMaskRefresh)
        {
            _pendingQuickMaskRefresh = false;
            RenderQuickMask(_quickMaskDirtyBounds);
        }
    }

    private void MarkResultDirty(PixelBounds bounds)
    {
        if (_session == null || bounds.IsEmpty) return;
        _resultDirtyBounds = _resultDirtyBounds.HasValue
            ? _resultDirtyBounds.Value.Union(bounds).ClampTo(_session.Width, _session.Height)
            : bounds.ClampTo(_session.Width, _session.Height);
    }

    private void MarkQuickMaskDirty(PixelBounds bounds)
    {
        if (_session == null || bounds.IsEmpty) return;
        _quickMaskDirtyBounds = _quickMaskDirtyBounds.HasValue
            ? _quickMaskDirtyBounds.Value.Union(bounds).ClampTo(_session.Width, _session.Height)
            : bounds.ClampTo(_session.Width, _session.Height);
    }

    /// <summary>
    /// Hapus elemen overlay dinamis, tetapi PERTAHANKAN path marching ants
    /// (Tag="ants") yang dideklarasikan di XAML agar tidak ikut terhapus.
    /// </summary>
    private static void ClearOverlayDynamic(Canvas overlay)
    {
        for (int i = overlay.Children.Count - 1; i >= 0; i--)
        {
            if (overlay.Children[i] is Control { Tag: "ants" }) continue;
            overlay.Children.RemoveAt(i);
        }
    }

    private void RenderOverlay()
    {
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay == null || !_editMode) return;
        ClearOverlayDynamic(overlay);
        SyncViewPort();
        RenderAnts();
        var tool = _activeSelectionTool; 
        if (tool is PolygonalLassoSelectionTool poly) 
        { 
            if (poly.CurrentPath.Count >= 1) 
                DrawPathOutline(overlay, poly.CurrentPath, closed: false); 
            if (poly.IsActive && poly.CurrentPath.Count >= 1) 
            { 
                var lastP = ImageToOverlay(poly.CurrentPath[poly.CurrentPath.Count - 1]); 
                var curP = ImageToOverlay(poly.Cursor); 
                DrawLine(overlay, lastP, curP, "#80FFE24A", dash: true); 
            } 
            if (poly.IsActive && poly.CurrentPath.Count >= 3) 
            { 
                var firstP = ImageToOverlay(poly.CurrentPath[0]); 
                var refP = ImageToOverlay(new Vec2(poly.CurrentPath[0].X + poly.CloseHitRadius, poly.CurrentPath[0].Y)); 
                double rad = Math.Max(6.0, Math.Sqrt((refP.X - firstP.X) * (refP.X - firstP.X) + (refP.Y - firstP.Y) * (refP.Y - firstP.Y))); 
                var curP2 = ImageToOverlay(poly.Cursor); 
                double ddx = curP2.X - firstP.X, ddy = curP2.Y - firstP.Y; 
                bool nearClose = Math.Sqrt(ddx * ddx + ddy * ddy) <= rad; 
                var circle = new Ellipse 
                { 
                    Width = rad * 2, Height = rad * 2, 
                    Stroke = Brushes.White, StrokeThickness = 1.5, 
                    Fill = nearClose ? new SolidColorBrush(Color.Parse("#80FFE24A")) : null 
                }; 
                Canvas.SetLeft(circle, firstP.X - rad); 
                Canvas.SetTop(circle, firstP.Y - rad); 
                overlay.Children.Add(circle); 
            } 
        } 
        else if (tool != null && tool.CurrentPath.Count >= 2 && tool is not PenTool) 
        { 
            DrawPathOutline(overlay, tool.CurrentPath, closed: tool.CanCommit); 
            if (tool.IsActive) 
            { 
                var last = ImageToOverlay(tool.CurrentPath[tool.CurrentPath.Count - 1]); 
                var first = ImageToOverlay(tool.CurrentPath[0]); 
                DrawLine(overlay, last, first, "#80FFE24A", dash: true); 
            } 
        } 
        if (tool is PenTool pen)
        {
            if (pen.CurrentPath.Count >= 2)
                DrawPathOutline(overlay, pen.CurrentPath, pen.IsClosed, penStyle: true);
            DrawPenOverlay(overlay, pen);
        }
    }

    /// <summary>




    private void DrawPathOutline(Canvas overlay, IReadOnlyList<Vec2> pts, bool closed, bool penStyle = false)
    {
        if (pts.Count < 2) return;
        double thickness = penStyle ? PenPathThickness() : 1.5;
        IBrush stroke = penStyle
            ? new SolidColorBrush(PenPathColor())
            : new SolidColorBrush(Color.Parse("#FFE24A"));

        if (penStyle)
        {
            // Underlay gelap tipis: garis path selalu terbaca di atas kanvas terang maupun gelap.
            AddPathPolyline(overlay, pts, closed, Brushes.Black, thickness + 2.0, 0.7);
        }
        AddPathPolyline(overlay, pts, closed, stroke, thickness, 1.0);

        if (closed && pts.Count >= 3)
        {
            var s0 = ImageToOverlay(pts[0]);
            var sl = ImageToOverlay(pts[pts.Count - 1]);
            if (penStyle)
            {
                DrawLine(overlay, sl, s0, "#000000", width: thickness + 2.0);
                DrawLine(overlay, sl, s0, PenPathStyle.NormalizeColor(_settings.EditorPenPathColor), width: thickness);
            }
            else DrawLine(overlay, sl, s0, "#FFE24A");
        }
    }

    private void AddPathPolyline(Canvas overlay, IReadOnlyList<Vec2> pts, bool closed, IBrush stroke, double thickness, double opacity)
    {
        var poly = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            Opacity = opacity,
        };
        if (!closed) poly.StrokeDashArray = new AvaloniaList<double> { 4, 3 };

        foreach (var p in pts)
        {
            poly.Points.Add(ImageToOverlay(p));
        }
        overlay.Children.Add(poly);
    }

    private void DrawLine(Canvas overlay, Point a, Point b, string color, bool dash = false, double width = 1)
    {
        var line = new Line
        {
            StartPoint = a,
            EndPoint = b,
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = width
        };
        if (dash) line.StrokeDashArray = new AvaloniaList<double> { 4, 3 };
        overlay.Children.Add(line);
    }

    /// <summary>
    /// Pusat canvas overlay dalam koordinat lokal canvas.
    /// <see cref="EditorViewPort.ImageToScreen"/> memakai origin di TENGAH gambar/viewport,
    /// sedangkan Canvas menggambar dari kiri-atas, sehingga perlu penambahan setengah ukuran.
    /// </summary>
    private Point OverlayCenter()
    {
        var canvas = this.FindControl<Canvas>("EditOverlay");
        double w = canvas?.Bounds.Width ?? 0;
        double h = canvas?.Bounds.Height ?? 0;

        // Fallback: bila bounds belum terukur (0), pakai ukuran ImageResult.
        if (w <= 0 || h <= 0)
        {
            var img = this.FindControl<Image>("ImgResult");
            w = img?.Bounds.Width ?? 0;
            h = img?.Bounds.Height ?? 0;
        }
        return new Point(w / 2.0, h / 2.0);
    }

    /// <summary>True bila vektor handle cukup besar untuk digambar (bukan nol).</summary>
    private static bool HasHandle(Vec2 v) => v.X * v.X + v.Y * v.Y > 0.25;

    /// <summary>
    /// Konversi koordinat gambar (piksel, origin tengah) ke koordinat lokal Canvas overlay.
    /// Mencakup letterbox Stretch=Uniform DAN RenderTransform (zoom/pan/rotate).
    /// EditOverlay tidak ikut RenderTransform, jadi kita terapkan transform manual.
    /// </summary>
    private Point ImageToOverlay(Vec2 img)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null || _session == null) return new Point(img.X, img.Y);
        var b = image.Bounds;
        double fit = Math.Min(b.Width / Math.Max(1.0, _session.Width), b.Height / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        double x = (b.Width - _session.Width * fit) / 2.0 + (img.X + _session.Width / 2.0) * fit;
        double y = (b.Height - _session.Height * fit) / 2.0 + (img.Y + _session.Height / 2.0) * fit;
        return ApplyViewTransform(new Point(x, y));
    }
    /// <summary>
    /// Sama seperti ImageToOverlay, tapi koordinat Grid parent (untuk Path ants).
    /// </summary>
    private Point ImageToGrid(Vec2 img)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null || _session == null) return new Point(img.X, img.Y);
        var b = image.Bounds;
        double fit = Math.Min(b.Width / Math.Max(1.0, _session.Width), b.Height / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        double x = (b.Width - _session.Width * fit) / 2.0 + (img.X + _session.Width / 2.0) * fit;
        double y = (b.Height - _session.Height * fit) / 2.0 + (img.Y + _session.Height / 2.0) * fit;
        var o = ApplyViewTransform(new Point(x, y));
        return new Point(o.X + b.X, o.Y + b.Y);
    }
    /// <summary>
    /// Terapkan RenderTransform ImgResult (zoom/pan/rotate) ke titik lokal Image.
    /// RenderTransformOrigin 50 persen berarti translate(-O) * M * translate(O).
    /// </summary>
    private Point ApplyViewTransform(Point local)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null) return local;
        if (image.RenderTransform is not TransformGroup tg) return local;
        var m = tg.Value;
        var b = image.Bounds;
        double ox = b.Width / 2.0, oy = b.Height / 2.0;
        var v = new Point(local.X - ox, local.Y - oy);
        var t = m.Transform(v);
        return new Point(t.X + ox, t.Y + oy);
    }


    /// <summary>
    /// Buat checkerboard 16x16 bitmap dan set sebagai ImageBrush background BrdResult.
    /// Avalonia DrawingBrush tidak mendukung TileMode, jadi kita pakai WriteableBitmap.
    /// </summary>
    private void ApplyCheckerboardBackground()
    {
        var brd = this.FindControl<Border>("BrdResult");
        if (brd == null) return;
        try
        {
            const int sz = 16, half = 8;
            var bmp = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(sz, sz),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    uint* ptr = (uint*)fb.Address;
                    uint dark = 0xFF303030, light = 0xFF3D3D3D;
                    for (int y = 0; y < sz; y++)
                        for (int x = 0; x < sz; x++)
                        {
                            bool isLight = (x < half) ^ (y < half);
                            ptr[y * fb.RowBytes / 4 + x] = isLight ? light : dark;
                        }
                }
            }
            var brush = new Avalonia.Media.ImageBrush(bmp)
            {
                TileMode = Avalonia.Media.TileMode.Tile,
                Stretch = Avalonia.Media.Stretch.None,
                AlignmentX = Avalonia.Media.AlignmentX.Left,
                AlignmentY = Avalonia.Media.AlignmentY.Top,
            };
            brd.Background = brush;
        }
        catch (Exception ex) { Console.WriteLine($"Checkerboard: {ex.Message}"); }
    }

    private void SyncViewPort()
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;

        _viewPort.ImageWidth = _session.Width;
        _viewPort.ImageHeight = _session.Height;
        _viewPort.RotationDegrees = RotationResult;

        double zoom = 1, tx = 0, ty = 0;
        if (img.RenderTransform is TransformGroup tg)
        {
            foreach (var t in tg.Children)
            {
                if (t is ScaleTransform st) zoom = st.ScaleX;
                else if (t is TranslateTransform tt) { tx = tt.X; ty = tt.Y; }
            }
        }

        var bounds = img.Bounds;

        // ViewWidth dan ViewHeight adalah ukuran control Image (di mana bitmap ditampilkan)
        // EditorViewPort.ImageToScreen menganggap origin di pusat viewport
        _viewPort.Zoom = zoom;
        _viewPort.ViewWidth = bounds.Width;
        _viewPort.ViewHeight = bounds.Height;
        _viewPort.PanX = tx;
        _viewPort.PanY = ty;
    }

    private void ApplyEditorViewTransform()
    {
        foreach (var name in new[] { "ImgOriginal", "ImgResult", "ImgOriginalCompare", "ImgQuickMask" })
        {
            var image = this.FindControl<Image>(name);
            if (image?.RenderTransform is not TransformGroup group) continue;
            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale)
                {
                    scale.ScaleX = _viewPort.Zoom;
                    scale.ScaleY = _viewPort.Zoom;
                }
                else if (transform is TranslateTransform translate)
                {
                    translate.X = _viewPort.PanX;
                    translate.Y = _viewPort.PanY;
                }
            }
        }

        if (this.FindControl<NumericUpDown>("ZoomControl") is not { } zoomControl) return;
        var zoomValue = (decimal)_viewPort.Zoom;
        if (zoomControl.Value == zoomValue) return;
        _updatingZoomControl = true;
        try { zoomControl.Value = zoomValue; }
        finally { _updatingZoomControl = false; }
    }

    private bool HandleEditorWheelZoom(Image image, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!_editMode || image.Name != "ImgResult") return false;
        if (e.Delta.Y == 0) return true;

        SyncViewPort();
        var bounds = image.Bounds;
        Point cursor;
        if (image.Parent is Avalonia.Visual parent)
        {
            var pointerInViewport = e.GetPosition(parent);
            cursor = new Point(pointerInViewport.X - bounds.X, pointerInViewport.Y - bounds.Y);
        }
        else
        {
            cursor = e.GetPosition(image);
        }
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        if (_viewPort.ZoomAtViewportPoint(cursor, factor))
        {
            ApplyEditorViewTransform();
            OnViewTransformChanged();
        }
        return true;
    }

    private void ApplyEditorPan(Avalonia.Vector screenDelta)
    {
        if (!_editMode) return;
        SyncViewPort();
        _viewPort.PanByScreenDelta(screenDelta.X, screenDelta.Y);
        ApplyEditorViewTransform();
        OnViewTransformChanged();
    }

    /// <summary>Photoshop: Ctrl++ / Ctrl+- = zoom in/out dari tengah kanvas.</summary>
    private void EditorZoomStep(double factor)
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;
        SyncViewPort();
        var bounds = img.Bounds;
        var center = new Point(bounds.Width / 2.0, bounds.Height / 2.0);
        if (_viewPort.ZoomAtViewportPoint(center, factor))
        {
            ApplyEditorViewTransform();
            OnViewTransformChanged();
        }
    }

    /// <summary>Photoshop: Ctrl+0 = fit to screen.</summary>
    private void EditorZoomFit()
    {
        if (_session == null) return;
        SyncViewPort();
        _viewPort.FitToView();
        ApplyEditorViewTransform();
        OnViewTransformChanged();
    }

    /// tidak ikut transform: harus digambar ulang agar tetap presisi.
    /// </summary>
    private void OnViewTransformChanged()
    {
        if (!_editMode) return;
        UpdateZoomText();
        RenderOverlay();
    }

    /// <summary>
    /// Konversi posisi pointer (koordinat lokal Image/overlay, pra-transform)
    /// ke koordinat gambar (piksel, berbasis TENGAH gambar).
    ///
    /// Kebalikan eksak dari ImageToOverlay; keduanya memakai transform yang sama.
    /// <summary>
    /// Konversi posisi pointer (koordinat lokal Image, pre-RenderTransform)
    /// ke koordinat gambar (piksel, berbasis TENGAH gambar = origin 0,0 di tengah).
    ///
    /// Avalonia GetPosition(img) mengembalikan posisi pre-RenderTransform,
    /// sehingga zoom/pan visual TIDAK perlu diinverse di sini.
    /// Hanya kompensasi Stretch=Uniform letterboxing + Margin yang diperlukan.
    /// </summary>
    private Vec2 PointerToImage(Point posInImage)
    {
        if (_session == null) return new Vec2(0, 0);
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return new Vec2(0, 0);
        var b = img.Bounds;
        double w = b.Width, h = b.Height;
        // Fit scale: sama seperti Stretch=Uniform
        double fit = Math.Min(w / Math.Max(1.0, _session.Width), h / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        // Offset letterbox kiri-atas
        double left = (w - _session.Width * fit) / 2.0;
        double top  = (h - _session.Height * fit) / 2.0;
        // Posisi dalam gambar piksel (0..Width, 0..Height), lalu geser ke origin tengah
        double px = (posInImage.X - left) / fit;
        double py = (posInImage.Y - top) / fit;
        return new Vec2(px - _session.Width / 2.0, py - _session.Height / 2.0);
    }

     private bool EditorPointerPressed(Image img, PointerPressedEventArgs e)
     {

         if (!_editMode || _session == null) return false;
         if (img.Name != "ImgResult") return false;
        var pt = e.GetCurrentPoint(img);
        var pos = pt.Position;

        var imagePos = PointerToImage(pos);

        // Titik tengah dalam koordinat gambar (untuk brush).
        double halfW = _session.Width / 2.0;
        double halfH = _session.Height / 2.0;
        var centered = new Vec2(imagePos.X + halfW, imagePos.Y + halfH);
        bool left = pt.Properties.IsLeftButtonPressed;
        // Photoshop: Alt + klik kanan + geser = HUD ukuran/hardness brush.
        // Klik kanan biasa tetap tidak melakukan apa-apa, jadi tidak ada konflik.
        if (pt.Properties.IsRightButtonPressed
            && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !_strokeActive
            && _activeTool is EditToolKind.Brush or EditToolKind.Eraser)
        {
            BeginBrushHud(pos);
            e.Pointer.Capture(img);
            return true;
        }
        // Klik kanan tidak boleh mengubah mask/selection. Pan tetap ditangani viewer.
        if (!left) return false;

        if (SelectionInteractionPolicy.HandlesPointerPress(_activeTool))
        {
            if (_activeSelectionTool == null) return false;
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // Mode kombinasi (add/sub/…) hanya di-latch untuk tool yang commit saat
            // pointer dilepas (lasso/marquee). Pen & poly-lasso commit eksplisit via
            // Enter/double-click belakangan, jadi pakai modifier saat itu (jangan basi).
            _pendingSelMode = SelectionInteractionPolicy.CommitsOnPointerRelease(_activeTool)
                ? EffectiveMode(e.KeyModifiers)
                : null;
            if (_activeSelectionTool is PenTool penTool)
            {
                // Photoshop: double-click HANYA menaruh 2 anchor biasa, TIDAK menutup path.
                // Path ditutup dengan klik pada anchor pertama (lihat PenTool.AddAnchor).
                // Enter = akhiri path (tetap terbuka); Esc = batalkan path.
                // Drag knob handle Bézier diprioritaskan di atas tambah anchor —
                // ini cara utama mengedit kurva setelah anchor ditempatkan.
                var (handleHit, handleAnchor, handleIsIn) = penTool.HitTestHandle(imagePos, 10);
                if (handleHit)
                {
                    _penDragHandleIndex = handleAnchor;
                    _penDragHandleIsIn = handleIsIn;
                    e.Pointer.Capture(img);
                    RenderOverlay();
                    return true;
                }
                int anchorHit = penTool.HitTestAnchor(imagePos, 10);
                if (alt && anchorHit >= 0)
                {
                    // Photoshop: Alt+klik anchor = convert smooth→corner; Alt+drag
                    // langsung menarik handle-out baru dari anchor tersebut.
                    penTool.ConvertToCorner(anchorHit);
                    _penDragHandleIndex = anchorHit;
                    _penDragHandleIsIn = false;
                    e.Pointer.Capture(img);
                    RenderOverlay();
                    return true;
                }
                if (ctrl && !alt)
                {
                    if (anchorHit >= 0) { _penDragIndex = anchorHit; e.Pointer.Capture(img); return true; }
                }
                e.Pointer.Capture(img);
                penTool.PointerDown(imagePos, alt);
            }
            else
            {
                e.Pointer.Capture(img);
                _activeSelectionTool.PointerDown(imagePos);
                if (_activeTool == EditToolKind.PolyLasso &&
                    !_activeSelectionTool.IsActive && _activeSelectionTool.CanCommit)
                {
                    CommitSelectionToState(_activeSelectionTool, e.KeyModifiers);
                    return true;
                }
            }
            RenderOverlay();
            return true;
        }

        switch (_activeTool)
        {
            case EditToolKind.Brush:
            case EditToolKind.Eraser:
                BeginStroke(centered, e);
                return true;

            case EditToolKind.Move:
                e.Pointer.Capture(img);   // agar release selalu terdeteksi walau keluar kontrol
                BeginMoveSelection(imagePos, e.KeyModifiers);
                return true;

            case EditToolKind.RefineEdge:
                e.Pointer.Capture(img);
                BeginRefineEdge(imagePos);
                return true;

            case EditToolKind.MagicWand:
                ApplyWand(centered, e.KeyModifiers);
                return true;

        }

        return false;
    }

    private bool EditorPointerMoved(Image img, PointerEventArgs e)
    {
        if (!_editMode || _session == null) return false;
        if (img.Name != "ImgResult") return false;

        var pos = e.GetPosition(img);
        var imagePos = PointerToImage(pos);
        double halfW = _session.Width / 2.0;
        double halfH = _session.Height / 2.0;
        var centered = new Vec2(imagePos.X + halfW, imagePos.Y + halfH);

        if (_strokeActive)
        {
            ContinueStroke(centered);
            return true;
        }

        if (_movingSelection)
        {
            ContinueMoveSelection(imagePos);
            return true;
        }

        if (_refineEdgeActive)
        {
            ContinueRefineEdge(imagePos);
            return true;
        }

        // Brush HUD ala Photoshop (Alt + klik kanan + geser).
        if (_brushHudActive)
        {
            UpdateBrushHud(pos, centered);
            return true;
        }

        // Drag anchor pen (Ctrl+klik pada anchor, direct-selection).
        if (_penDragIndex >= 0 && _activeSelectionTool is PenTool penDrag)
        {
            penDrag.MoveAnchor(_penDragIndex, imagePos);
            RenderOverlay();
            return true;
        }

        // Drag knob handle Bézier pen; tahan Alt untuk mematahkan simetri handle.
        if (_penDragHandleIndex >= 0 && _activeSelectionTool is PenTool penHandle)
        {
            var ha = penHandle.Anchors[_penDragHandleIndex];
            var rel = new Vec2(imagePos.X - ha.Point.X, imagePos.Y - ha.Point.Y);
            penHandle.SetHandle(_penDragHandleIndex, _penDragHandleIsIn, rel,
                mirror: !e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            RenderOverlay();
            return true;
        }

        // Hover highlight anchor/handle pen + kursor kontekstual (tanpa drag).
        if (_activeSelectionTool is PenTool penHov && penHov.Anchors.Count > 0
            && _penDragIndex < 0 && _penDragHandleIndex < 0
            && !e.GetCurrentPoint(img).Properties.IsLeftButtonPressed)
        {
            if (UpdatePenHover(penHov, imagePos))
                RenderOverlay();
        }

        // Selama jalur seleksi sedang dibangun, teruskan gerakan (pen/lasso/poly).
        if (_activeSelectionTool is SelectionTool st &&
            (st.IsActive || st is PenTool { IsClosed: false } && st.CurrentPath.Count > 0))
        {
            bool pressed = e.GetCurrentPoint(img).Properties.IsLeftButtonPressed;
            if (st is MarqueeSelectionTool mq)
            {
                mq.Constrain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                mq.FromCenter = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            }
            st.PointerMove(imagePos, pressed);
            RenderOverlay();
            return true;
        }

        // Brush/eraser cursor tetap terlihat saat pointer hanya hover.
        if (_activeTool is EditToolKind.Brush or EditToolKind.Eraser)
        {
            DrawBrushCursor(centered);
            return true;
        }

        // Cursor kuas Refine Edge.
        if (_activeTool == EditToolKind.RefineEdge)
        {
            DrawRefineEdgeCursor(imagePos);
            return true;
        }

        return false;
    }

    private bool EditorPointerReleased(Image img, PointerReleasedEventArgs e)
    {
        if (!_editMode || _session == null) return false;
        if (img.Name != "ImgResult") return false;

        // Akhiri brush HUD (tombol kanan) sebelum handler tombol kiri.
        if (_brushHudActive)
        {
            EndBrushHud();
            e.Pointer.Capture(null);
            return true;
        }

        if (_strokeActive)
        {
            EndStroke();
            e.Pointer.Capture(null);
            return true;
        }

        if (_movingSelection)
        {
            EndMoveSelection();
            e.Pointer.Capture(null);
            return true;
        }

        if (_refineEdgeActive)
        {
            EndRefineEdge();
            e.Pointer.Capture(null);
            return true;
        }

        if (_penDragIndex >= 0 || _penDragHandleIndex >= 0)
        {
            _penDragIndex = -1;
            _penDragHandleIndex = -1;
            e.Pointer.Capture(null);
            RenderOverlay();
            return true;
        }

        if (_activeSelectionTool is SelectionTool t && t.IsActive)
        {
            var pos = e.GetPosition(img);
            var imagePos = PointerToImage(pos);
            t.PointerUp(imagePos);

            // Pen tidak auto-commit saat mouse dilepas, sesuai perilaku Photoshop.
            if (!SelectionInteractionPolicy.CommitsOnPointerRelease(_activeTool))
            {
                e.Pointer.Capture(null);
                RenderOverlay();
                return true;
            }

            if (t.CanCommit) CommitSelectionToState(t, e.KeyModifiers);
            else { t.Cancel(); RenderOverlay(); }
            e.Pointer.Capture(null);
            return true;
        }

        e.Pointer.Capture(null);
        return false;
    }

}

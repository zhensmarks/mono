using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

/// <summary>
/// Bagian Pen Tool dari editor pixelcut (<see cref="PreviewWindow"/>).
/// Dipisah dari PreviewWindow.Editor.cs agar ukuran tiap file tetap terkendali.
/// Berisi state, hit-test visual, dan rendering overlay path Bezier ala Photoshop.
/// </summary>
public partial class PreviewWindow
{
    /// <summary>Target hover kursor pada overlay Pen Tool (highlight + kursor kontekstual).</summary>
    private enum PenHoverKind { None, Anchor, Handle }

    private int _penDragIndex = -1;
    private int _penDragHandleIndex = -1; // anchor yang handle-nya sedang di-drag
    private bool _penDragHandleIsIn;
    private PenHoverKind _penHoverKind = PenHoverKind.None;
    private int _penHoverIndex = -1;
    private bool _penHoverIsIn;

    /// Gambar anchor, handle Bézier, dan rubber band Pen Tool dengan gaya Photoshop.
    /// Handle disimpan sebagai vektor RELATIF terhadap anchor, jadi titik absolutnya
    /// adalah <c>a.Point + a.HandleIn/Out</c>. Anchor/handle yang di-hover atau
    /// sedang di-drag disorot kuning.
    /// </summary>
    private void DrawPenOverlay(Canvas overlay, PenTool pen)
    {
        var anchors = pen.Anchors;
        if (anchors.Count == 0) return;

        // Garis handle + knob untuk tiap anchor yang punya handle.
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            var p = ImageToOverlay(a.Point);
            var hin = a.Point + a.HandleIn;
            var hout = a.Point + a.HandleOut;
            bool hasIn = HasHandle(a.HandleIn);
            bool hasOut = HasHandle(a.HandleOut);

            if (hasIn)
            {
                var sh = ImageToOverlay(hin);
                DrawLine(overlay, p, sh, "#99A9C7FF");
                DrawHandleKnob(overlay, sh, IsHotHandle(i, true));
            }
            if (hasOut)
            {
                var sh = ImageToOverlay(hout);
                DrawLine(overlay, p, sh, "#99A9C7FF");
                DrawHandleKnob(overlay, sh, IsHotHandle(i, false));
            }
        }

        // Rubber band: garis putus-putus dari anchor terakhir ke kursor.
        // Memakai warna path yang dipilih (transparan) agar tidak ada "warna lain".
        if (pen.IsActive && !pen.IsClosed && anchors.Count > 0)
        {
            var last = ImageToOverlay(anchors[anchors.Count - 1].Point);
            var cur = ImageToOverlay(pen.Cursor);
            var c = PenPathColor();
            DrawLine(overlay, last, cur, $"#80{c.R:X2}{c.G:X2}{c.B:X2}", dash: true);
        }

        // Anchor: kotak (corner) atau diamond (smooth/has handle).
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            var p = ImageToOverlay(a.Point);
            bool smooth = HasHandle(a.HandleIn) || HasHandle(a.HandleOut);
            bool isFirst = i == 0;
            bool nearFirst = isFirst && anchors.Count >= 3 && pen.IsActive;
            bool hot = nearFirst || i == _penDragIndex || i == _penDragHandleIndex
                || (_penHoverKind == PenHoverKind.Anchor && _penHoverIndex == i);
            // Anchor mengikuti warna garis path (pola outline hitam dipertahankan);
            // yang di-hover / di-drag tetap disorot kuning.
            IBrush fill = hot ? new SolidColorBrush(Color.Parse("#FFE24A")) : new SolidColorBrush(PenPathColor());

            var shape = smooth
                ? (Shape)new Polygon
                {
                    Points = new AvaloniaList<Point>
                    {
                        new Point(p.X, p.Y - 5), new Point(p.X + 5, p.Y),
                        new Point(p.X, p.Y + 5), new Point(p.X - 5, p.Y)
                    },
                    Fill = fill,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                }
                : (Shape)new Rectangle
                {
                    Width = 8, Height = 8,
                    Fill = fill,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

            double ox = p.X - (smooth ? 5 : 4);
            double oy = p.Y - (smooth ? 5 : 4);
            Canvas.SetLeft(shape, ox);
            Canvas.SetTop(shape, oy);
            overlay.Children.Add(shape);
        }

        // Indikator Photoshop: lingkaran kecil di kursor saat hover anchor pertama
        // (path bisa ditutup dengan klik).
        if (anchors.Count >= 3 && pen.IsActive && !pen.IsClosed
            && _penHoverKind == PenHoverKind.Anchor && _penHoverIndex == 0)
        {
            var cur = ImageToOverlay(pen.Cursor);
            var circle = new Ellipse
            {
                Width = 10, Height = 10,
                Stroke = new SolidColorBrush(Color.Parse("#FFE24A")),
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(circle, cur.X + 12);
            Canvas.SetTop(circle, cur.Y - 5);
            overlay.Children.Add(circle);
        }
    }

    private bool IsHotHandle(int anchorIndex, bool isIn) =>
        (_penHoverKind == PenHoverKind.Handle && _penHoverIndex == anchorIndex && _penHoverIsIn == isIn)
        || (_penDragHandleIndex == anchorIndex && _penDragHandleIsIn == isIn);

    private void DrawHandleKnob(Canvas overlay, Point p, bool highlight = false)
    {
        double size = highlight ? 9 : 6;
        var knob = new Ellipse
        {
            Width = size, Height = size,
            Fill = new SolidColorBrush(Color.Parse(highlight ? "#FFE24A" : "#FF3B82F6")),
            Stroke = Brushes.White,
            StrokeThickness = 1
        };
        Canvas.SetLeft(knob, p.X - size / 2);
        Canvas.SetTop(knob, p.Y - size / 2);
        overlay.Children.Add(knob);
    }

    /// <summary>Hitung hover anchor/handle pen; kembalikan true bila state berubah.</summary>
    private bool UpdatePenHover(PenTool pen, Vec2 imagePos)
    {
        var (handleHit, handleAnchor, handleIsIn) = pen.HitTestHandle(imagePos, 10);
        PenHoverKind kind;
        int index;
        bool isIn;
        if (handleHit)
        {
            kind = PenHoverKind.Handle; index = handleAnchor; isIn = handleIsIn;
        }
        else
        {
            int anchorHit = pen.HitTestAnchor(imagePos, 10);
            if (anchorHit >= 0) { kind = PenHoverKind.Anchor; index = anchorHit; isIn = false; }
            else { kind = PenHoverKind.None; index = -1; isIn = false; }
        }

        bool changed = kind != _penHoverKind || index != _penHoverIndex || isIn != _penHoverIsIn;
        _penHoverKind = kind;
        _penHoverIndex = index;
        _penHoverIsIn = isIn;
        if (changed && !_toolState.IsSpacePanActive)
            Cursor = kind == PenHoverKind.None
                ? new Cursor(StandardCursorType.Cross)
                : new Cursor(StandardCursorType.Hand);
        return changed;
    }
}

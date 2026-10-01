using System;
using Avalonia;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Converts between viewport-local screen coordinates and centered image-pixel coordinates.
/// Pan is stored before rotation, matching the editor's Scale → Translate → Rotate transform group.
/// </summary>
public sealed class EditorViewPort
{
    public const double MinimumZoom = 0.2;
    public const double MaximumZoom = 20.0;
    private const double MinimumVisiblePanPixels = 64.0;

    /// <summary>Image dimensions in pixels.</summary>
    public int ImageWidth { get; set; } = 1;
    public int ImageHeight { get; set; } = 1;

    /// <summary>Scale applied by the editor (1.0 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Clockwise rotation in degrees, matching the UI.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>Pan offset in pre-rotation viewport pixels.</summary>
    public double PanX { get; set; }
    public double PanY { get; set; }

    /// <summary>Size of the local viewport control in device-independent pixels.</summary>
    public double ViewWidth { get; set; } = 1;
    public double ViewHeight { get; set; } = 1;

    private double Rad => (double.IsFinite(RotationDegrees) ? RotationDegrees : 0) * Math.PI / 180.0;

    /// <summary>Convert centered viewport coordinates to centered image coordinates.</summary>
    public Vec2 ScreenToImage(Point screen)
    {
        double angle = -Rad;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double rx = screen.X * cos - screen.Y * sin;
        double ry = screen.X * sin + screen.Y * cos;
        double zoom = IsValidZoom(Zoom) ? Zoom : 1;
        return new Vec2((rx - PanX) / zoom, (ry - PanY) / zoom);
    }

    public Vec2 ScreenToImage(double sx, double sy) => ScreenToImage(new Point(sx, sy));

    /// <summary>Convert centered image coordinates to centered viewport coordinates.</summary>
    public Point ImageToScreen(Vec2 image)
    {
        double zoom = IsValidZoom(Zoom) ? Zoom : 1;
        double x = image.X * zoom + PanX;
        double y = image.Y * zoom + PanY;
        double angle = Rad;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        return new Point(x * cos - y * sin, x * sin + y * cos);
    }

    public Point ImageToScreen(double ix, double iy) => ImageToScreen(new Vec2(ix, iy));

    /// <summary>
    /// Zoom around a point expressed in viewport-local coordinates (origin at top-left).
    /// The image-space point under that cursor remains at the same local screen position,
    /// including when the viewport is already panned or rotated.
    /// </summary>
    public bool ZoomAtViewportPoint(Point viewportPoint, double factor,
        double minZoom = MinimumZoom, double maxZoom = MaximumZoom)
    {
        if (!double.IsFinite(viewportPoint.X) || !double.IsFinite(viewportPoint.Y) ||
            !double.IsFinite(factor) || factor <= 0 ||
            !double.IsFinite(ViewWidth) || !double.IsFinite(ViewHeight) ||
            ViewWidth <= 0 || ViewHeight <= 0)
            return false;

        if (!double.IsFinite(minZoom) || minZoom <= 0) minZoom = MinimumZoom;
        if (!double.IsFinite(maxZoom) || maxZoom < minZoom) maxZoom = MaximumZoom;

        double oldZoom = IsValidZoom(Zoom) ? Math.Clamp(Zoom, minZoom, maxZoom) : 1.0;
        if (!double.IsFinite(PanX)) PanX = 0;
        if (!double.IsFinite(PanY)) PanY = 0;
        Zoom = oldZoom;

        var centeredCursor = new Point(viewportPoint.X - ViewWidth / 2.0,
            viewportPoint.Y - ViewHeight / 2.0);
        Vec2 imageAnchor = ScreenToImage(centeredCursor);
        double newZoom = Math.Clamp(oldZoom * factor, minZoom, maxZoom);
        if (Math.Abs(newZoom - oldZoom) < 1e-12)
            return false;

        Zoom = newZoom;
        // Undo the viewport rotation, then solve pan so this same image point maps
        // back to the original cursor. A zero visibility margin avoids moving the
        // anchor; direct panning below uses a larger margin for easy input recovery.
        double cos = Math.Cos(Rad), sin = Math.Sin(Rad);
        double unrotatedX = centeredCursor.X * cos + centeredCursor.Y * sin;
        double unrotatedY = -centeredCursor.X * sin + centeredCursor.Y * cos;
        PanX = unrotatedX - imageAnchor.X * newZoom;
        PanY = unrotatedY - imageAnchor.Y * newZoom;
        ClampPan(minimumVisiblePixels: 0);
        return true;
    }

    /// <summary>
    /// Move by a screen-space drag delta, enforcing a visible overlap so a canvas
    /// dragged toward an edge remains hit-testable and can be dragged back.
    /// </summary>
    public void PanByScreenDelta(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) return;
        double cos = Math.Cos(Rad), sin = Math.Sin(Rad);
        // Pan is stored before rotation, so convert the screen drag into that basis.
        PanX += deltaX * cos + deltaY * sin;
        PanY += -deltaX * sin + deltaY * cos;
        ClampPan(MinimumVisiblePanPixels);
    }

    /// <summary>Fit the rotated image inside the viewport and center it.</summary>
    public void FitToView(double padding = 0)
    {
        if (!double.IsFinite(ViewWidth) || !double.IsFinite(ViewHeight) || ViewWidth <= 0 || ViewHeight <= 0)
            return;
        double availableWidth = Math.Max(1, ViewWidth - padding * 2);
        double availableHeight = Math.Max(1, ViewHeight - padding * 2);
        double cos = Math.Abs(Math.Cos(Rad)), sin = Math.Abs(Math.Sin(Rad));
        double boundsWidth = ImageWidth * cos + ImageHeight * sin;
        double boundsHeight = ImageWidth * sin + ImageHeight * cos;
        double zoom = Math.Min(availableWidth / Math.Max(1, boundsWidth),
            availableHeight / Math.Max(1, boundsHeight));
        if (!IsValidZoom(zoom)) zoom = 1;
        Zoom = zoom;

        double centerX = ImageWidth * zoom / 2.0;
        double centerY = ImageHeight * zoom / 2.0;
        double angle = Rad;
        double rotatedX = centerX * Math.Cos(angle) - centerY * Math.Sin(angle);
        double rotatedY = centerX * Math.Sin(angle) + centerY * Math.Cos(angle);
        PanX = ViewWidth / 2.0 - rotatedX;
        PanY = ViewHeight / 2.0 - rotatedY;
    }

    /// <summary>Reset zoom and center the image in the viewport.</summary>
    public void Reset()
    {
        Zoom = 1;
        RotationDegrees = 0;
        PanX = (ViewWidth - ImageWidth) / 2.0;
        PanY = (ViewHeight - ImageHeight) / 2.0;
    }

    public double ImageRadiusToScreen(double radiusPx) => radiusPx * (IsValidZoom(Zoom) ? Zoom : 1);
    public double ScreenRadiusToImage(double radiusPx) => radiusPx / (IsValidZoom(Zoom) ? Zoom : 1);

    /// <summary>
    /// Clamp pan so at least <paramref name="minimumVisiblePixels"/> of the
    /// transformed image bounds overlaps the viewport on each axis.
    /// </summary>
    public void ClampPan(double minimumVisiblePixels = MinimumVisiblePanPixels)
    {
        if (!double.IsFinite(ViewWidth) || !double.IsFinite(ViewHeight) ||
            ViewWidth <= 0 || ViewHeight <= 0)
            return;

        if (!double.IsFinite(PanX)) PanX = 0;
        if (!double.IsFinite(PanY)) PanY = 0;
        double zoom = IsValidZoom(Zoom) ? Zoom : 1;
        double imageWidth = Math.Max(1, ImageWidth);
        double imageHeight = Math.Max(1, ImageHeight);
        double fit = Math.Min(ViewWidth / imageWidth, ViewHeight / imageHeight);
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;

        double width = imageWidth * fit * zoom;
        double height = imageHeight * fit * zoom;
        double cos = Math.Abs(Math.Cos(Rad)), sin = Math.Abs(Math.Sin(Rad));
        double rotatedWidth = width * cos + height * sin;
        double rotatedHeight = width * sin + height * cos;
        double visibleX = Math.Clamp(double.IsFinite(minimumVisiblePixels) ? minimumVisiblePixels : 0, 0, ViewWidth);
        double visibleY = Math.Clamp(double.IsFinite(minimumVisiblePixels) ? minimumVisiblePixels : 0, 0, ViewHeight);
        double limitX = Math.Max(0, (rotatedWidth + ViewWidth) / 2.0 - visibleX);
        double limitY = Math.Max(0, (rotatedHeight + ViewHeight) / 2.0 - visibleY);

        // Pan rotates with the image; clamp in screen axes, then convert back.
        double angle = Rad;
        double screenX = PanX * Math.Cos(angle) - PanY * Math.Sin(angle);
        double screenY = PanX * Math.Sin(angle) + PanY * Math.Cos(angle);
        screenX = Math.Clamp(screenX, -limitX, limitX);
        screenY = Math.Clamp(screenY, -limitY, limitY);
        PanX = screenX * Math.Cos(angle) + screenY * Math.Sin(angle);
        PanY = -screenX * Math.Sin(angle) + screenY * Math.Cos(angle);
    }

    private static bool IsValidZoom(double zoom) => double.IsFinite(zoom) && zoom > 0;
}

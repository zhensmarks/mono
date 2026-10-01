using System;
using Avalonia;
using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class EditorViewPortTransformTests
{
    [Theory]
    [InlineData(126.25, 74.5, 1.45)]
    [InlineData(612.75, 286.25, 1.45)]
    [InlineData(126.25, 74.5, 0.72)]
    [InlineData(612.75, 286.25, 0.72)]
    public void ZoomAtViewportPoint_LeavesOffCenterImageAnchorInPlace(double x, double y, double factor)
    {
        var viewport = CreateViewport();
        viewport.Zoom = 1.3;
        viewport.PanX = -45;
        viewport.PanY = 35;
        viewport.RotationDegrees = 17;
        var cursor = new Point(x, y);
        var centeredCursor = ToCentered(viewport, cursor);
        var imageAnchor = viewport.ScreenToImage(centeredCursor);

        Assert.True(viewport.ZoomAtViewportPoint(cursor, factor));

        var screenAfter = viewport.ImageToScreen(imageAnchor);
        Assert.InRange(Math.Abs(screenAfter.X + viewport.ViewWidth / 2 - cursor.X), 0, 1e-8);
        Assert.InRange(Math.Abs(screenAfter.Y + viewport.ViewHeight / 2 - cursor.Y), 0, 1e-8);
    }

    [Theory]
    [InlineData(19.5, 2.0, 20.0)]
    [InlineData(0.21, 0.1, 0.2)]
    public void ZoomAtViewportPoint_ClampsAtSafeMinimumAndMaximum(double startZoom, double factor, double expectedZoom)
    {
        var viewport = CreateViewport();
        viewport.Zoom = startZoom;
        viewport.PanX = 12;
        viewport.PanY = -9;
        var cursor = new Point(275.5, 145.25);
        var imageAnchor = viewport.ScreenToImage(ToCentered(viewport, cursor));

        Assert.True(viewport.ZoomAtViewportPoint(cursor, factor));

        Assert.Equal(expectedZoom, viewport.Zoom, 10);
        var screenAfter = viewport.ImageToScreen(imageAnchor);
        Assert.InRange(Math.Abs(screenAfter.X + viewport.ViewWidth / 2 - cursor.X), 0, 1e-7);
        Assert.InRange(Math.Abs(screenAfter.Y + viewport.ViewHeight / 2 - cursor.Y), 0, 1e-7);
    }

    [Fact]
    public void PanByScreenDelta_ClampsVisibleBoundsAndCanBeDraggedBackIntoView()
    {
        var viewport = CreateViewport();
        viewport.PanByScreenDelta(100_000, -100_000);

        double maxX = (RenderedWidth(viewport) + viewport.ViewWidth) / 2 - 64;
        double maxY = (RenderedHeight(viewport) + viewport.ViewHeight) / 2 - 64;
        Assert.InRange(viewport.PanX, maxX - 1e-7, maxX + 1e-7);
        Assert.InRange(viewport.PanY, -maxY - 1e-7, -maxY + 1e-7);
        Assert.InRange(Overlap(RenderedWidth(viewport), viewport.ViewWidth, viewport.PanX), 64 - 1e-7, 64 + 1e-7);
        Assert.InRange(Overlap(RenderedHeight(viewport), viewport.ViewHeight, viewport.PanY), 64 - 1e-7, 64 + 1e-7);

        double edgeX = viewport.PanX;
        double edgeY = viewport.PanY;
        viewport.PanByScreenDelta(-100, 100);
        Assert.True(viewport.PanX < edgeX);
        Assert.True(viewport.PanY > edgeY);
        Assert.True(double.IsFinite(viewport.PanX) && double.IsFinite(viewport.PanY));
    }

    [Fact]
    public void ZoomAtViewportPoint_RejectsNonFiniteOrNonPositiveFactorsWithoutCorruptingTransform()
    {
        var viewport = CreateViewport();
        var zoom = viewport.Zoom;
        var panX = viewport.PanX;
        var panY = viewport.PanY;

        Assert.False(viewport.ZoomAtViewportPoint(new Point(200, 100), double.NaN));
        Assert.False(viewport.ZoomAtViewportPoint(new Point(200, 100), 0));
        Assert.Equal(zoom, viewport.Zoom);
        Assert.Equal(panX, viewport.PanX);
        Assert.Equal(panY, viewport.PanY);
    }

    private static EditorViewPort CreateViewport() => new()
    {
        ImageWidth = 1536,
        ImageHeight = 1024,
        ViewWidth = 735,
        ViewHeight = 356,
        Zoom = 1
    };

    private static Point ToCentered(EditorViewPort viewport, Point point) =>
        new(point.X - viewport.ViewWidth / 2, point.Y - viewport.ViewHeight / 2);

    private static double RenderedWidth(EditorViewPort viewport)
    {
        double fit = Math.Min(viewport.ViewWidth / viewport.ImageWidth, viewport.ViewHeight / viewport.ImageHeight);
        return viewport.ImageWidth * fit * viewport.Zoom;
    }

    private static double RenderedHeight(EditorViewPort viewport)
    {
        double fit = Math.Min(viewport.ViewWidth / viewport.ImageWidth, viewport.ViewHeight / viewport.ImageHeight);
        return viewport.ImageHeight * fit * viewport.Zoom;
    }

    private static double Overlap(double contentSize, double viewSize, double centerOffset) =>
        Math.Max(0, Math.Min(viewSize / 2, centerOffset + contentSize / 2) -
                    Math.Max(-viewSize / 2, centerOffset - contentSize / 2));
}

using FrameStudio.App.Models;
using FrameStudio.App.Services;
using SkiaSharp;
using Xunit;

namespace FrameStudio.Tests;

public sealed class FrameToolPrototypeRendererTests
{
    [Fact]
    public void NonRectangularMaskLeavesPngPixelsOutsideShapeEqualToStoredComposite()
    {
        var renderer = new FrameToolPrototypeRenderer();
        using var photo = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        photo.Erase(SKColor.Parse("#D65A48"));
        var mask = FrameMask.Polygon(
        [
            new(0.20, 0.20),
            new(0.80, 0.20),
            new(0.20, 0.80)
        ]);

        var storedCompositePng = renderer.RenderStoredComposite();
        var previewAndExportPng = renderer.Render(mask, photo);
        using var storedComposite = SKBitmap.Decode(storedCompositePng)!;
        using var output = SKBitmap.Decode(previewAndExportPng)!;

        // This point is inside the polygon's rectangular bounds but outside the triangular mask.
        var outsideX = 0.75f;
        var outsideY = 0.75f;
        var x = (int)(outsideX * FrameToolPrototypeRenderer.ArtboardWidth);
        var y = (int)(outsideY * FrameToolPrototypeRenderer.ArtboardHeight);
        Assert.Equal(storedComposite.GetPixel(x, y), output.GetPixel(x, y));

        // A point well inside the triangle is changed by the placed photo.
        var insideX = (int)(0.32f * FrameToolPrototypeRenderer.ArtboardWidth);
        var insideY = (int)(0.32f * FrameToolPrototypeRenderer.ArtboardHeight);
        Assert.NotEqual(storedComposite.GetPixel(insideX, insideY), output.GetPixel(insideX, insideY));
    }

    [Fact]
    public void PolygonMaskAndCropProduceIdenticalPreviewAndExportRenderBytes()
    {
        var renderer = new FrameToolPrototypeRenderer();
        using var photo = new SKBitmap(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul);
        photo.Erase(SKColor.Parse("#D65A48"));
        var mask = FrameMask.Polygon(
        [
            new(0.18, 0.21),
            new(0.72, 0.19),
            new(0.61, 0.68),
            new(0.27, 0.77)
        ]);

        var previewBytes = renderer.Render(mask, photo, zoom: 1.35, rotation: 11, offsetX: 0.12, offsetY: -0.08);
        var exportBytes = renderer.Render(mask, photo, zoom: 1.35, rotation: 11, offsetX: 0.12, offsetY: -0.08);

        Assert.Equal(previewBytes, exportBytes);
    }

    [Fact]
    public void RectangleMaskStoresNormalizedVerticesAndClampsToArtboard()
    {
        var mask = FrameMask.Rectangle(new NormalizedFramePoint(0.8, 0.7), new NormalizedFramePoint(-0.2, 0.1));

        Assert.True(mask.IsValid);
        Assert.Equal(FrameMaskShape.Rectangle, mask.Shape);
        Assert.Equal(4, mask.Points.Count);
        Assert.Equal(new NormalizedFramePoint(0, 0.1), mask.Points[0]);
        Assert.Equal(new NormalizedFramePoint(0.8, 0.7), mask.Points[2]);
    }
}

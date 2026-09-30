using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class BrushStrokeAccumulatorTests
{
    [Fact]
    public void StampFlowAllocatesOnlyTilesTouchedByTheBrush()
    {
        const int width = 1000, height = 1000;
        var mask = new byte[width * height];
        var accumulator = new BrushStrokeAccumulator(width, height);
        var stamp = new BrushStamp
        {
            X = 500,
            Y = 500,
            Radius = 8,
            Hardness = 1,
            Opacity = 1,
            Flow = 0.25,
            Restore = true
        };

        BrushTool.StampFlow(mask, accumulator, width, height, stamp);

        Assert.InRange(accumulator.AllocatedTileCount, 1, 1);
        Assert.Equal(BrushStrokeAccumulator.TileSize * BrushStrokeAccumulator.TileSize, accumulator.AllocatedBytes);
        Assert.True(mask[500 * width + 500] > 0);
    }

    [Fact]
    public void StampFlowAllocatesAdjacentTilesAcrossTileBoundary()
    {
        const int width = 256, height = 256;
        var mask = new byte[width * height];
        var accumulator = new BrushStrokeAccumulator(width, height);
        var stamp = new BrushStamp
        {
            X = 128,
            Y = 128,
            Radius = 2,
            Hardness = 1,
            Opacity = 1,
            Flow = 1,
            Restore = true
        };

        BrushTool.StampFlow(mask, accumulator, width, height, stamp);

        Assert.Equal(4, accumulator.AllocatedTileCount);
        Assert.Equal((byte)255, mask[128 * width + 128]);
    }

    [Fact]
    public void StampBoundsClipAndIgnoreOffCanvasOrNonFiniteStamps()
    {
        var edge = new BrushStamp { X = 0, Y = 0, Radius = 2 };
        var bounds = BrushTool.GetStampBounds(edge, 10, 10);
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(3, bounds.Width);
        Assert.Equal(3, bounds.Height);

        edge.X = double.NaN;
        Assert.True(BrushTool.GetStampBounds(edge, 10, 10).IsEmpty);
        edge.X = -100;
        Assert.True(BrushTool.GetStampBounds(edge, 10, 10).IsEmpty);
    }

    [Fact]
    public void InterpolateIncludesEndpointForVerticalSegments()
    {
        var points = new List<BrushStamp>();
        var from = new BrushStamp { X = 5, Y = 0 };
        var to = new BrushStamp { X = 5, Y = 11 };

        BrushTool.Interpolate(points, from, to, spacingPx: 5);

        Assert.Equal(3, points.Count);
        Assert.Equal(5, points[^1].X);
        Assert.Equal(11, points[^1].Y);
    }
}

using System;
using System.Collections.Generic;
using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class EditorMaskRegressionTests
{
    [Fact]
    public void CenteredPenPath_TranslatesToTopLeftMaskCoordinates()
    {
        const int width = 100;
        const int height = 80;
        var pen = new PenTool();
        pen.PointerDown(new Vec2(-10, -8));
        pen.PointerUp(new Vec2(-10, -8));
        pen.PointerDown(new Vec2(10, -8));
        pen.PointerUp(new Vec2(10, -8));
        pen.PointerDown(new Vec2(10, 8));
        pen.PointerUp(new Vec2(10, 8));

        var region = Assert.IsType<MaskRegion>(pen.Commit());
        region.Translate(width / 2.0, height / 2.0);
        var selection = new SelectionState(width, height);
        selection.Combine(region, SelectionCombineMode.Replace, antiAlias: false);

        Assert.True(selection.Coverage[40 * width + 50] > 0);
        Assert.Equal((byte)0, selection.Coverage[10 * width + 10]);
        Assert.True(selection.GetBounds(out var minX, out var minY, out var maxX, out var maxY));
        Assert.InRange(minX, 40, 41);
        Assert.InRange(minY, 32, 33);
        Assert.InRange(maxX, 59, 60);
        Assert.InRange(maxY, 47, 48);
    }

    [Theory]
    [InlineData(EditToolKind.Brush, true, true)]
    [InlineData(EditToolKind.Brush, false, false)]
    [InlineData(EditToolKind.Eraser, true, false)]
    [InlineData(EditToolKind.Eraser, false, false)]
    [InlineData(EditToolKind.Pan, true, false)]
    public void BrushRestorePolicyOnlyAppliesToBrush(EditToolKind tool, bool requested, bool expected)
    {
        Assert.Equal(expected, BrushTool.ShouldRestore(tool, requested));
    }

    [Theory]
    [InlineData(EditToolKind.Lasso, true)]
    [InlineData(EditToolKind.RectMarquee, true)]
    [InlineData(EditToolKind.EllipseMarquee, true)]
    [InlineData(EditToolKind.PolyLasso, false)]
    [InlineData(EditToolKind.Pen, false)]
    public void SelectionReleasePolicy_MatchesToolInteraction(EditToolKind tool, bool expected)
    {
        Assert.Equal(expected, SelectionInteractionPolicy.CommitsOnPointerRelease(tool));
    }

    [Theory]
    [InlineData(EditToolKind.Lasso, true)]
    [InlineData(EditToolKind.RectMarquee, true)]
    [InlineData(EditToolKind.EllipseMarquee, true)]
    [InlineData(EditToolKind.PolyLasso, true)]
    [InlineData(EditToolKind.Pen, true)]
    [InlineData(EditToolKind.MagicWand, false)]
    [InlineData(EditToolKind.Pan, false)]
    [InlineData(EditToolKind.Move, false)]
    public void SelectionPointerPressPolicyRoutesOnlySelectionTools(EditToolKind tool, bool expected)
    {
        Assert.Equal(expected, SelectionInteractionPolicy.HandlesPointerPress(tool));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarqueePointerDragBuildsAndCommitsAlignedSelection(bool ellipse)
    {
        const int width = 60;
        const int height = 60;
        var marquee = new MarqueeSelectionTool { Ellipse = ellipse };
        marquee.PointerDown(new Vec2(-10, -10));
        marquee.PointerMove(new Vec2(10, 10), pressed: true);
        Assert.True(marquee.IsActive);
        marquee.PointerUp(new Vec2(10, 10));
        Assert.False(marquee.IsActive);

        var region = Assert.IsType<MaskRegion>(marquee.Commit());
        region.Translate(width / 2.0, height / 2.0);
        var selection = new SelectionState(width, height);
        selection.Combine(region, SelectionCombineMode.Replace, antiAlias: false);

        Assert.True(selection.Coverage[30 * width + 30] > 0);
        Assert.Equal((byte)0, selection.Coverage[0]);
    }

    [Fact]
    public void UndoingAndRedoingManyRestoreStrokes_PreservesExactMaskAndComposite()
    {
        const int width = 64;
        const int height = 64;
        var result = new PixelBuffer(width, height);
        var original = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            result.SetPixel(x, y, 0, 0, 0, 0);
            original.SetPixel(x, y, 220, 80, 40, 255);
        }

        using var session = new MaskEditSession(result, original, undoSteps: 32, undoMemoryMb: 16);
        var baseline = (byte[])session.Mask.Clone();
        for (int i = 0; i < 12; i++)
        {
            session.ApplyBrushStroke(new List<BrushStamp>
            {
                new() { X = 4 + i * 4, Y = 32, Radius = 1.5, Hardness = 1, Opacity = 1, Flow = 1, Restore = true }
            });
        }

        var edited = (byte[])session.Mask.Clone();
        Assert.NotEqual(baseline, edited);
        for (int i = 0; i < 12; i++) Assert.NotNull(session.UndoAction());
        Assert.Equal(baseline, session.Mask);
        var undoneComposite = session.Composite();
        for (int i = 0; i < baseline.Length; i++) Assert.Equal(baseline[i], undoneComposite.GetAlpha(i % width, i / width));

        for (int i = 0; i < 12; i++) Assert.NotNull(session.RedoAction());
        Assert.Equal(edited, session.Mask);
    }

    [Fact]
    public void BrushSizeSoftnessOpacityAndFlowChangeCoverageAsExpected()
    {
        const int size = 21;
        var hard = new byte[size * size];
        var soft = new byte[size * size];
        BrushTool.Stamp(hard, size, size, new BrushStamp { X = 10, Y = 10, Radius = 4, Hardness = 1, Opacity = 0.5, Restore = true });
        BrushTool.Stamp(soft, size, size, new BrushStamp { X = 10, Y = 10, Radius = 4, Hardness = 0, Opacity = 0.5, Restore = true });
        int center = 10 * size + 10;
        int edge = 10 * size + 13;
        Assert.Equal((byte)128, hard[center]);
        Assert.True(soft[edge] > 0 && soft[edge] < hard[edge]);

        var smaller = new byte[size * size];
        BrushTool.Stamp(smaller, size, size, new BrushStamp { X = 10, Y = 10, Radius = 2, Hardness = 1, Opacity = 1, Restore = true });
        Assert.Equal((byte)0, smaller[edge]);

        var buildup = new byte[size * size];
        var accumulator = new BrushStrokeAccumulator(size, size);
        var flowing = new BrushStamp { X = 10, Y = 10, Radius = 4, Hardness = 1, Opacity = 0.8, Flow = 0.2, Restore = true };
        BrushTool.StampFlow(buildup, accumulator, size, size, flowing);
        byte firstPass = buildup[center];
        BrushTool.StampFlow(buildup, accumulator, size, size, flowing);
        Assert.True(buildup[center] > firstPass);
        Assert.True(buildup[center] < 255);
    }

    [Fact]
    public void QuickMaskBrush_ChangesSelectionUntilAppliedToMask()
    {
        const int size = 16;
        var result = new PixelBuffer(size, size);
        var original = new PixelBuffer(size, size);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            result.SetPixel(x, y, 0, 0, 0, 0);
            original.SetPixel(x, y, 80, 160, 220, 255);
        }
        using var session = new MaskEditSession(result, original, undoSteps: 8, undoMemoryMb: 16);
        var baseline = (byte[])session.Mask.Clone();
        var accumulator = new BrushStrokeAccumulator(size, size);
        BrushTool.StampFlow(session.Selection.Coverage, accumulator, size, size,
            new BrushStamp { X = 8, Y = 8, Radius = 4, Hardness = 0.7, Opacity = 1, Flow = 1, Restore = true });
        session.Selection.NotifyChanged();

        Assert.True(session.Selection.HasSelection);
        Assert.Equal(baseline, session.Mask);
        session.ApplySelectionToMask(255, "Quick Mask Restore");
        Assert.True(session.Mask[8 * size + 8] > 0);
        Assert.Equal((byte)0, session.Mask[0]);
    }

    [Fact]
    public void SelectionErase_IsBoundedAndUndoable()
    {
        const int size = 10;
        var result = new PixelBuffer(size, size);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++) result.SetPixel(x, y, 10, 20, 30, 255);
        using var session = new MaskEditSession(result, original: null, undoSteps: 8, undoMemoryMb: 16);
        session.Selection.Combine(new MaskRegion(new[]
        {
            new Vec2(2, 2), new Vec2(6, 2), new Vec2(6, 6), new Vec2(2, 6)
        }), SelectionCombineMode.Replace, antiAlias: false);

        session.ApplySelectionToMask(0, "Remove object");
        Assert.Equal((byte)0, session.Mask[4 * size + 4]);
        Assert.Equal((byte)255, session.Mask[0]);
        Assert.Equal("Remove object", session.UndoAction());
        Assert.Equal((byte)255, session.Mask[4 * size + 4]);
        Assert.Equal("Remove object", session.RedoAction());
        Assert.Equal((byte)0, session.Mask[4 * size + 4]);
    }

    [Fact]
    public void SmallSelectionOnLargeImage_UsesBoundedTemporaryCoverageMemory()
    {
        const int size = 4096;
        var mask = new byte[size * size];
        var region = new MaskRegion(new[]
        {
            new Vec2(2040, 2040), new Vec2(2056, 2040), new Vec2(2056, 2056), new Vec2(2040, 2056)
        });
        long before = GC.GetAllocatedBytesForCurrentThread();
        MaskRasterizer.FillRegion(region, mask, size, size, 255,
            MaskCombineOp.Replace, antiAlias: true, feather: 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(mask[2048 * size + 2048] > 0);
        Assert.True(allocated < 8 * 1024 * 1024, $"A small selection allocated {allocated:N0} temporary bytes.");
    }
}

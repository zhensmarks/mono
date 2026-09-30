using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class MaskEditSessionTests
{
    [Fact]
    public void Composite_UsesOriginalRgbWhenRestoredMaskExceedsCutoutAlpha()
    {
        var result = new PixelBuffer(2, 1);
        result.SetPixel(0, 0, 0, 0, 0, 0);
        result.SetPixel(1, 0, 20, 30, 40, 128);
        var original = new PixelBuffer(2, 1);
        original.SetPixel(0, 0, 200, 100, 50, 255);
        original.SetPixel(1, 0, 120, 80, 220, 255);
        using var session = new MaskEditSession(result, original, undoSteps: 8, undoMemoryMb: 16);

        session.Mask[0] = 255;
        session.Mask[1] = 200;
        var composite = session.Composite();

        composite.GetRgb(0, 0, out var r0, out var g0, out var b0);
        Assert.Equal((200, 100, 50), (r0, g0, b0));
        Assert.Equal((byte)255, composite.GetAlpha(0, 0));
        composite.GetRgb(1, 0, out var r1, out var g1, out var b1);
        Assert.Equal((120, 80, 220), (r1, g1, b1));
        Assert.Equal((byte)200, composite.GetAlpha(1, 0));
    }

    [Fact]
    public void Composite_PreservesMatteRgbUntilMaskActuallyExpands()
    {
        var result = new PixelBuffer(1, 1);
        result.SetPixel(0, 0, 16, 64, 128, 100);
        var original = new PixelBuffer(1, 1);
        original.SetPixel(0, 0, 220, 180, 140, 255);
        using var session = new MaskEditSession(result, original, undoSteps: 8, undoMemoryMb: 16);

        var unchanged = session.Composite();
        unchanged.GetRgb(0, 0, out var matteR, out var matteG, out var matteB);
        Assert.Equal((16, 64, 128), (matteR, matteG, matteB));

        session.BeginEdit("Restore");
        session.Mask[0] = 255;
        var restored = session.Composite();
        restored.GetRgb(0, 0, out var sourceR, out var sourceG, out var sourceB);
        Assert.Equal((220, 180, 140), (sourceR, sourceG, sourceB));

        Assert.Equal("Restore", session.UndoAction());
        Assert.Equal((byte)100, session.Composite().GetAlpha(0, 0));
        Assert.Equal("Restore", session.RedoAction());
        restored = session.Composite();
        restored.GetRgb(0, 0, out sourceR, out sourceG, out sourceB);
        Assert.Equal((220, 180, 140), (sourceR, sourceG, sourceB));
    }

    [Fact]
    public void MissingOriginalNeverTurnsTransparentCutoutRgbOpaqueAndCannotBeSavedAsRestore()
    {
        var result = new PixelBuffer(1, 1);
        result.SetPixel(0, 0, 0, 0, 0, 0);
        using var session = new MaskEditSession(result, original: null, undoSteps: 8, undoMemoryMb: 16);
        session.Mask[0] = 255;

        var composite = session.Composite();
        Assert.Equal((byte)0, composite.GetAlpha(0, 0));
        Assert.True(session.HasPendingRestore);
        Assert.False(session.HasRestoreSource);
        Assert.Throws<InvalidOperationException>(() => session.BakeToFile(Path.Combine(Path.GetTempPath(), "must-not-save.png")));
    }

    [Fact]
    public void CompositeInto_LeavesPixelsOutsideDirtyBoundsUntouched()
    {
        var result = new PixelBuffer(3, 1);
        result.SetPixel(0, 0, 20, 30, 40, 255);
        result.SetPixel(1, 0, 50, 60, 70, 255);
        result.SetPixel(2, 0, 80, 90, 100, 255);
        using var session = new MaskEditSession(result, original: null, undoSteps: 8, undoMemoryMb: 16);
        var target = new PixelBuffer(3, 1);
        target.SetPixel(0, 0, 1, 2, 3, 4);
        target.SetPixel(1, 0, 1, 2, 3, 4);
        target.SetPixel(2, 0, 1, 2, 3, 4);

        session.CompositeInto(target, new PixelBounds(1, 0, 1, 1));

        target.GetRgb(0, 0, out var leftR, out var leftG, out var leftB);
        target.GetRgb(1, 0, out var middleR, out var middleG, out var middleB);
        target.GetRgb(2, 0, out var rightR, out var rightG, out var rightB);
        Assert.Equal((1, 2, 3), (leftR, leftG, leftB));
        Assert.Equal((50, 60, 70), (middleR, middleG, middleB));
        Assert.Equal((1, 2, 3), (rightR, rightG, rightB));
    }
}

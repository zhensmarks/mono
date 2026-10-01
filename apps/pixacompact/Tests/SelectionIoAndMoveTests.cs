using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Skia;
using PixelcutCompact.Services.Editing;
using PixelcutCompact.Views;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class SelectionIoAndMoveTests
{
    static SelectionIoAndMoveTests() => SkiaPlatform.Initialize();

    [Fact]
    public async Task Selection_SaveAndLoadRoundTripPreservesCoverage()
    {
        var expected = new byte[] { 0, 1, 63, 127, 128, 192, 254, 255, 11, 33, 77, 155 };
        var selection = new SelectionState(4, 3);
        Array.Copy(expected, selection.Coverage, expected.Length);
        var path = Path.Combine(Path.GetTempPath(), $"pixacompact-selection-{Guid.NewGuid():N}.png");

        try
        {
            await SelectionIo.SaveAsync(selection, path);
            var loaded = await SelectionIo.LoadAsync(path, selection.Width, selection.Height);

            Assert.Equal(expected, loaded.Coverage);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ShiftDragMaskTranslation_ShiftsSnapshotAndClipsOutsideCanvas()
    {
        const int width = 4;
        const int height = 3;
        var sourceMask = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var shiftedMask = new byte[sourceMask.Length];
        var expected = new byte[] { 0, 0, 0, 0, 0, 1, 2, 3, 0, 5, 6, 7 };

        PreviewWindow.ShiftBuffer(sourceMask, shiftedMask, width, height, dx: 1, dy: 1);

        Assert.Equal(expected, shiftedMask);
    }
}

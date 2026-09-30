using Avalonia.Media;
using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class CompositeRendererTests
{
    [Fact]
    public void RenderMaskOverlayInto_UpdatesOnlyTheRequestedBounds()
    {
        var overlay = new PixelBuffer(2, 1);
        overlay.SetPixel(0, 0, 1, 2, 3, 4);
        overlay.SetPixel(1, 0, 4, 5, 6, 7);
        var mask = new byte[] { 255, 255 };

        CompositeRenderer.RenderMaskOverlayInto(overlay, mask, 2, 1, Color.FromRgb(255, 0, 0),
            new PixelBounds(0, 0, 1, 1), maxAlpha: 128);

        overlay.GetRgb(0, 0, out var updatedR, out var updatedG, out var updatedB);
        Assert.Equal((255, 0, 0), (updatedR, updatedG, updatedB));
        Assert.Equal((byte)128, overlay.GetAlpha(0, 0));
        overlay.GetRgb(1, 0, out var cachedR, out var cachedG, out var cachedB);
        Assert.Equal((4, 5, 6), (cachedR, cachedG, cachedB));
        Assert.Equal((byte)7, overlay.GetAlpha(1, 0));
    }
}

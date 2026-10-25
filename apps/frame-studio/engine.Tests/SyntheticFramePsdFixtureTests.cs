using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class SyntheticFramePsdFixtureTests
{
    [Fact]
    public void ReaderRecognizesSyntheticTargetBoundsAndMaskClippingEvidenceWithoutEnablingPsdWrites()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "synthetic_frame_targets.psd");

        var document = new PsdDocumentReader().Read(fixturePath);
        var layers = Flatten(document.Layers).ToArray();

        Assert.Equal(24, document.Width);
        Assert.Equal(16, document.Height);
        Assert.NotNull(document.CompositePreview);
        Assert.Equal(2, document.PhotoSlots.Count);
        Assert.False(document.CanSaveLayeredPsd);

        AssertSlot(FrameSlotSizeHint.XL, new FrameBounds(3, 4, 8, 7));
        AssertSlot(FrameSlotSizeHint.S, new FrameBounds(15, 5, 5, 5));

        void AssertSlot(FrameSlotSizeHint size, FrameBounds expectedBounds)
        {
            var slot = Assert.Single(document.PhotoSlots, candidate => candidate.SizeHint == size);
            Assert.Equal(expectedBounds, slot.Bounds);

            var layer = Assert.Single(layers, candidate => candidate.Id == slot.LayerId);
            Assert.Equal(expectedBounds, layer.Bounds);
            Assert.True(layer.HasPixelData);
            Assert.True(layer.HasMask);
            Assert.True(layer.HasClipping);
            Assert.True(layer.Bounds.X >= 0 && layer.Bounds.Y >= 0);
            Assert.True(layer.Bounds.X + layer.Bounds.Width <= document.Width);
            Assert.True(layer.Bounds.Y + layer.Bounds.Height <= document.Height);
        }
    }

    [Fact]
    public async Task MetadataFlagsAloneDoNotCreatePlacementReadyPhotoOrLogoSlots()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "synthetic_frame_targets.psd");
        var result = await new PsdTemplateEngineAdapter().InspectAsync(fixturePath);

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(result.Slots, slot =>
            (slot.Type is FramePsdSlotType.Photo or FramePsdSlotType.Logo) && slot.IsPlacementReady);
        Assert.DoesNotContain(result.Slots, slot => slot.Type == FramePsdSlotType.Photo);
        Assert.False(result.EngineCapabilities!.CanWriteLayeredPsd);
    }

    private static IEnumerable<FramePsdLayer> Flatten(IEnumerable<FramePsdLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }
}

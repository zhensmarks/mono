using FrameStudio.App.Models;
using FrameStudio.App.Services;
using SkiaSharp;
using Xunit;

namespace FrameStudio.Tests;

public sealed class RasterPngComposerMaskTests
{
    [Fact]
    public void ExactBinaryApertureChangesOnlyItsVisiblePixels()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-mask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var photoPath = Path.Combine(root, "photo.png");
        try
        {
            var backgroundColor = new SKColor(20, 40, 180);
            var photoColor = new SKColor(230, 40, 30);
            var background = CreateSolidPng(8, 7, backgroundColor);
            File.WriteAllBytes(photoPath, CreateSolidPng(4, 3, photoColor));
            var mask = new byte[4 * 3];
            mask[1 * 4 + 1] = 255;
            mask[2 * 4 + 2] = 255;
            var slot = new FrameSlot(
                "photo",
                "Photo",
                SlotKind.Photo,
                "synthetic exact mask",
                bounds: new FrameSlotBounds(2, 2, 4, 3),
                isPlacementReady: true,
                apertureMask: mask)
            {
                Photo = new SourcePhoto(photoPath, null)
            };

            var output = new RasterPngComposer().Compose(background, new[] { slot });
            using var composed = SKBitmap.Decode(output);

            Assert.NotNull(composed);
            var changed = 0;
            var changedOutsideMask = 0;
            var changedOutsideRoi = 0;
            for (var y = 0; y < composed!.Height; y++)
            for (var x = 0; x < composed.Width; x++)
            {
                var actual = composed.GetPixel(x, y);
                var inRoi = x >= 2 && x < 6 && y >= 2 && y < 5;
                var maskIndex = inRoi ? (y - 2) * 4 + (x - 2) : -1;
                var shouldChange = inRoi && mask[maskIndex] != 0;
                if (actual != backgroundColor)
                    changed++;
                if (!inRoi && actual != backgroundColor)
                    changedOutsideRoi++;
                if (inRoi && !shouldChange && actual != backgroundColor)
                    changedOutsideMask++;
                Assert.Equal(shouldChange ? photoColor : backgroundColor, actual);
            }

            Assert.Equal(2, changed);
            Assert.Equal(0, changedOutsideMask);
            Assert.Equal(0, changedOutsideRoi);
            Assert.True(slot.HasTrustedApertureMask);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(SlotKind.Photo)]
    [InlineData(SlotKind.Logo)]
    public void PhotoAndLogoBothUseTheExactAperture(SlotKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-logo-mask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var photoPath = Path.Combine(root, "source.png");
        try
        {
            var backgroundColor = new SKColor(20, 40, 180);
            var photoColor = new SKColor(230, 40, 30);
            File.WriteAllBytes(photoPath, CreateSolidPng(3, 3, photoColor));
            var mask = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
            var slot = new FrameSlot(
                "image",
                "Image",
                kind,
                "synthetic image aperture",
                bounds: new FrameSlotBounds(1, 1, 3, 3),
                isPlacementReady: true,
                apertureMask: mask)
            {
                Photo = new SourcePhoto(photoPath, null)
            };
            var background = CreateSolidPng(5, 5, backgroundColor);

            using var output = SKBitmap.Decode(new RasterPngComposer().Compose(background, new[] { slot }));

            Assert.NotNull(output);
            for (var y = 0; y < 5; y++)
            for (var x = 0; x < 5; x++)
            {
                var inSlot = x is >= 1 and < 4 && y is >= 1 and < 4;
                var localIndex = inSlot ? (y - 1) * 3 + (x - 1) : -1;
                var shouldChange = inSlot && mask[localIndex] == 255;
                Assert.Equal(shouldChange ? photoColor : backgroundColor, output!.GetPixel(x, y));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssignedImageWithoutExactApertureFailsClosed()
    {
        var background = CreateSolidPng(4, 4, new SKColor(10, 20, 30));
        var slot = new FrameSlot(
            "photo",
            "Photo",
            SlotKind.Photo,
            "metadata-only mask flag",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true)
        {
            Photo = new SourcePhoto("unused.png", null)
        };

        var error = Assert.Throws<InvalidOperationException>(() => new RasterPngComposer().Compose(background, new[] { slot }));

        Assert.Contains("per-pixel aperture mask", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(slot.HasTrustedApertureMask);
    }

    [Fact]
    public void ApertureDimensionsMustMatchSlotBounds()
    {
        var error = Assert.Throws<ArgumentException>(() => new FrameSlot(
            "photo",
            "Photo",
            SlotKind.Photo,
            "invalid synthetic mask",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true,
            apertureMask: [255, 0, 255]));

        Assert.Contains("one alpha sample for every slot pixel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SoftMaskFailsClosedAndSourceArrayCannotMutateTheStoredMap()
    {
        var error = Assert.Throws<ArgumentException>(() => new FrameSlot(
            "photo",
            "Photo",
            SlotKind.Photo,
            "synthetic soft mask",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true,
            apertureMask: [255, 128, 0, 255]));
        Assert.Contains("soft or antialiased mask edges are not supported", error.Message, StringComparison.OrdinalIgnoreCase);

        var sourceMask = new byte[] { 255, 0, 0, 255 };
        var slot = new FrameSlot(
            "photo",
            "Photo",
            SlotKind.Photo,
            "immutable synthetic map",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true,
            apertureMask: sourceMask);
        sourceMask[0] = 0;

        Assert.True(slot.HasTrustedApertureMask);
        Assert.Equal((byte)255, slot.ApertureMask.Span[0]);
    }

    [Fact]
    public void ManualFrameRoundTripsToSeparatePngCopyWithoutChangingMaster()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-manual-frame-{Guid.NewGuid():N}");
        var outputFolder = Path.Combine(root, "output");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outputFolder);
        var photoPath = Path.Combine(root, "photo.png");
        var masterPath = Path.Combine(root, "disposable-master.psd");
        var masterBytes = new byte[] { 0x38, 0x42, 0x50, 0x53, 0x01, 0x02, 0x03, 0x04 };
        try
        {
            var backgroundColor = new SKColor(18, 36, 144);
            var photoColor = new SKColor(238, 82, 34);
            File.WriteAllBytes(masterPath, masterBytes);
            File.WriteAllBytes(photoPath, CreateSolidPng(2, 2, photoColor));
            var frame = new FrameSlot(
                "manual-frame-1",
                "Frame manual 1",
                SlotKind.Photo,
                "synthetic manual rectangle",
                bounds: new FrameSlotBounds(2, 3, 3, 2),
                isPlacementReady: true,
                apertureMask: Enumerable.Repeat((byte)255, 3 * 2).ToArray(),
                isManualFrame: true)
            {
                Photo = new SourcePhoto(photoPath, null)
            };
            var background = CreateSolidPng(8, 8, backgroundColor);
            var composedPng = new RasterPngComposer().Compose(background, new[] { frame });
            var outputPath = new PngOutputWriter().WriteNewPng(outputFolder, "disposable-master-manual-frame", composedPng, masterPath);

            using var output = SKBitmap.Decode(outputPath);

            Assert.NotNull(output);
            Assert.NotEqual(masterPath, outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(masterBytes, File.ReadAllBytes(masterPath));
            Assert.True(frame.IsManualFrame);
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var inside = x >= 2 && x < 5 && y >= 3 && y < 5;
                Assert.Equal(inside ? photoColor : backgroundColor, output!.GetPixel(x, y));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManualRectangleRejectsNonRectangleBinaryAperture()
    {
        var error = Assert.Throws<ArgumentException>(() => new FrameSlot(
            "manual-frame",
            "Manual frame",
            SlotKind.Photo,
            "invalid manual rectangle",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true,
            apertureMask: [255, 0, 255, 255],
            isManualFrame: true));

        Assert.Contains("fully opaque photo rectangles", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyBinaryApertureIsNotTrusted()
    {
        var slot = new FrameSlot(
            "photo",
            "Photo",
            SlotKind.Photo,
            "empty synthetic map",
            bounds: new FrameSlotBounds(0, 0, 2, 2),
            isPlacementReady: true,
            apertureMask: [0, 0, 0, 0]);

        Assert.False(slot.HasTrustedApertureMask);
    }

    [Fact]
    public void TransformedApertureRoundTripsThroughSeparatePngCopyWithoutChangingMaster()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-roundtrip-{Guid.NewGuid():N}");
        var outputFolder = Path.Combine(root, "output");
        Directory.CreateDirectory(outputFolder);
        var masterPath = Path.Combine(root, "master.psd");
        var photoPath = Path.Combine(root, "synthetic-photo.png");
        var masterBytes = new byte[] { 0x38, 0x42, 0x50, 0x53, 1, 2, 3, 4 };
        var backgroundColor = new SKColor(16, 48, 160);
        var photoColor = new SKColor(224, 72, 24);
        var background = CreateSolidPng(8, 7, backgroundColor);
        var mask = new byte[]
        {
            0, 0, 255, 0,
            0, 255, 255, 0,
            255, 255, 255, 0
        };

        try
        {
            File.WriteAllBytes(masterPath, masterBytes);
            File.WriteAllBytes(photoPath, CreateSolidPng(10, 8, photoColor));
            var slot = new FrameSlot(
                "photo",
                "Photo",
                SlotKind.Photo,
                "synthetic transformed aperture",
                bounds: new FrameSlotBounds(2, 2, 4, 3),
                isPlacementReady: true,
                apertureMask: mask)
            {
                Photo = new SourcePhoto(photoPath, null),
                Zoom = 1.4,
                Rotation = 27,
                OffsetX = 0.3,
                OffsetY = -0.2
            };

            var composer = new RasterPngComposer();
            var composedBytes = composer.Compose(background, new[] { slot });
            var maskBytes = composer.CreateApertureMaskPng(slot);
            var outputPath = new PngOutputWriter().WriteNewPng(outputFolder, "master", composedBytes, masterPath);

            Assert.NotEqual(masterPath, outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(masterBytes, File.ReadAllBytes(masterPath));

            using var reopened = SKBitmap.Decode(outputPath);
            using var reopenedMask = SKBitmap.Decode(maskBytes);
            Assert.NotNull(reopened);
            Assert.NotNull(reopenedMask);
            Assert.Equal(8, reopened!.Width);
            Assert.Equal(7, reopened.Height);
            Assert.Equal(4, reopenedMask!.Width);
            Assert.Equal(3, reopenedMask.Height);

            var changedInsideMask = 0;
            for (var y = 0; y < reopened.Height; y++)
            for (var x = 0; x < reopened.Width; x++)
            {
                var inSlot = x >= 2 && x < 6 && y >= 2 && y < 5;
                var localX = inSlot ? x - 2 : -1;
                var localY = inSlot ? y - 2 : -1;
                var shouldBeMasked = inSlot && mask[localY * 4 + localX] == 255;
                var actual = reopened.GetPixel(x, y);
                if (!shouldBeMasked)
                    Assert.Equal(backgroundColor, actual);
                else if (actual != backgroundColor)
                    changedInsideMask++;
            }

            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 4; x++)
                Assert.Equal(mask[y * 4 + x], reopenedMask.GetPixel(x, y).Alpha);

            Assert.True(changedInsideMask > 0);
            Assert.Equal(masterBytes, File.ReadAllBytes(masterPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreateSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }
}

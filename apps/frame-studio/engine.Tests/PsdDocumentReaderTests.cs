using System.Buffers.Binary;
using System.IO.Compression;
using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdDocumentReaderTests
{
    private static readonly byte[] ExpectedRgba =
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255
    ];

    [Fact]
    public void ReadsSyntheticPsdDocumentLayerSlotAndMergedComposite()
    {
        using var input = new MemoryStream(SyntheticPsdFixture.CreateRgbWithPhotoLayer(), writable: false);
        var document = new PsdDocumentReader().Read(input, "sample.psd");

        Assert.Equal("sample.psd", document.SourceName);
        Assert.Equal(2, document.Width);
        Assert.Equal(2, document.Height);
        Assert.Equal((ushort)3, document.ChannelCount);
        Assert.Equal((byte)8, document.BitDepth);
        Assert.Equal("Rgb", document.ColorMode);
        Assert.Single(document.Layers);
        Assert.True(document.Layers[0].IsVisible);
        Assert.Equal("Photo XL", document.Layers[0].Name);
        Assert.Equal(new FrameBounds(0, 0, 2, 2), document.Layers[0].Bounds);
        Assert.True(document.Layers[0].HasPixelData);
        Assert.Single(document.PhotoSlots);
        Assert.Equal(FrameSlotSizeHint.XL, document.PhotoSlots[0].SizeHint);
        Assert.Equal(document.Layers[0].Id, document.PhotoSlots[0].LayerId);
        Assert.NotNull(document.CompositePreview);
        Assert.Equal(ExpectedRgba, document.CompositePreview!.RgbaPixels.ToArray());
        Assert.Empty(document.Warnings);
        Assert.False(document.CanSaveLayeredPsd);
        Assert.True(document.CanExportFlattenedPng);
    }

    [Fact]
    public void WritesValidFlattenedRgbaPng()
    {
        using var input = new MemoryStream(SyntheticPsdFixture.CreateRgbWithPhotoLayer(), writable: false);
        var document = new PsdDocumentReader().Read(input);
        using var png = new MemoryStream();

        document.WriteCompositePreviewPng(png);

        var bytes = png.ToArray();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(ExpectedRgbaWithPngRowFilters(), InflatePngIdat(bytes));
    }

    [Fact]
    public void FileExportNeverOverwritesAnExistingOutput()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"frame-studio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var sourcePath = Path.Combine(tempDirectory, "source.psd");
            var outputPath = Path.Combine(tempDirectory, "preview.png");
            File.WriteAllBytes(sourcePath, SyntheticPsdFixture.CreateRgbWithPhotoLayer());
            var document = new PsdDocumentReader().Read(sourcePath);
            var existing = new byte[] { 9, 8, 7 };
            File.WriteAllBytes(outputPath, existing);

            Assert.Throws<IOException>(() => document.SaveCompositePreviewPng(outputPath));
            Assert.Equal(existing, File.ReadAllBytes(outputPath));
            Assert.Throws<ArgumentException>(() => document.SaveCompositePreviewPng(sourcePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("School S", null)]
    [InlineData("S", FrameSlotSizeHint.S)]
    [InlineData("[Slot:XL]", FrameSlotSizeHint.XL)]
    [InlineData("Foto portrait", FrameSlotSizeHint.Unspecified)]
    public void SlotHintsAreConservativeAndNameBased(string name, FrameSlotSizeHint? expected)
    {
        using var input = new MemoryStream(SyntheticPsdFixture.CreateRgbWithPhotoLayer(name), writable: false);
        var document = new PsdDocumentReader().Read(input);

        Assert.Equal(expected.HasValue ? 1 : 0, document.PhotoSlots.Count);
        if (expected.HasValue)
        {
            Assert.Equal(expected.Value, document.PhotoSlots[0].SizeHint);
        }
    }

    private static byte[] ExpectedRgbaWithPngRowFilters()
    {
        return
        [
            0, 255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 0, 255, 255, 255, 255, 255, 255
        ];
    }

    private static byte[] InflatePngIdat(byte[] png)
    {
        using var compressed = new MemoryStream();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            offset += 4;
            var type = System.Text.Encoding.ASCII.GetString(png, offset, 4);
            offset += 4;
            if (type == "IDAT")
            {
                compressed.Write(png, offset, length);
            }
            offset += length + 4; // payload and CRC
            if (type == "IEND")
            {
                break;
            }
        }

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        return raw.ToArray();
    }
}

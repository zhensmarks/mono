using System.Buffers.Binary;
using System.IO.Compression;

namespace FrameStudio.Engine;

internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteRgba32(Stream output, int width, int height, ReadOnlySpan<byte> rgbaPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        }

        var expectedLength = checked((long)width * height * 4);
        if (expectedLength != rgbaPixels.Length)
        {
            throw new ArgumentException("RGBA pixel buffer length does not match the image dimensions.", nameof(rgbaPixels));
        }

        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)height);
        header[8] = 8; // bit depth
        header[9] = 6; // truecolor with alpha
        header[10] = 0; // compression method
        header[11] = 0; // filter method
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var rowLength = checked(width * 4);
            var scanline = new byte[checked(rowLength + 1)];
            for (var y = 0; y < height; y++)
            {
                scanline[0] = 0; // PNG filter: None
                rgbaPixels.Slice(y * rowLength, rowLength).CopyTo(scanline.AsSpan(1));
                zlib.Write(scanline);
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = uint.MaxValue;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ~crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc;
    }
}

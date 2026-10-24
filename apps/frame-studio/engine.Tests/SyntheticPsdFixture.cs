using System.Buffers.Binary;
using System.Text;

namespace FrameStudio.Engine.Tests;

internal static class SyntheticPsdFixture
{
    private static readonly byte[] RedPlane = [255, 0, 0, 255];
    private static readonly byte[] GreenPlane = [0, 255, 0, 255];
    private static readonly byte[] BluePlane = [0, 0, 255, 255];

    public static byte[] CreateRgbWithPhotoLayer(string layerName = "Photo XL", bool clipped = false)
    {
        using var output = new MemoryStream();
        WriteAscii(output, "8BPS");
        WriteUInt16(output, 1); // PSD version
        output.Write(new byte[6]);
        WriteUInt16(output, 3); // RGB channels
        WriteUInt32(output, 2); // height
        WriteUInt32(output, 2); // width
        WriteUInt16(output, 8); // bit depth
        WriteUInt16(output, 3); // RGB mode
        WriteUInt32(output, 0); // color mode data
        WriteUInt32(output, 0); // image resources

        using var layerInfo = new MemoryStream();
        WriteUInt16(layerInfo, 1); // one layer
        WriteLayerRecord(layerInfo, layerName, clipped);
        WriteLayerChannelData(layerInfo, RedPlane);
        WriteLayerChannelData(layerInfo, GreenPlane);
        WriteLayerChannelData(layerInfo, BluePlane);

        var layerAndMaskLength = checked((uint)(4 + layerInfo.Length + 4));
        WriteUInt32(output, layerAndMaskLength);
        WriteUInt32(output, checked((uint)layerInfo.Length));
        layerInfo.Position = 0;
        layerInfo.CopyTo(output);
        WriteUInt32(output, 0); // empty global layer mask

        WriteUInt16(output, 0); // raw composite image compression
        output.Write(RedPlane);
        output.Write(GreenPlane);
        output.Write(BluePlane);
        return output.ToArray();
    }

    private static void WriteLayerRecord(Stream output, string layerName, bool clipped)
    {
        WriteInt32(output, 0); // top
        WriteInt32(output, 0); // left
        WriteInt32(output, 2); // bottom (exclusive)
        WriteInt32(output, 2); // right (exclusive)
        WriteUInt16(output, 3);

        for (short channelId = 0; channelId < 3; channelId++)
        {
            WriteUInt16(output, unchecked((ushort)channelId));
            WriteUInt32(output, 6); // 2-byte raw compression marker + 4 pixel bytes
        }

        WriteAscii(output, "8BIM");
        WriteAscii(output, "norm");
        output.WriteByte(255); // opacity
        output.WriteByte((byte)(clipped ? 1 : 0)); // clipping
        output.WriteByte(0x02); // bit 1: visible
        output.WriteByte(0);   // filler

        var encodedName = Encoding.Latin1.GetBytes(layerName);
        if (encodedName.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(layerName));
        }

        var pascalLength = 1 + encodedName.Length;
        var padding = (4 - (pascalLength % 4)) % 4;
        using var extraData = new MemoryStream();
        WriteUInt32(extraData, 0); // no layer mask
        WriteUInt32(extraData, 0); // no blending ranges
        extraData.WriteByte((byte)encodedName.Length);
        extraData.Write(encodedName);
        extraData.Write(new byte[padding]);

        WriteUInt32(output, checked((uint)extraData.Length));
        extraData.Position = 0;
        extraData.CopyTo(output);
    }

    private static void WriteLayerChannelData(Stream output, byte[] pixels)
    {
        WriteUInt16(output, 0); // raw channel compression
        output.Write(pixels);
    }

    private static void WriteAscii(Stream output, string value) => output.Write(Encoding.ASCII.GetBytes(value));

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteInt32(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
}

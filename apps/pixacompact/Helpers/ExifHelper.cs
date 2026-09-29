using System;
using System.IO;

namespace PixelcutCompact.Helpers;

public static class ExifHelper
{
    public static int GetOrientation(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);
            
            // Check for JPEG (0xFFD8)
            if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8) return 1;

            while (fs.Position < fs.Length)
            {
                byte marker = reader.ReadByte();
                if (marker != 0xFF) return 1;
                
                byte type = reader.ReadByte();
                if (type == 0xE1) // APP1 (Exif)
                {
                    return ReadExifOrientation(reader);
                }
                
                // Skip other markers
                int length = (reader.ReadByte() << 8) | reader.ReadByte();
                fs.Seek(length - 2, SeekOrigin.Current);
            }
        }
        catch { }
        return 1;
    }

    private static int ReadExifOrientation(BinaryReader reader)
    {
        long startPos = reader.BaseStream.Position;
        int length = (reader.ReadByte() << 8) | reader.ReadByte();
        
        // "Exif\0\0"
        if (reader.ReadByte() != 'E' || reader.ReadByte() != 'x' || 
            reader.ReadByte() != 'i' || reader.ReadByte() != 'f' || 
            reader.ReadByte() != 0 || reader.ReadByte() != 0) return 1;

        // TIFF Header
        long tiffStart = reader.BaseStream.Position;
        bool littleEndian = reader.ReadByte() == 'I' && reader.ReadByte() == 'I';
        reader.ReadByte(); reader.ReadByte(); // 42 (0x002A)

        int offset = ReadInt32(reader, littleEndian); // IFD offset
        reader.BaseStream.Seek(tiffStart + offset, SeekOrigin.Begin);

        int entries = ReadUInt16(reader, littleEndian);
        
        for (int i = 0; i < entries; i++)
        {
            int tag = ReadUInt16(reader, littleEndian);
            if (tag == 274) // Orientation
            {
                reader.ReadUInt16(); // Type (3=short)
                reader.ReadInt32(); // Count
                // Value is usually packed in the offset field for shorts
                return ReadUInt16(reader, littleEndian);
            }
            reader.BaseStream.Seek(10, SeekOrigin.Current); // Skip rest of entry
        }
        
        return 1;
    }

    private static short ReadUInt16(BinaryReader reader, bool littleEndian)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        return littleEndian ? (short)(b1 | (b2 << 8)) : (short)((b1 << 8) | b2);
    }
    
    private static int ReadInt32(BinaryReader reader, bool littleEndian)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();
        byte b4 = reader.ReadByte();
        return littleEndian ? 
            (b1 | (b2 << 8) | (b3 << 16) | (b4 << 24)) : 
            ((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);
    }
}

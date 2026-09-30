using System;

namespace PixelcutCompact.Services.Editing;

/// <summary>A clipped pixel rectangle used to limit mask and preview work.</summary>
public readonly struct PixelBounds
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelBounds(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    public static PixelBounds Empty => default;

    public static PixelBounds Full(int width, int height)
        => width <= 0 || height <= 0 ? Empty : new PixelBounds(0, 0, width, height);

    public PixelBounds Union(PixelBounds other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        long left = Math.Min((long)X, other.X);
        long top = Math.Min((long)Y, other.Y);
        long right = Math.Max((long)X + Width, (long)other.X + other.Width);
        long bottom = Math.Max((long)Y + Height, (long)other.Y + other.Height);
        return new PixelBounds((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }

    public PixelBounds ClampTo(int width, int height)
    {
        if (IsEmpty || width <= 0 || height <= 0) return Empty;
        long left = Math.Clamp((long)X, 0, width);
        long top = Math.Clamp((long)Y, 0, height);
        long right = Math.Clamp((long)X + Width, 0, width);
        long bottom = Math.Clamp((long)Y + Height, 0, height);
        if (right <= left || bottom <= top) return Empty;
        return new PixelBounds((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }
}

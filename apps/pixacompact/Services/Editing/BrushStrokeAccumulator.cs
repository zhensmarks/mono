using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Sparse per-stroke flow accumulation. Only touched 128x128 tiles allocate
/// memory, rather than one byte for every pixel in the image.
/// </summary>
public sealed class BrushStrokeAccumulator
{
    public const int TileSize = 128;
    private readonly int _tilesAcross;
    private readonly int _tilesDown;
    private readonly Dictionary<long, byte[]> _tiles = new();

    public int Width { get; }
    public int Height { get; }
    public int AllocatedTileCount => _tiles.Count;
    public long AllocatedBytes => (long)_tiles.Count * TileSize * TileSize;

    public BrushStrokeAccumulator(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _tilesAcross = (int)(((long)width + TileSize - 1) / TileSize);
        _tilesDown = (int)(((long)height + TileSize - 1) / TileSize);
    }

    internal byte[] GetOrCreateTile(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)_tilesAcross)
            throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)tileY >= (uint)_tilesDown)
            throw new ArgumentOutOfRangeException(nameof(tileY));

        long key = (long)tileY * _tilesAcross + tileX;
        if (_tiles.TryGetValue(key, out var tile)) return tile;
        tile = new byte[TileSize * TileSize];
        _tiles.Add(key, tile);
        return tile;
    }
}

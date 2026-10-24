namespace FrameStudio.Engine;

/// <summary>A PSD layer's document-space pixel bounds.</summary>
public readonly record struct FrameBounds(int X, int Y, int Width, int Height);

/// <summary>Best-effort size hint inferred from a layer name, not a confirmed editable slot.</summary>
public enum FrameSlotSizeHint
{
    Unspecified,
    XL,
    S
}

/// <summary>A read-only layer node. Bounds and slot hints are descriptive only.</summary>
public sealed record FramePsdLayer(
    string Id,
    string Name,
    bool IsVisible,
    byte Opacity,
    FrameBounds Bounds,
    bool HasPixelData,
    FrameSlotSizeHint? SlotHint,
    IReadOnlyList<FramePsdLayer> Children)
{
    /// <summary>Best-effort source layer kind; this does not make the layer editable.</summary>
    public string Kind { get; init; } = "unknown";

    public bool HasMask { get; init; }
    public bool HasVectorMask { get; init; }
    public bool HasEffects { get; init; }
}

/// <summary>Diagnostics for an explicitly configured optional PSD reader backend.</summary>
public sealed record FramePsdOptionalBackendCapabilities(
    string Name,
    string Version,
    string License,
    string RuntimeRequirement,
    string PlatformScope,
    bool IsConfigured,
    bool RuntimeVerified,
    bool CanAttemptPsdRead,
    bool CanAttemptPsbRead,
    bool CanInspectLayerTreeBestEffort,
    bool CanExportSupportedStoredCompositePng,
    bool CanWriteLayeredPsd,
    string Limitations);

/// <summary>A possible photo location inferred from a layer's name and rectangle.</summary>
public sealed record FramePhotoSlotCandidate(
    string LayerId,
    string LayerName,
    FrameSlotSizeHint SizeHint,
    FrameBounds Bounds);

/// <summary>8-bit-per-channel RGBA pixels from the PSD's stored merged composite.</summary>
public sealed class FrameCompositePreview
{
    private readonly byte[] _rgbaPixels;

    internal FrameCompositePreview(int width, int height, byte[] rgbaPixels)
    {
        Width = width;
        Height = height;
        _rgbaPixels = rgbaPixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> RgbaPixels => _rgbaPixels;
}

/// <summary>Read-only document metadata and its optional merged-composite preview.</summary>
public sealed class FramePsdDocument
{
    private readonly string? _sourcePath;

    internal FramePsdDocument(
        string sourceName,
        string? sourcePath,
        int width,
        int height,
        ushort channelCount,
        byte bitDepth,
        string colorMode,
        IReadOnlyList<FramePsdLayer> layers,
        IReadOnlyList<FramePhotoSlotCandidate> photoSlots,
        FrameCompositePreview? compositePreview,
        IReadOnlyList<string> warnings)
    {
        SourceName = sourceName;
        _sourcePath = sourcePath;
        Width = width;
        Height = height;
        ChannelCount = channelCount;
        BitDepth = bitDepth;
        ColorMode = colorMode;
        Layers = layers;
        PhotoSlots = photoSlots;
        CompositePreview = compositePreview;
        Warnings = warnings;
    }

    public string SourceName { get; }
    public int Width { get; }
    public int Height { get; }
    public ushort ChannelCount { get; }
    public byte BitDepth { get; }
    public string ColorMode { get; }
    public IReadOnlyList<FramePsdLayer> Layers { get; }
    public IReadOnlyList<FramePhotoSlotCandidate> PhotoSlots { get; }
    public FrameCompositePreview? CompositePreview { get; }
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>PsdSharp is read-only; this prototype cannot save editable PSD layers.</summary>
    public bool CanSaveLayeredPsd => false;

    public bool CanExportFlattenedPng => CompositePreview is not null;

    /// <summary>Writes a new PNG file. Existing files are never overwritten.</summary>
    public void SaveCompositePreviewPng(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (CompositePreview is null)
        {
            throw new InvalidOperationException("This document has no supported composite preview to export.");
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullOutputPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The flattened preview output path must use a .png extension.", nameof(outputPath));
        }

        if (_sourcePath is not null && PathsEqual(fullOutputPath, _sourcePath))
        {
            throw new InvalidOperationException("The preview cannot overwrite its source PSD/PSB file.");
        }

        using var output = new FileStream(fullOutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        PngEncoder.WriteRgba32(output, CompositePreview.Width, CompositePreview.Height, CompositePreview.RgbaPixels.Span);
    }

    /// <summary>Writes PNG bytes to a caller-owned writable stream.</summary>
    public void WriteCompositePreviewPng(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        }

        if (CompositePreview is null)
        {
            throw new InvalidOperationException("This document has no supported composite preview to export.");
        }

        PngEncoder.WriteRgba32(output, CompositePreview.Width, CompositePreview.Height, CompositePreview.RgbaPixels.Span);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}

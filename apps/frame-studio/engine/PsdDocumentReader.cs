using PsdSharp;
using PsdSharp.Images;
using PsdSharp.Images.DataConversion;
using PsdSharp.Parsing;

namespace FrameStudio.Engine;

/// <summary>Reads PSD/PSB structure and extracts the file's already-merged RGB composite.</summary>
public sealed class PsdDocumentReader
{
    private readonly long _maxPreviewBytes;
    private readonly long _maxInputFileBytes;
    private readonly PsdToolsDesktopFallbackReader? _fallbackReader;

    public PsdDocumentReader(
        long maxPreviewBytes = 256L * 1024 * 1024,
        PsdToolsDesktopFallbackReader? fallbackReader = null,
        long? maxInputFileBytes = null)
    {
        if (maxPreviewBytes < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPreviewBytes), "Preview limit must be at least one RGBA pixel.");
        }

        var inputLimit = maxInputFileBytes ?? fallbackReader?.MaxInputFileBytes ?? 512L * 1024 * 1024;
        if (inputLimit <= 0 || inputLimit > 4L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputFileBytes));
        }

        _maxPreviewBytes = maxPreviewBytes;
        _maxInputFileBytes = inputLimit;
        _fallbackReader = fallbackReader;
    }

    public FramePsdEngineCapabilities Capabilities => _fallbackReader is null
        ? FramePsdEngineCapabilities.Current
        : FramePsdEngineCapabilities.Current with { OptionalFallback = _fallbackReader.Capabilities };

    public FramePsdDocument Read(string path)
    {
        return Read(path, CancellationToken.None);
    }

    public FramePsdDocument Read(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > _maxInputFileBytes)
        {
            throw new FramePsdInputLimitException();
        }

        return ReadCore(input, Path.GetFileName(fullPath), fullPath, cancellationToken);
    }

    /// <summary>The stream must be readable and seekable. The reader leaves it open.</summary>
    public FramePsdDocument Read(Stream input, string sourceName = "Untitled.psd")
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("PSD input must be a readable, seekable stream.", nameof(input));
        }

        if (input.Length - input.Position > _maxInputFileBytes)
        {
            throw new FramePsdInputLimitException();
        }

        return ReadCore(input, string.IsNullOrWhiteSpace(sourceName) ? "Untitled.psd" : sourceName, null, CancellationToken.None);
    }

    private FramePsdDocument ReadCore(Stream input, string sourceName, string? sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            return ReadWithPsdSharp(input, sourceName, sourcePath);
        }
        catch (Exception primaryException) when (
            _fallbackReader is not null
            && sourcePath is not null
            && primaryException is not OutOfMemoryException and not StackOverflowException and not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return _fallbackReader.Read(sourcePath, sourceName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception fallbackException) when (fallbackException is not OutOfMemoryException and not StackOverflowException)
            {
                throw new FramePsdReaderFallbackException(primaryException, fallbackException);
            }
        }
    }

    private FramePsdDocument ReadWithPsdSharp(Stream input, string sourceName, string? sourcePath)
    {
        var psd = PsdFile.Open(input, new PsdLoadOptions
        {
            LeaveInputOpen = true,
            ImageDataLoading = ImageDataLoading.Auto
        });

        var width = checked((int)psd.Header.WidthInPixels);
        var height = checked((int)psd.Header.HeightInPixels);
        var warnings = new List<string>();
        var layers = psd.RootLayers
            .Select((layer, index) => MapLayer(layer, $"layer/{index}"))
            .ToArray();
        var slots = Flatten(layers)
            .Where(layer => layer.SlotHint.HasValue)
            .Select(layer => new FramePhotoSlotCandidate(
                layer.Id,
                layer.Name,
                layer.SlotHint!.Value,
                layer.Bounds))
            .ToArray();

        var preview = TryReadCompositePreview(psd, width, height, warnings);
        return new FramePsdDocument(
            sourceName,
            sourcePath,
            width,
            height,
            psd.Header.NumberOfChannels,
            psd.Header.ChannelDepth,
            psd.Header.ColorMode.ToString(),
            Array.AsReadOnly(layers),
            Array.AsReadOnly(slots),
            preview,
            Array.AsReadOnly(warnings.ToArray()));
    }

    private FrameCompositePreview? TryReadCompositePreview(PsdFile psd, int width, int height, List<string> warnings)
    {
        if (psd.ImageData is null)
        {
            warnings.Add("The PSD reader did not provide merged image data; no preview was produced.");
            return null;
        }

        // PsdSharp 1.2.0's shared converter treats CMYK planes as RGB. Keep output color-safe by
        // exporting only RGB files with the expected 3 color channels and optional alpha channel.
        if (psd.Header.ColorMode != ColorMode.Rgb)
        {
            warnings.Add($"A flattened preview is not emitted for {psd.Header.ColorMode}; this prototype only exports RGB composites to avoid misleading color conversion.");
            return null;
        }

        if (psd.Header.NumberOfChannels is not (3 or 4))
        {
            warnings.Add($"A flattened preview is not emitted for an RGB composite with {psd.Header.NumberOfChannels} channels; only 3-channel RGB and 4-channel RGBA are accepted.");
            return null;
        }

        if (psd.Header.ChannelDepth is not (8 or 16 or 32))
        {
            warnings.Add($"A flattened preview is not emitted for {psd.Header.ChannelDepth}-bit channels; supported preview depths are 8, 16, and 32 bits.");
            return null;
        }

        var pixelCount = (long)width * height;
        var outputBytes = pixelCount * 4;
        if (pixelCount <= 0 || outputBytes > _maxPreviewBytes || outputBytes > int.MaxValue)
        {
            warnings.Add($"A flattened preview is not emitted because its RGBA buffer would exceed the configured {_maxPreviewBytes:N0}-byte limit.");
            return null;
        }

        try
        {
            var rgba = PixelDataConverter.GetInterleavedBuffer(psd.ImageData, ColorType.Rgba8888);
            if (rgba.Length != outputBytes)
            {
                warnings.Add("The PSD reader returned an unexpected composite buffer length; no preview was produced.");
                return null;
            }

            return new FrameCompositePreview(width, height, rgba);
        }
        catch (NotSupportedException exception)
        {
            warnings.Add($"A flattened preview is not available: {exception.Message}");
            return null;
        }
    }

    private static FramePsdLayer MapLayer(Layer layer, string id)
    {
        var children = layer.Children
            .Select((child, index) => MapLayer(child, $"{id}/{index}"))
            .ToArray();
        var bounds = ToBounds(layer.Bounds);
        var slotHint = SlotNameClassifier.Classify(layer.Name);

        return new FramePsdLayer(
            id,
            layer.Name,
            layer.ShouldBeVisible,
            layer.Opacity,
            bounds,
            layer.ImageData is not null && bounds.Width > 0 && bounds.Height > 0,
            slotHint,
            Array.AsReadOnly(children));
    }

    private static FrameBounds ToBounds(Rectangle rectangle)
    {
        var x = rectangle.TopLeft.X;
        var y = rectangle.TopLeft.Y;
        var width = Math.Max(0L, (long)rectangle.BottomRight.X - x + 1);
        var height = Math.Max(0L, (long)rectangle.BottomRight.Y - y + 1);
        return new FrameBounds(x, y, checked((int)Math.Min(width, int.MaxValue)), checked((int)Math.Min(height, int.MaxValue)));
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

internal static class SlotNameClassifier
{
    private static readonly HashSet<string> PhotoMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "photo", "foto", "portrait", "slot"
    };

    public static FrameSlotSizeHint? Classify(string? layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return null;
        }

        var tokens = layerName
            .Split(new[] { ' ', '\t', '\r', '\n', '-', '_', '[', ']', '(', ')', ':', '/', '\\', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();

        var hasMarker = tokens.Any(PhotoMarkers.Contains);
        if (!hasMarker && tokens.Length == 1 && tokens[0] == "xl")
        {
            return FrameSlotSizeHint.XL;
        }

        if (!hasMarker && tokens.Length == 1 && tokens[0] == "s")
        {
            return FrameSlotSizeHint.S;
        }

        if (!hasMarker)
        {
            return null;
        }

        if (tokens.Contains("xl", StringComparer.OrdinalIgnoreCase))
        {
            return FrameSlotSizeHint.XL;
        }

        if (tokens.Contains("s", StringComparer.OrdinalIgnoreCase))
        {
            return FrameSlotSizeHint.S;
        }

        return FrameSlotSizeHint.Unspecified;
    }
}

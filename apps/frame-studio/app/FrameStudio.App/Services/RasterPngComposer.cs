using FrameStudio.App.Models;
using SkiaSharp;

namespace FrameStudio.App.Services;

/// <summary>
/// Composes local slot overlays over the PSD's stored merged composite using verified binary aperture maps.
/// It never writes to layered PSD data.
/// </summary>
public sealed class RasterPngComposer
{
    public const long MaximumPixels = 30_000_000;

    public byte[] Compose(byte[] storedCompositePng, IEnumerable<FrameSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(storedCompositePng);
        ArgumentNullException.ThrowIfNull(slots);

        using var pngData = SKData.CreateCopy(storedCompositePng);
        using var codec = SKCodec.Create(pngData)
            ?? throw new InvalidDataException("The stored composite is not a readable PNG image.");
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
            throw new InvalidOperationException($"The artboard exceeds the safe raster limit of {MaximumPixels:N0} pixels.");

        using var background = SKBitmap.Decode(storedCompositePng)
            ?? throw new InvalidDataException("The stored composite is not a readable PNG image.");

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Could not allocate the output raster surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(background, new SKRect(0, 0, width, height));

        foreach (var slot in slots)
        {
            var isImageSlot = slot.IsPhoto || slot.IsLogo;
            if (isImageSlot && slot.Photo is not null && !slot.HasTrustedApertureMask)
                throw new InvalidOperationException("An exact per-pixel aperture mask is required before an image slot can be rendered.");

            if (isImageSlot && !slot.HasTrustedApertureMask)
                continue;

            if (slot.Bounds is not { Width: > 0, Height: > 0 } bounds)
                continue;

            var left = Math.Max(0L, bounds.X);
            var top = Math.Max(0L, bounds.Y);
            var right = Math.Min((long)width, (long)bounds.X + bounds.Width);
            var bottom = Math.Min((long)height, (long)bounds.Y + bounds.Height);
            if (right <= left || bottom <= top)
                continue;

            var destination = new SKRect((float)left, (float)top, (float)right, (float)bottom);
            if (isImageSlot && slot.Photo is { } photo)
                DrawMaskedImageSlot(canvas, slot, photo.Path, destination);
            else if (slot.IsText && !string.IsNullOrWhiteSpace(slot.Value))
                DrawTextSlot(canvas, slot, slot.Value, destination);
        }

        using var outputImage = surface.Snapshot();
        using var encoded = outputImage.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The raster compositor could not encode a PNG output.");
        return encoded.ToArray();
    }

    public byte[] CreateApertureMaskPng(FrameSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!slot.HasTrustedApertureMask || slot.Bounds is not { Width: > 0, Height: > 0 } bounds)
            throw new InvalidOperationException("A verified pixel aperture map is required to create a preview opacity mask.");

        using var maskBitmap = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var mask = slot.ApertureMask.Span;
        if (mask.Length != checked(bounds.Width * bounds.Height))
            throw new InvalidDataException("The aperture map length does not match the slot bounds.");

        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
        {
            var alpha = mask[y * bounds.Width + x];
            if (alpha is not (0 or 255))
                throw new InvalidDataException("Only binary aperture coverage is supported by the preview renderer.");
            maskBitmap.SetPixel(x, y, new SKColor(255, 255, 255, alpha));
        }

        using var encoded = maskBitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The raster compositor could not encode the aperture mask.");
        return encoded.ToArray();
    }

    private static void DrawMaskedImageSlot(SKCanvas canvas, FrameSlot slot, string path, SKRect destination)
    {
        var bounds = slot.Bounds ?? throw new InvalidOperationException("A masked image slot must have bounds.");
        var mask = slot.ApertureMask.Span;
        var slotInfo = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var slotSurface = SKSurface.Create(slotInfo)
            ?? throw new InvalidOperationException("Could not allocate an isolated image-slot surface.");
        var slotCanvas = slotSurface.Canvas;
        slotCanvas.Clear(SKColors.Transparent);
        slotCanvas.Save();
        slotCanvas.Translate(-bounds.X, -bounds.Y);
        DrawImageSlot(slotCanvas, slot, path, destination);
        slotCanvas.Restore();

        using var maskBitmap = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
        {
            var alpha = mask[y * bounds.Width + x];
            maskBitmap.SetPixel(x, y, new SKColor(255, 255, 255, alpha));
        }

        using (var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, IsAntialias = false })
            slotCanvas.DrawBitmap(maskBitmap, 0, 0, maskPaint);

        using var slotImage = slotSurface.Snapshot();
        canvas.DrawImage(slotImage, bounds.X, bounds.Y);
    }

    private static void DrawImageSlot(SKCanvas canvas, FrameSlot slot, string path, SKRect destination)
    {
        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode the assigned source image '{Path.GetFileName(path)}'.");
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidDataException($"The assigned source image '{Path.GetFileName(path)}' has no pixels.");

        var scaleX = destination.Width / source.Width;
        var scaleY = destination.Height / source.Height;
        var scale = slot.IsLogo || slot.IsFitMode ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
        scale *= (float)slot.Zoom;
        var drawWidth = source.Width * scale;
        var drawHeight = source.Height * scale;
        var centerX = destination.MidX + (float)slot.OffsetX * destination.Width;
        var centerY = destination.MidY + (float)slot.OffsetY * destination.Height;

        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        using var image = SKImage.FromBitmap(source);
        var sampling = new SKSamplingOptions(new SKCubicResampler(1f / 3f, 1f / 3f));

        canvas.Save();
        canvas.ClipRect(destination, SKClipOperation.Intersect, antialias: false);
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees((float)slot.Rotation);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(-drawWidth / 2, -drawHeight / 2, drawWidth / 2, drawHeight / 2),
            sampling,
            paint);
        canvas.Restore();
    }

    private static void DrawTextSlot(SKCanvas canvas, FrameSlot slot, string value, SKRect destination)
    {
        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Math.Clamp(destination.Height * 0.22f, 10, 160), 1f, 0);
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        var lines = Wrap(value, font, paint, Math.Max(1, destination.Width - 12));
        if (lines.Count == 0)
            return;

        var lineHeight = font.Size * 1.2f;
        var blockHeight = lineHeight * lines.Count;
        var baseline = -blockHeight / 2 + font.Size;
        var centerX = destination.MidX + (float)slot.OffsetX * destination.Width;
        var centerY = destination.MidY + (float)slot.OffsetY * destination.Height;

        canvas.Save();
        canvas.ClipRect(destination, SKClipOperation.Intersect, antialias: false);
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees((float)slot.Rotation);
        canvas.Scale((float)slot.Zoom, (float)slot.Zoom);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var x = -font.MeasureText(line, paint) / 2;
            canvas.DrawText(line, x, baseline + index * lineHeight, font, paint);
        }
        canvas.Restore();
    }

    private static List<string> Wrap(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (line.Length > 0 && font.MeasureText(candidate, paint) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            if (line.Length > 0)
                lines.Add(line);
        }
        return lines;
    }
}

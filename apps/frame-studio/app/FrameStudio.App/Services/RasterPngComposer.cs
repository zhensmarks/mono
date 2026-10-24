using FrameStudio.App.Models;
using SkiaSharp;

namespace FrameStudio.App.Services;

/// <summary>
/// Composes local slot overlays over the PSD's stored merged composite. It never writes to the PSD
/// and deliberately uses estimated layer rectangles instead of claiming Photoshop mask fidelity.
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
            if ((slot.IsPhoto || slot.IsLogo) && !slot.IsPlacementReady)
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
            if ((slot.IsPhoto || slot.IsLogo) && slot.Photo is { } photo)
                DrawImageSlot(canvas, slot, photo.Path, destination);
            else if (slot.IsText && !string.IsNullOrWhiteSpace(slot.Value))
                DrawTextSlot(canvas, slot, slot.Value, destination);
        }

        using var outputImage = surface.Snapshot();
        using var encoded = outputImage.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The raster compositor could not encode a PNG output.");
        return encoded.ToArray();
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
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        canvas.Save();
        canvas.ClipRect(destination, SKClipOperation.Intersect, antialias: false);
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees((float)slot.Rotation);
        canvas.DrawBitmap(
            source,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(-drawWidth / 2, -drawHeight / 2, drawWidth / 2, drawHeight / 2),
            paint);
        canvas.Restore();
    }

    private static void DrawTextSlot(SKCanvas canvas, FrameSlot slot, string value, SKRect destination)
    {
        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Typeface = typeface,
            TextSize = Math.Clamp(destination.Height * 0.22f, 10, 160)
        };

        var lines = Wrap(value, paint, Math.Max(1, destination.Width - 12));
        if (lines.Count == 0)
            return;

        var lineHeight = paint.TextSize * 1.2f;
        var blockHeight = lineHeight * lines.Count;
        var baseline = -blockHeight / 2 + paint.TextSize;
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
            var x = -paint.MeasureText(line) / 2;
            canvas.DrawText(line, x, baseline + index * lineHeight, paint);
        }
        canvas.Restore();
    }

    private static List<string> Wrap(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (line.Length > 0 && paint.MeasureText(candidate) > maxWidth)
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

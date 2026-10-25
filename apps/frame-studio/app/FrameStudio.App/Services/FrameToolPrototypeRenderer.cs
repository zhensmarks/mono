using FrameStudio.App.Models;
using SkiaSharp;

namespace FrameStudio.App.Services;

/// <summary>
/// Renders the prototype's synthetic background, a photo clipped by first-class frame geometry,
/// and synthetic foreground artwork. The returned PNG bytes are used both on screen and for export.
/// </summary>
public sealed class FrameToolPrototypeRenderer
{
    public const int ArtboardWidth = 960;
    public const int ArtboardHeight = 640;

    public byte[] RenderStoredComposite()
    {
        using var surface = CreateSurface();
        DrawBackground(surface.Canvas);
        DrawForeground(surface.Canvas);
        return Encode(surface);
    }

    public byte[] Render(
        FrameMask? mask,
        SKBitmap? photo,
        double zoom = 1,
        double rotation = 0,
        double offsetX = 0,
        double offsetY = 0,
        FrameInsertionPoint insertionPoint = FrameInsertionPoint.BetweenBackgroundAndForeground)
    {
        using var surface = CreateSurface();
        var canvas = surface.Canvas;
        DrawBackground(canvas);

        if (insertionPoint == FrameInsertionPoint.AboveForeground)
            DrawForeground(canvas);

        if (mask is { IsValid: true } && photo is { Width: > 0, Height: > 0 })
            DrawPhotoThroughMask(canvas, mask, photo, zoom, rotation, offsetX, offsetY);

        if (insertionPoint == FrameInsertionPoint.BetweenBackgroundAndForeground)
            DrawForeground(canvas);

        return Encode(surface);
    }

    public static SKBitmap CreateSyntheticPhoto()
    {
        var bitmap = new SKBitmap(960, 640, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#A8C5C4"));

        using (var sky = new SKPaint { IsAntialias = true })
        {
            sky.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, 430),
                new SKColor[] { SKColor.Parse("#A7C6C8"), SKColor.Parse("#E9C9A5") },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, 960, 430), sky);
        }

        using (var sun = new SKPaint { Color = SKColor.Parse("#F4D38A"), IsAntialias = true })
            canvas.DrawCircle(265, 215, 62, sun);

        using (var farRidge = new SKPaint { Color = SKColor.Parse("#789A98"), IsAntialias = true })
        using (var path = new SKPath())
        {
            path.MoveTo(0, 390);
            path.LineTo(158, 236);
            path.LineTo(282, 354);
            path.LineTo(428, 208);
            path.LineTo(610, 380);
            path.LineTo(760, 262);
            path.LineTo(960, 382);
            path.LineTo(960, 640);
            path.LineTo(0, 640);
            path.Close();
            canvas.DrawPath(path, farRidge);
        }

        using (var nearRidge = new SKPaint { Color = SKColor.Parse("#426D68"), IsAntialias = true })
        using (var path = new SKPath())
        {
            path.MoveTo(0, 438);
            path.LineTo(205, 336);
            path.LineTo(390, 431);
            path.LineTo(590, 306);
            path.LineTo(760, 431);
            path.LineTo(960, 348);
            path.LineTo(960, 640);
            path.LineTo(0, 640);
            path.Close();
            canvas.DrawPath(path, nearRidge);
        }

        using (var water = new SKPaint { Color = SKColor.Parse("#739C99"), IsAntialias = true })
            canvas.DrawRect(new SKRect(0, 458, 960, 640), water);
        using (var waterline = new SKPaint { Color = SKColor.Parse("#A9C7B8"), StrokeWidth = 3, IsAntialias = true })
        {
            foreach (var (y, left, right) in new[] { (482f, 102f, 520f), (510f, 172f, 618f), (541f, 60f, 450f), (568f, 230f, 650f) })
                canvas.DrawLine(left, y, right, y, waterline);
        }

        using (var shore = new SKPaint { Color = SKColor.Parse("#314E49"), IsAntialias = true })
        using (var path = new SKPath())
        {
            path.MoveTo(0, 582);
            path.CubicTo(140, 525, 255, 600, 392, 553);
            path.CubicTo(540, 500, 730, 607, 960, 538);
            path.LineTo(960, 640);
            path.LineTo(0, 640);
            path.Close();
            canvas.DrawPath(path, shore);
        }

        return bitmap;
    }

    private static SKSurface CreateSurface() => SKSurface.Create(
        new SKImageInfo(ArtboardWidth, ArtboardHeight, SKColorType.Rgba8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Artboard pratinjau tidak dapat dibuat.");

    private static byte[] Encode(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG pratinjau tidak dapat dikodekan.");
        return encoded.ToArray();
    }

    private static void DrawBackground(SKCanvas canvas)
    {
        canvas.Clear(SKColor.Parse("#EDE8DE"));
        using var dark = new SKPaint { Color = SKColor.Parse("#263B37"), IsAntialias = true };
        using var muted = new SKPaint { Color = SKColor.Parse("#68736C"), IsAntialias = true };
        using var rule = new SKPaint { Color = SKColor.Parse("#C8C1B4"), StrokeWidth = 1.4f, IsAntialias = true };
        using var titleTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var regularTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;

        using var fieldFont = new SKFont(regularTypeface, 13f, 1.05f, 0);
        using var smallMutedFont = new SKFont(regularTypeface, 12f, 1f, 0);
        using var titleFont = new SKFont(titleTypeface, 40f, 0.95f, 0);
        using var bodyFont = new SKFont(regularTypeface, 15f, 1f, 0);
        using var labelDarkFont = new SKFont(titleTypeface, 11f, 1f, 0);
        using var labelMutedFont = new SKFont(regularTypeface, 11f, 1f, 0);

        canvas.DrawLine(62, 84, 898, 84, rule);
        canvas.DrawText("FIELD NOTES   /   FRAME STUDY", 64, 61, SKTextAlign.Left, fieldFont, dark);
        canvas.DrawText("SYNTHETIC ARTBOARD     ·     01", 715, 61, SKTextAlign.Left, smallMutedFont, muted);

        using var divider = new SKPaint { Color = SKColor.Parse("#C8C1B4"), StrokeWidth = 1.2f, IsAntialias = true };
        canvas.DrawLine(684, 124, 684, 563, divider);

        canvas.DrawText("JENDELA", 720, 253, SKTextAlign.Left, titleFont, dark);
        canvas.DrawText("KE JEDA", 720, 301, SKTextAlign.Left, titleFont, dark);

        canvas.DrawText("Satu ruang untuk foto", 721, 345, SKTextAlign.Left, bodyFont, muted);
        canvas.DrawText("yang ingin disimpan.", 721, 368, SKTextAlign.Left, bodyFont, muted);
        canvas.DrawLine(721, 399, 848, 399, rule);

        canvas.DrawText("FRAME  /  01", 721, 428, SKTextAlign.Left, labelDarkFont, dark);
        canvas.DrawText("BENTUK DITENTUKAN PENGGUNA", 721, 451, SKTextAlign.Left, labelMutedFont, muted);

        canvas.DrawLine(62, 584, 898, 584, rule);
        canvas.DrawText("CONTOH VISUAL SINTETIS", 64, 609, SKTextAlign.Left, labelMutedFont, muted);
        canvas.DrawText("FOTO TERPOTONG MENGIKUTI MASK", 681, 609, SKTextAlign.Left, labelMutedFont, muted);
    }

    private static void DrawForeground(SKCanvas canvas)
    {
        // This small botanical line-art is a synthetic foreground layer used to show insertion order.
        using var stemPaint = new SKPaint
        {
            Color = SKColor.Parse("#F1EADC"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var leafPaint = new SKPaint { Color = SKColor.Parse("#F1EADC"), IsAntialias = true };
        using var stem = new SKPath();
        stem.MoveTo(534, 514);
        stem.CubicTo(552, 452, 569, 389, 602, 332);
        stem.CubicTo(613, 311, 625, 293, 641, 278);
        canvas.DrawPath(stem, stemPaint);

        DrawLeaf(canvas, leafPaint, 559, 444, -32, 23, 10);
        DrawLeaf(canvas, leafPaint, 583, 388, 28, 24, 10);
        DrawLeaf(canvas, leafPaint, 605, 337, -35, 21, 9);
        DrawLeaf(canvas, leafPaint, 625, 294, 33, 19, 8);

        using var dotPaint = new SKPaint { Color = SKColor.Parse("#E8AE75"), IsAntialias = true };
        canvas.DrawCircle(641, 278, 5, dotPaint);
    }

    private static void DrawLeaf(SKCanvas canvas, SKPaint paint, float x, float y, float rotation, float width, float height)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateDegrees(rotation);
        canvas.DrawOval(new SKRect(-width / 2, -height / 2, width / 2, height / 2), paint);
        canvas.Restore();
    }

    private static void DrawPhotoThroughMask(
        SKCanvas canvas,
        FrameMask mask,
        SKBitmap photo,
        double zoom,
        double rotation,
        double offsetX,
        double offsetY)
    {
        using var path = new SKPath();
        var first = mask.Points[0];
        path.MoveTo((float)(first.X * ArtboardWidth), (float)(first.Y * ArtboardHeight));
        for (var index = 1; index < mask.Points.Count; index++)
        {
            var point = mask.Points[index];
            path.LineTo((float)(point.X * ArtboardWidth), (float)(point.Y * ArtboardHeight));
        }
        path.Close();

        var bounds = path.Bounds;
        var scale = Math.Max(bounds.Width / photo.Width, bounds.Height / photo.Height) * (float)Math.Clamp(zoom, 0.25, 4);
        var drawWidth = photo.Width * scale;
        var drawHeight = photo.Height * scale;
        var centerX = bounds.MidX + (float)Math.Clamp(offsetX, -2, 2) * bounds.Width;
        var centerY = bounds.MidY + (float)Math.Clamp(offsetY, -2, 2) * bounds.Height;

        using var paint = new SKPaint { IsAntialias = true };
        using var image = SKImage.FromBitmap(photo);
        var sampling = new SKSamplingOptions(new SKCubicResampler(1f / 3f, 1f / 3f));
        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees((float)rotation);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, photo.Width, photo.Height),
            new SKRect(-drawWidth / 2, -drawHeight / 2, drawWidth / 2, drawHeight / 2),
            sampling,
            paint);
        canvas.Restore();
    }
}

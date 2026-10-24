using Avalonia.Media.Imaging;
using FrameStudio.App.Models;

namespace FrameStudio.App.Services;

public sealed class PhotoFileService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    public bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public SourcePhoto Load(string path)
    {
        Bitmap? thumbnail = null;
        Bitmap? image = null;
        string? previewError = null;
        try
        {
            using var stream = File.OpenRead(path);
            image = new Bitmap(stream);
            thumbnail = image.CreateScaledBitmap(new Avalonia.PixelSize(112, 112), BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            thumbnail?.Dispose();
            image?.Dispose();
            thumbnail = null;
            image = null;
            previewError = "Pratinjau tidak tersedia";
        }

        return new SourcePhoto(path, thumbnail, previewError, image);
    }
}

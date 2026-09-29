using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

/// <summary>
/// Represents a file staged for upload as a Trello card attachment.
/// Generates a thumbnail preview for image files.
/// </summary>
public class PendingAttachmentItem : ObservableObject, IDisposable
{
    private static readonly string[] _imageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    public string FilePath { get; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public bool IsImage { get; }

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public PendingAttachmentItem(string filePath)
    {
        FilePath = filePath;
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        IsImage = _imageExts.Contains(ext);
        if (IsImage) _ = LoadThumbnailAsync();
    }

    private async System.Threading.Tasks.Task LoadThumbnailAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                using var stream = System.IO.File.OpenRead(FilePath);
                Thumbnail = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 120);
            });
        }
        catch { /* Corrupt or inaccessible image */ }
    }

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

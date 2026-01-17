using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

public partial class TrelloAttachment : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string PreviewUrl { get; set; } = ""; // Thumbnail
    public bool IsImage { get; set; }
    public long Bytes { get; set; }

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isDownloading;
}

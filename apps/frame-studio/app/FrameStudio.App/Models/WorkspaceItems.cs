using Avalonia.Media.Imaging;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrameStudio.App.Models;

public enum SlotKind
{
    Photo,
    Text,
    Logo
}

public sealed class FrameSlot : INotifyPropertyChanged
{
    private string? _value;
    private SourcePhoto? _photo;
    private bool _isSelected;

    public FrameSlot(string id, string label, SlotKind kind, string helper, string? value = null)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Helper = helper;
        _value = value;
    }

    public string Id { get; }
    public string Label { get; }
    public SlotKind Kind { get; }
    public string Helper { get; }
    public bool IsPhoto => Kind == SlotKind.Photo;
    public bool IsText => Kind == SlotKind.Text;
    public bool IsLogo => Kind == SlotKind.Logo;
    public string? Value { get => _value; set { _value = value; OnPropertyChanged(); } }
    public SourcePhoto? Photo { get => _photo; set { _photo = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); } }
    public string DisplayValue => Photo?.FileName ?? Value ?? "Belum diisi";
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SourcePhoto
{
    public SourcePhoto(string path, Bitmap? thumbnail, string? previewError = null)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Thumbnail = thumbnail;
        PreviewError = previewError;
    }

    public string Path { get; }
    public string FileName { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public string? PreviewError { get; }
}

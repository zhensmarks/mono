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

public readonly record struct FrameSlotBounds(int X, int Y, int Width, int Height);

public sealed record PsdLayerTreeRow(
    string StructurePath,
    string DisplayName,
    string Kind,
    string Details,
    string IndentPrefix);

public sealed class FrameSlot : INotifyPropertyChanged
{
    private string? _value;
    private SourcePhoto? _photo;
    private bool _isSelected;
    private double _zoom = 1;
    private double _rotation;
    private double _offsetX;
    private double _offsetY;
    private bool _isFitMode;
    private readonly bool _isPlacementReady;

    public FrameSlot(string id, string label, SlotKind kind, string helper, string? value = null, FrameSlotBounds? bounds = null, bool isPlacementReady = false)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Helper = helper;
        _value = value;
        Bounds = bounds;
        _isPlacementReady = isPlacementReady;
    }

    public string Id { get; }
    public string Label { get; }
    public SlotKind Kind { get; }
    public string KindLabel => Kind switch
    {
        SlotKind.Photo => "Foto",
        SlotKind.Text => "Teks",
        SlotKind.Logo => "Logo",
        _ => "Slot"
    };
    public string Helper { get; }
    public FrameSlotBounds? Bounds { get; }
    public bool HasBounds => Bounds is { Width: > 0, Height: > 0 };
    public bool IsPlacementReady => _isPlacementReady && HasBounds;
    public bool IsPhoto => Kind == SlotKind.Photo;
    public bool IsText => Kind == SlotKind.Text;
    public bool IsLogo => Kind == SlotKind.Logo;
    public string? Value { get => _value; set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); } }
    public SourcePhoto? Photo { get => _photo; set { _photo = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); OnPropertyChanged(nameof(IsFilled)); } }
    public string DisplayValue => Photo?.FileName ?? Value ?? "Belum diisi";
    public bool IsFilled => Photo is not null || !string.IsNullOrWhiteSpace(Value);
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public double Zoom { get => _zoom; set { _zoom = Math.Clamp(value, 0.5, 3); OnPropertyChanged(); } }
    public double Rotation { get => _rotation; set { _rotation = value % 360; OnPropertyChanged(); } }
    public double OffsetX { get => _offsetX; set { _offsetX = value; OnPropertyChanged(); } }
    public double OffsetY { get => _offsetY; set { _offsetY = value; OnPropertyChanged(); } }
    public bool IsFitMode { get => _isFitMode; set { _isFitMode = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SourcePhoto : IDisposable
{
    private bool _disposed;

    public SourcePhoto(string path, Bitmap? thumbnail, string? previewError = null, Bitmap? image = null)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Thumbnail = thumbnail;
        Image = image;
        PreviewError = previewError;
    }

    public string Path { get; }
    public string FileName { get; }
    public Bitmap? Thumbnail { get; }
    public Bitmap? Image { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public string? PreviewError { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Thumbnail?.Dispose();
        if (!ReferenceEquals(Image, Thumbnail))
            Image?.Dispose();
    }
}

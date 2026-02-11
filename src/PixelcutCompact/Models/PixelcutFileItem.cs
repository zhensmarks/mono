using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace PixelcutCompact.Models;

public partial class PixelcutFileItem : ObservableObject
{
    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private string _status = "Menunggu";
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _originalSizeDisplay = "";
    [ObservableProperty] private string _resultSizeDisplay = "";
    [ObservableProperty] private long _resultSize;
    [ObservableProperty] private string _resultPath = "";
    [ObservableProperty] private bool _hasResult;

    partial void OnResultPathChanged(string value)
    {
        HasResult = !string.IsNullOrEmpty(value) && File.Exists(value);
    }

    public PixelcutFileItem(string path)
    {
        FilePath = path;
        // Format: PARENT_FOLDER\FILENAME.EXT
        var dir = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        FileName = Path.Combine(dir, Path.GetFileName(path));
        
        var info = new FileInfo(path);
        if (info.Exists)
        {
            OriginalSizeDisplay = FormatSize(info.Length);
        }
    }
    
    partial void OnResultSizeChanged(long value)
    {
        ResultSizeDisplay = FormatSize(value);
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        // Use N2 for 2 decimal places with comma (depending on culture, but usually N2 handles it well)
        // If we want to force comma, we can use specific culture, but let's try standard N2 first 
        // which usually follows system locale (User likely has ID/EU locale for comma)
        // Or explicitly replace dot with comma for "Indonesian style" if system is US.
        return $"{len:0.00} {sizes[order]}".Replace('.', ','); 
    }
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnail == null) LoadThumbnailAsync();
            return _thumbnail;
        }
        private set => SetProperty(ref _thumbnail, value);
    }

    private bool _isLoadingThumbnail;

    private async void LoadThumbnailAsync()
    {
        if (_isLoadingThumbnail || _thumbnail != null) return;
        _isLoadingThumbnail = true;

        var path = HasResult && File.Exists(ResultPath) ? ResultPath : FilePath;
        if (!File.Exists(path)) return;

        try
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                // Decode to 200px width/height to save memory
                var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 200);
                
                // Dispatch to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Thumbnail = bitmap;
                    _isLoadingThumbnail = false;
                });
            });
        }
        catch
        {
            _isLoadingThumbnail = false;
        }
    }

    partial void OnHasResultChanged(bool value)
    {
        // Invalidate thumbnail to reload (e.g. switch from original to result)
        _thumbnail = null;
        OnPropertyChanged(nameof(Thumbnail));
        LoadThumbnailAsync();
    }
}

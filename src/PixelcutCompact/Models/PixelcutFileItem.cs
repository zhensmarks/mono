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

    public PixelcutFileItem(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        
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
        return $"{len:0.1} {sizes[order]}";
    }
}

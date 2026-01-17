using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using Avalonia.Media;

namespace BMachine.UI.Models;

public partial class PixelcutFileItem : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _status = "Menunggu"; // Menunggu, Memproses, Selesai, Gagal
    [ObservableProperty] private double _progress = 0;
    [ObservableProperty] private long _originalSize = 0;
    [ObservableProperty] private long _resultSize = 0;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string? _errorMessage; // Inline error display
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isSelected; // Added for selection support

    public Avalonia.Media.IBrush StatusColor => _status switch 
    {
        "Selesai" => Avalonia.Media.Brushes.LightGreen,
        "Gagal" => Avalonia.Media.Brushes.Red,
        "Memproses" => Avalonia.Media.Brushes.SkyBlue,
        "Berjalan..." => Avalonia.Media.Brushes.SkyBlue,
        _ => Avalonia.Media.Brushes.Gray
    };

    [ObservableProperty] private string? _customDisplayName;

    // Display: FOLDER\File.jpg
    public string DisplayName 
    { 
        get 
        {
            if (!string.IsNullOrEmpty(_customDisplayName)) return _customDisplayName;

            var folder = Path.GetFileName(Path.GetDirectoryName(_filePath)) ?? "";
            return string.IsNullOrEmpty(folder) ? _fileName : $"{folder}\\{_fileName}";
        }
    }
    
    public void SetDisplayName(string name)
    {
        CustomDisplayName = name;
        OnPropertyChanged(nameof(DisplayName));
    }
    
    public string OriginalSizeDisplay => FormatSize(_originalSize);
    public string ResultSizeDisplay => _resultSize > 0 ? FormatSize(_resultSize) : "-";

    public PixelcutFileItem(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        if (File.Exists(path))
        {
            _originalSize = new FileInfo(path).Length;
        }
    }

    partial void OnResultSizeChanged(long value)
    {
        OnPropertyChanged(nameof(ResultSizeDisplay));
    }

    partial void OnIsDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(ResultSizeDisplay));
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

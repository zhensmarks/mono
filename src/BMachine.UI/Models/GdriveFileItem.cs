using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace BMachine.UI.Models;

public partial class GdriveFileItem : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _displayPath = "";
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isSelected;

    public long FileSize { get; private set; }
    public string FileSizeDisplay => FormatSize(FileSize);

    public GdriveFileItem(string filePath, string displayPath)
    {
        FilePath = filePath;
        DisplayPath = displayPath;
        if (File.Exists(filePath))
        {
            FileSize = new FileInfo(filePath).Length;
        }
    }

    private static string FormatSize(long bytes)
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

    public override bool Equals(object? obj)
    {
        if (obj is GdriveFileItem other)
        {
            return FilePath == other.FilePath;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return FilePath.GetHashCode();
    }
}

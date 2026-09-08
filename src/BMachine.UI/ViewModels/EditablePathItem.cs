using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public class StoredPathEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

public partial class EditablePathItem : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isEditingPath;

    public string AutoDetectedName => GetFolderName(Path);

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : AutoDetectedName;

    public static string GetFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var folderName = System.IO.Path.GetFileName(trimmed);
            return !string.IsNullOrEmpty(folderName) ? folderName : trimmed;
        }
        catch
        {
            return path ?? "";
        }
    }

    public EditablePathItem()
    {
    }

    public EditablePathItem(string path, string? name = null)
    {
        _path = path;
        _name = !string.IsNullOrWhiteSpace(name) ? name : GetFolderName(path);
    }

    partial void OnPathChanged(string value)
    {
        OnPropertyChanged(nameof(AutoDetectedName));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    [RelayCommand]
    private void ToggleEditPath()
    {
        IsEditingPath = !IsEditingPath;
    }

    public static List<(string Path, string Name)> ParseStoredPaths(string? json)
    {
        var result = new List<(string Path, string Name)>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        var p = elem.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(p))
                            result.Add((p, GetFolderName(p)));
                    }
                    else if (elem.ValueKind == JsonValueKind.Object)
                    {
                        string p = elem.TryGetProperty("Path", out var pElem) ? pElem.GetString() ?? "" : "";
                        string n = elem.TryGetProperty("Name", out var nElem) ? nElem.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            if (string.IsNullOrWhiteSpace(n)) n = GetFolderName(p);
                            result.Add((p, n));
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }
}

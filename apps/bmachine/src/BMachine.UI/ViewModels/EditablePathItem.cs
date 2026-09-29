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

    /// <summary>Hex warna (mis. "#FF5722") untuk menandai folder root ini di browser.</summary>
    public string Color { get; set; } = "";

    /// <summary>Key resource ikon (mis. "IconFolder", "IconStar") untuk folder root ini.</summary>
    public string Icon { get; set; } = "";
}

public partial class EditablePathItem : ObservableObject
{
    /// <summary>Warna default root folder (dipakai bila user belum memilih).</summary>
    public const string DefaultColor = "#4A9EFF";

    /// <summary>Ikon default root folder (dipakai bila user belum memilih).</summary>
    public const string DefaultIcon = "IconFolder";

    /// <summary>Daftar key ikon yang boleh dipilih di menu Pengaturan path.</summary>
    public static readonly string[] AvailableIcons =
    {
        "IconFolder", "IconFolderFilled", "IconFolderOpen",
        "IconStar", "IconStarFilled", "IconBolt",
        "IconBookmark", "IconHeart", "IconHeartFilled",
        "IconHome", "IconHome2", "IconBox", "IconLayers"
    };

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isEditingPath;

    /// <summary>Hex warna root folder di tree browser. Kosong = pakai default.</summary>
    [ObservableProperty]
    private string _color = "";

    /// <summary>Key ikon root folder di tree browser. Kosong = pakai default.</summary>
    [ObservableProperty]
    private string _icon = "";

    /// <summary>Warna efektif (fallback ke default bila kosong).</summary>
    public string EffectiveColor => string.IsNullOrWhiteSpace(Color) ? DefaultColor : Color;

    /// <summary>Ikon efektif (fallback ke default bila kosong).</summary>
    public string EffectiveIcon => string.IsNullOrWhiteSpace(Icon) ? DefaultIcon : Icon;

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

    partial void OnColorChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveColor));
    }

    partial void OnIconChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveIcon));
    }

    [RelayCommand]
    private void ToggleEditPath()
    {
        IsEditingPath = !IsEditingPath;
    }

    /// <summary>Set icon key for this root folder (called from the icon palette).</summary>
    [RelayCommand]
    private void PickIcon(string? iconKey)
    {
        if (!string.IsNullOrWhiteSpace(iconKey))
            Icon = iconKey;
    }

    /// <summary>Reset colour &amp; icon back to defaults.</summary>
    [RelayCommand]
    private void ResetVisual()
    {
        Color = "";
        Icon = "";
    }

    public static List<(string Path, string Name)> ParseStoredPaths(string? json)
    {
        var result = new List<(string Path, string Name)>();
        foreach (var entry in ParseStoredEntries(json))
            result.Add((entry.Path, entry.Name));
        return result;
    }

    /// <summary>
    /// Parse JSON tersimpan menjadi entri lengkap (Path, Name, Color, Icon).
    /// Backward compatible dengan format lama (array string atau objek tanpa
    /// Color/Icon) -> field yang hilang dikosongkan dan nanti pakai default.
    /// </summary>
    public static List<StoredPathEntry> ParseStoredEntries(string? json)
    {
        var result = new List<StoredPathEntry>();
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
                            result.Add(new StoredPathEntry { Path = p, Name = GetFolderName(p) });
                    }
                    else if (elem.ValueKind == JsonValueKind.Object)
                    {
                        string p = elem.TryGetProperty("Path", out var pElem) ? pElem.GetString() ?? "" : "";
                        string n = elem.TryGetProperty("Name", out var nElem) ? nElem.GetString() ?? "" : "";
                        string c = elem.TryGetProperty("Color", out var cElem) ? cElem.GetString() ?? "" : "";
                        string i = elem.TryGetProperty("Icon", out var iElem) ? iElem.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            if (string.IsNullOrWhiteSpace(n)) n = GetFolderName(p);
                            result.Add(new StoredPathEntry { Path = p, Name = n, Color = c, Icon = i });
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }
}

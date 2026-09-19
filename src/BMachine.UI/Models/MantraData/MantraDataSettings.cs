using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BMachine.UI.Models.MantraData;

public class MantraDataSettings
{
    public string PhotoshopExePath { get; set; } = @"C:\Program Files\Adobe\Adobe Photoshop 2020\Photoshop.exe";
    public string LastPsdFolder { get; set; } = string.Empty;
    public string LastPhotoFolder { get; set; } = string.Empty;
    public int PhotoMatchThreshold { get; set; } = 62;
    public bool AiEnabled { get; set; } = false;
    public string AiProvider { get; set; } = "ollama";
    public string AiEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = "";
    public bool FreezeColumnsEnabled { get; set; } = false;
    public int FrozenColumnCount { get; set; } = 2;
    public List<string> RecentFiles { get; set; } = new();
    public Dictionary<string, bool> MenuVisibility { get; set; } = new();
    public Dictionary<string, string> MenuShortcuts { get; set; } = new();

    public bool GetMenuVisibility(string key)
    {
        if (MenuVisibility != null && MenuVisibility.TryGetValue(key, out var visible))
            return visible;
        return true;
    }

    public string GetMenuShortcut(string key)
    {
        if (MenuShortcuts != null && MenuShortcuts.TryGetValue(key, out var sc))
            return sc;
        return GetDefaultShortcut(key);
    }

    public static string GetDefaultShortcut(string key) => key switch
    {
        "Copy" => "Ctrl+C",
        "Paste" => "Ctrl+V",
        "ClearCells" => "Delete",
        "FindReplace" => "Ctrl+F",
        _ => string.Empty
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BMachine",
        "mantradata_settings.json"
    );

    public static MantraDataSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<MantraDataSettings>(json) ?? new MantraDataSettings();
            }
        }
        catch { }

        return new MantraDataSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}

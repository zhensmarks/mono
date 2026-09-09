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
    public int PhotoMatchThreshold { get; set; } = 12;
    public bool FreezeColumnsEnabled { get; set; } = false;
    public int FrozenColumnCount { get; set; } = 2;
    public List<string> RecentFiles { get; set; } = new();

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

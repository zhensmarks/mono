using System;
using System.IO;
using System.Text.Json;

namespace PixelcutCompact.Services;

public class PreviewWindowSettings
{
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 600;
    public double Zoom { get; set; } = 1.0;
    public string PhotoshopPath { get; set; } = "";

    private static string GetPath() => Path.Combine(AppContext.BaseDirectory, "preview_settings.json");

    public static PreviewWindowSettings Load()
    {
        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<PreviewWindowSettings>(json) ?? new PreviewWindowSettings();
            }
        }
        catch { }
        return new PreviewWindowSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(GetPath(), json);
        }
        catch { }
    }
}

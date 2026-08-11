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
    
    // Shortcuts
    public string ShortcutNext { get; set; } = "Right";
    public string ShortcutPrevious { get; set; } = "Left";
    public string ShortcutPhotoshop { get; set; } = "P";
    public string ShortcutRotate { get; set; } = "R";
    public string ShortcutFitScreen { get; set; } = "F";
    
    // Photopea settings
    public string PhotopeaSaveFormat { get; set; } = "png"; // "png" or "psd"
    public bool AutoAdvanceOnSave { get; set; } = false;

    // Background settings
    public int BackgroundType { get; set; } = 0; // 0: Default, 1: Checkerboard, 2: Solid
    public string SolidColorHex { get; set; } = "#00FF00";
    public string CheckerColor1 { get; set; } = "#333333";
    public string CheckerColor2 { get; set; } = "#4D4D4D";

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

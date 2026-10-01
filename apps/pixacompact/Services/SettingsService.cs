using System;
using System.IO;
using System.Text.Json;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services;

public class SettingsService
{
    private const string FileName = "settings.json";
    private readonly string _filePath;

    public SettingsService()
        : this(Path.Combine(AppContext.BaseDirectory, FileName))
    {
    }

    public SettingsService(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }
}

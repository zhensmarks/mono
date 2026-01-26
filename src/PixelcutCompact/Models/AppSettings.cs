using Avalonia;

namespace PixelcutCompact.Models;

public class AppSettings
{
    public string? ProxyAddress { get; set; }
    public double WindowWidth { get; set; } = 380;
    public double WindowHeight { get; set; } = 350;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public bool IsMaximized { get; set; }
    public string? PythonScriptPath { get; set; }
}

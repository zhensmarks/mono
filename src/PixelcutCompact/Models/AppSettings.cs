using Avalonia;
using System;
using System.Collections.Generic;

namespace PixelcutCompact.Models;

public class AppSettings
{
    public double WindowWidth { get; set; } = 380;
    public double WindowHeight { get; set; } = 350;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public bool IsMaximized { get; set; }
    public string? PythonScriptPath { get; set; }
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#3b82f6";
    public string? CustomDarkBackground { get; set; }
    public string? CustomLightBackground { get; set; }
    public string? PixaApiKey { get; set; }
    public List<PixaAccount> PixaAccounts { get; set; } = new();
    public Guid? ActiveAccountId { get; set; }
    public bool UseWebMode { get; set; } = true;
    public string RemoveBgEngine { get; set; } = "PIXA";
    public string RembgModel { get; set; } = "u2netp";
    public string? RembgExecutablePath { get; set; }
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
}

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
    public string? ProxyAddress { get; set; }
    public string? PixaApiKey { get; set; }
    public List<PixaAccount> PixaAccounts { get; set; } = new();
    public Guid? ActiveAccountId { get; set; }
    /// <summary>Aktifkan rotasi round-robin antar akun pixelcut (butuh minimal 1 akun terdaftar).</summary>
    public bool UseAccountRotation { get; set; }
    public bool UseWebMode { get; set; } = true;
    public string RemoveBgEngine { get; set; } = "PIXA";
    public string RembgModel { get; set; } = "u2netp";
    public string? RembgExecutablePath { get; set; }
    public bool MixProxyEnabled { get; set; }
    public string? MixProxyList { get; set; }
    public bool ShowBrowser { get; set; }
    public bool UseGpuForRembg { get; set; } = true;
    /// <summary>Ukuran batch (jumlah file per kirim). Default 100 = limit akun gratis pixelcut.</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>Ukuran sub-batch per tab. Batch besar dipecah jadi beberapa sub-batch supaya tidak crash mid-way.</summary>
    public int BatchSubSize { get; set; } = 25;
    /// <summary>Tutup browser otomatis setelah antrian selesai (kalau ShowBrowser aktif).</summary>
    public bool AutoCloseBrowser { get; set; } = true;
    public bool AlphaMattingEnabled { get; set; }
    public int AlphaMattingErodeSize { get; set; } = 10;
    public int AlphaMattingForegroundThreshold { get; set; } = 240;
    public int AlphaMattingBackgroundThreshold { get; set; } = 10;
}


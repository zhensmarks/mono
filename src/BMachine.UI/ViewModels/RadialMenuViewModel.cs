using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.SDK;
using BMachine.UI.Models;
using Avalonia;
using Avalonia.Controls;

namespace BMachine.UI.ViewModels;

public partial class RadialMenuViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    private readonly Services.IProcessLogService? _logService;
    // private Dictionary<string, string> _scriptAliases = new(); // Replaced by ScriptConfig version below
    
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private Thickness _menuMargin; 

    public ObservableCollection<RadialMenuItem> MasterItems { get; } = new();
    public ObservableCollection<RadialMenuItem> ActionItems { get; } = new();

    [ObservableProperty]
    private RadialMenuItem? _highlightedItem;

    [ObservableProperty] private string _customScriptsPath = "";

    public RadialMenuViewModel(IDatabase? database, Services.IProcessLogService? logService = null)
    {
        _database = database;
        _logService = logService;
        
        LoadScripts();
    }

    public void ReloadScripts()
    {
        LoadScripts();
    }

    private void LoadScripts()
    {
        MasterItems.Clear(); 
        ActionItems.Clear();
        LoadMetadata();

        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        
        // Load All Scripts into temp collection or directly
        // We'll add directly then sort
        
        // Action Scripts Only (.jsx and .pyw)
        var actionPath = Path.Combine(baseDir, "Action");
        if (Directory.Exists(actionPath))
        {
            foreach (var f in Directory.GetFiles(actionPath, "*.jsx")) AddItem(ActionItems, f, false);
            foreach (var f in Directory.GetFiles(actionPath, "*.pyw")) AddItem(ActionItems, f, false);
        }
        if (Directory.Exists(baseDir))
        {
            foreach (var f in Directory.GetFiles(baseDir, "*.pyw")) AddItem(ActionItems, f, false);
        }

        // Sort ActionItems
        var sorted = ActionItems.OrderBy(x => x.Order).ThenBy(x => x.FullName).ToList();
        ActionItems.Clear();
        foreach (var item in sorted) ActionItems.Add(item);

        ArrangeItems();
    }




    private void ArrangeItems()
    {
        if (ActionItems.Count == 0) return;

        double centerX = 100; 
        double centerY = 100;
        
        // Unified "Tight Arc" Logic
        // User requested: "buat dekat lagi" (closer/tighter) and "ke tengahkan" (centered on right).
        // 40 degrees step provides a tight, cohesive arc that stays on the Right side 
        // even with 5-6 items (-80..80 to -100..100).
        double angleStep = 45.0; 
        double radius = 50; 

        // If items exceed circle capacity (e.g. 9+ items @ 45deg), compress spacing
        if (ActionItems.Count * angleStep > 360)
        {
            angleStep = 360.0 / ActionItems.Count;
        }

        // Calculate total span required
        double totalSpan = (ActionItems.Count - 1) * angleStep;

        // Start Angle logic: Center the arc around 0 degrees (Right)
        double startAngle = -totalSpan / 2.0;

        for (int i = 0; i < ActionItems.Count; i++)
        {
            double angleDeg = startAngle + (i * angleStep);
            
            // Convert to Radians
            double angleRad = angleDeg * (Math.PI / 180.0);

            // Calculate Position
            ActionItems[i].X = centerX + radius * Math.Cos(angleRad) - 16; 
            ActionItems[i].Y = centerY + radius * Math.Sin(angleRad) - 16;
            
            // Normalize for Highlight Logic
            // Transform standard trig angle (0=Right) to Logic angle (0=Top/North)
            double normalizedAngle = (angleDeg + 90);
            while (normalizedAngle < 0) normalizedAngle += 360;
            while (normalizedAngle >= 360) normalizedAngle -= 360;
            
            ActionItems[i].Angle = normalizedAngle;
        }
    }

    // ...

    private Dictionary<string, ScriptConfig> _scriptAliases = new();

    private void AddItem(ObservableCollection<RadialMenuItem> collection, string path, bool isMaster)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("_")) return;

        string displayName;
        string shortName;
        int order = 9999;

        Avalonia.Media.StreamGeometry? icon = null;

        if (_scriptAliases.ContainsKey(fileName))
        {
            var config = _scriptAliases[fileName];
            displayName = config.Name;
            order = config.Order;
            
            // Resolve Icon
            if (!string.IsNullOrEmpty(config.IconKey))
            {
                 if (Application.Current!.TryGetResource(config.IconKey, null, out var res) && res is Avalonia.Media.StreamGeometry geom)
                 {
                     icon = geom;
                 }
            }
            
            // Priority: Custom Code > Generated from Custom Name > Generated from Filename
            if (!string.IsNullOrEmpty(config.Code))
            {
                shortName = config.Code;
            }
            else
            {
                shortName = RadialMenuItem.GenerateShortName(displayName);
            }
        }
        else
        {
            displayName = Path.GetFileNameWithoutExtension(fileName);
            shortName = RadialMenuItem.GenerateShortName(displayName);
        }

        collection.Add(new RadialMenuItem
        {
            FullName = displayName,
            ShortName = shortName,
            ScriptPath = path,
            IsMaster = isMaster,
            Order = order, // Store order
            Icon = icon // Assign Icon
        });
    }

    private void LoadMetadata()
    {
        try
        {
            var metaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    // Fallback helpers if transition period?
                    _scriptAliases = new(); 
                }
            }
        }
        catch { _scriptAliases = new(); }
    }

    [ObservableProperty]
    private string _centerText = "";

    public void UpdateHighlight(Point mousePos, Size windowSize)
    {
        // 1. Calculate center of window
        double cx = windowSize.Width / 2;
        double cy = windowSize.Height / 2;

        // 2. Vector from center
        double dx = mousePos.X - cx;
        double dy = mousePos.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // 3. Check Deadzone (Center) & Outer Limit
        //    Compact: Center Zone Radius 20, Outer Limit 100
        if (dist < 20 || dist > 90)
        {
             HighlightItem(null);
             CenterText = ""; // Clear text
             return;
        }

        // 4. Calculate Angle (0 is North/Up)
        double angle = Math.Atan2(dy, dx) * (180 / Math.PI);
        angle += 90; 
        if (angle < 0) angle += 360;

        // 5. Find closest item based on angle
        RadialMenuItem? closest = null;
        double minDiff = double.MaxValue;

        foreach (var item in ActionItems)
        {
            // Simple angle diff
            double diff = Math.Abs(angle - item.Angle);
            if (diff > 180) diff = 360 - diff; // Wrap around

            if (diff < minDiff)
            {
                minDiff = diff;
                closest = item;
            }
        }

        // 6. Highlight if within angular wedge (e.g. +/- 30 deg)
        //    Adjust threshold based on count (360 / count / 2)
        double threshold = (360.0 / ActionItems.Count) / 2.0;
        
        if (closest != null && minDiff < threshold)
        {
            HighlightItem(closest);
            CenterText = closest.FullName; // Update Center Text
        }
        else
        {
            HighlightItem(null);
            CenterText = "";
        }
    }

    private void HighlightItem(RadialMenuItem? item)
    {
        if (_highlightedItem != item)
        {
            if (_highlightedItem != null) _highlightedItem.IsHighlighted = false;
            _highlightedItem = item;
            if (_highlightedItem != null) _highlightedItem.IsHighlighted = true;
            OnPropertyChanged(nameof(HighlightedItem)); 
        }
    }

    public void ExecuteHighlighted()
    {
        if (_highlightedItem != null)
        {
            _ = ExecuteScript(_highlightedItem.ScriptPath);
        }
        else
        {
            // Missed selection -> Close
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    public async Task ExecuteScript(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            _logService?.AddLog($"[INFO] Menjalankan Action Script: {fileName}");

            // 1. Write Context to Temp File (Standardizing with BatchViewModel)
            // Even if empty, scripts might check for this file or shared libs might read it.
            var context = new { 
                SourceFolders = new System.Collections.Generic.List<object>(), // No source folders in Radial mode
                OutputBasePath = "",
                UseInput = false,
                UseOutput = false
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(context, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var tempPath = Path.Combine(Path.GetTempPath(), "bmachine_context.json");
            await File.WriteAllTextAsync(tempPath, json);
            
            _logService?.AddLog($"[INFO] Context written to: {tempPath}");
            
            // 2. Launch Process
            var ext = Path.GetExtension(path).ToLower();
            
            if (ext == ".jsx")
            {
                 // Photoshop Action
                 await RunJsxScript(path);
                 _logService?.AddLog("[SUCCESS] Script sent to Photoshop.");
            }
            else if (ext == ".pyw")
            {
                 // Python GUI Script
                 _logService?.AddLog($"[INFO] Launching Python Script: {fileName}");
                 
                 var startInfo = new ProcessStartInfo
                 {
                     FileName = path,
                     UseShellExecute = true // Let OS handle .pyw -> pythonw
                 };
                 Process.Start(startInfo);
                 _logService?.AddLog("[SUCCESS] Python script launched.");
            }
            else
            {
                 // Standard Python/Shell
                 _logService?.AddLog($"[INFO] Launching Script: {fileName}");
                 Process.Start(new ProcessStartInfo { FileName = "python", Arguments = $"\"{path}\"", UseShellExecute = true });
                 _logService?.AddLog("[SUCCESS] Script launched.");
            }
             
             RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[ERROR] Action Launch Failed: {ex.Message}");
        }
    }

    private async Task RunJsxScript(string scriptPath)
    {
        if (_database == null) return;
        
        var photoshopPath = await _database.GetAsync<string>("Configs.Master.PhotoshopPath");
        
        // Auto-Detect if missing
        if (string.IsNullOrEmpty(photoshopPath) || !File.Exists(photoshopPath))
        {
             var commonPaths = new[]
             {
                 @"C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe",
                 @"C:\Program Files\Adobe\Adobe Photoshop 2023\Photoshop.exe",
                 @"C:\Program Files\Adobe\Adobe Photoshop 2022\Photoshop.exe",
                 @"C:\Program Files\Adobe\Adobe Photoshop 2021\Photoshop.exe",
                 @"C:\Program Files\Adobe\Adobe Photoshop 2020\Photoshop.exe"
             };

             foreach (var path in commonPaths)
             {
                 if (File.Exists(path))
                 {
                     photoshopPath = path;
                     await _database.SetAsync("Configs.Master.PhotoshopPath", path); // Auto-save
                     _logService?.AddLog($"[Radial] Auto-detected Photoshop: {path}");
                     break;
                 }
             }
        }

        if (string.IsNullOrEmpty(photoshopPath) || !File.Exists(photoshopPath))
        {
             _logService?.AddLog("[ERROR] Photoshop path not set or invalid. Please Config in Settings.");
             Process.Start("explorer", "/select,\"" + scriptPath + "\""); // Fallback
             return;
        }
        
        _logService?.AddLog($"Running JSX in Photoshop: {Path.GetFileName(scriptPath)}");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = photoshopPath,
            Arguments = $"-r \"{scriptPath}\"",
            UseShellExecute = false
        };
        
        try
        {
             Process.Start(startInfo);
        }
        catch(Exception ex)
        {
             _logService?.AddLog($"[ERROR] Failed to launch Photoshop: {ex.Message}");
        }
    }

    public event Action? RequestClose;
}

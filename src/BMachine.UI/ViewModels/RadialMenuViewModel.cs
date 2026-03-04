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
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.ViewModels;

public partial class RadialMenuViewModel : ObservableObject, CommunityToolkit.Mvvm.Messaging.IRecipient<BMachine.UI.Messages.ScriptOrderChangedMessage>
{
    private readonly IDatabase? _database;
    private readonly Services.IProcessLogService? _logService;
    private readonly BMachine.Core.Platform.IPlatformService _platformService;
    
    private const int MaxVisibleItems = 7;
    private const double CanvasSize = 200.0;
    private const double CenterX = 100.0;
    private const double CenterY = 100.0;
    private const double ItemRadius = 48.0;    // Distance from center to item
    private const double ButtonSize = 32.0;
    private const double LabelOffset = 30.0;   // Extra distance for label from item center
    
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private Thickness _menuMargin; 

    public ObservableCollection<RadialMenuItem> MasterItems { get; } = new();
    public ObservableCollection<RadialMenuItem> ActionItems { get; } = new();
    
    /// <summary>
    /// Items currently visible on screen (subset based on current page)
    /// </summary>
    public ObservableCollection<RadialMenuItem> VisibleItems { get; } = new();

    // Manual property — not using [ObservableProperty] because HighlightItem 
    // needs direct field access to avoid MVVMTK0034 warnings
    private RadialMenuItem? _currentHighlightedItem;
    public RadialMenuItem? HighlightedItem
    {
        get => _currentHighlightedItem;
        private set => SetProperty(ref _currentHighlightedItem, value);
    }

    [ObservableProperty] private string _customScriptsPath = "";
    
    /// <summary>
    /// Current page: 0 = main, 1 = more
    /// </summary>
    [ObservableProperty] private int _currentPage;
    
    /// <summary>
    /// Whether the total item count exceeds MaxVisibleItems
    /// </summary>
    [ObservableProperty] private bool _hasMoreItems;

    /// <summary>
    /// Start angle of the highlighted wedge (for PieWedge binding)
    /// </summary>
    [ObservableProperty] private double _wedgeStart;

    /// <summary>
    /// Sweep angle of the highlighted wedge (for PieWedge binding)
    /// </summary>
    [ObservableProperty] private double _wedgeSweep;

    /// <summary>
    /// Whether the pie wedge should be visible
    /// </summary>
    [ObservableProperty] private bool _isWedgeVisible;

    /// <summary>
    /// Label text for currently highlighted item
    /// </summary>
    [ObservableProperty] private string _highlightLabel = "";

    /// <summary>
    /// X position for the highlight label
    /// </summary>
    [ObservableProperty] private double _highlightLabelX;

    /// <summary>
    /// Y position for the highlight label
    /// </summary>
    [ObservableProperty] private double _highlightLabelY;

    /// <summary>
    /// Whether label is on left side (affects alignment)
    /// </summary>
    [ObservableProperty] private bool _isLabelOnLeft;

    public RadialMenuViewModel(IDatabase? database, Services.IProcessLogService? logService = null, BMachine.Core.Platform.IPlatformService? platformService = null)
    {
        _database = database;
        _logService = logService;
        _platformService = platformService ?? BMachine.Core.Platform.PlatformServiceFactory.Get();
        
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.RegisterAll(this);

        LoadScripts();
    }
    
    public void Receive(BMachine.UI.Messages.ScriptOrderChangedMessage message)
    {
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

        HasMoreItems = ActionItems.Count > MaxVisibleItems;
        CurrentPage = 0;
        BuildVisibleItems();
    }

    /// <summary>
    /// Rebuild VisibleItems based on CurrentPage
    /// </summary>
    private void BuildVisibleItems()
    {
        VisibleItems.Clear();
        HighlightItem(null);

        if (ActionItems.Count <= MaxVisibleItems)
        {
            // All items fit — no More button needed
            foreach (var item in ActionItems)
            {
                item.IsNavigation = false;
                item.NavigationType = "";
                VisibleItems.Add(item);
            }
        }
        else if (CurrentPage == 0)
        {
            // Page 1: first 7 items + More button
            for (int i = 0; i < Math.Min(MaxVisibleItems, ActionItems.Count); i++)
            {
                ActionItems[i].IsNavigation = false;
                ActionItems[i].NavigationType = "";
                VisibleItems.Add(ActionItems[i]);
            }
            
            // Add "More" navigation item
            VisibleItems.Add(new RadialMenuItem
            {
                FullName = "More",
                ShortName = "⋯",
                IsNavigation = true,
                NavigationType = "more"
            });
        }
        else
        {
            // Page 2: remaining items + Back button
            for (int i = MaxVisibleItems; i < ActionItems.Count; i++)
            {
                ActionItems[i].IsNavigation = false;
                ActionItems[i].NavigationType = "";
                VisibleItems.Add(ActionItems[i]);
            }
            
            // Add "Back" navigation item
            VisibleItems.Add(new RadialMenuItem
            {
                FullName = "Back",
                ShortName = "←",
                IsNavigation = true,
                NavigationType = "back"
            });
        }

        ArrangeItems();
    }

    /// <summary>
    /// Arrange items in a full circle (360°), evenly spaced.
    /// The "More" / "Back" button is always placed at the bottom (270° in trig = South).
    /// </summary>
    private void ArrangeItems()
    {
        if (VisibleItems.Count == 0) return;

        int count = VisibleItems.Count;
        double baseAngleStep = 360.0 / count;
        double angleStep = Math.Min(baseAngleStep, 45.0);

        for (int i = 0; i < count; i++)
        {
            var item = VisibleItems[i];
            double angleDeg;

            if (item.IsNavigation)
            {
                // Navigation items (More/Back) always at bottom (180° in our 0=Top system)
                angleDeg = 180.0;
            }
            else
            {
                // Distribute non-nav items evenly
                if (HasMoreItems)
                {
                    // Reserve bottom slot for nav, distribute others across remaining space
                    int nonNavCount = count - 1;
                    double totalSpan = (nonNavCount - 1) * angleStep;
                    
                    // Find this item's index among non-nav items
                    int nonNavIndex = 0;
                    for (int j = 0; j < i; j++)
                    {
                        if (!VisibleItems[j].IsNavigation) nonNavIndex++;
                    }
                    
                    // Start from top, go clockwise, center the group
                    double startAngle = -totalSpan / 2.0;
                    angleDeg = startAngle + nonNavIndex * angleStep;
                }
                else
                {
                    // No nav items — simple even distribution centered on top
                    double totalSpan = (count - 1) * angleStep;
                    double startAngle = -totalSpan / 2.0;
                    angleDeg = startAngle + i * angleStep;
                }
            }

            // Normalize angle
            double normalizedAngle = angleDeg;
            while (normalizedAngle < 0) normalizedAngle += 360;
            while (normalizedAngle >= 360) normalizedAngle -= 360;
            item.Angle = normalizedAngle;

            // Convert "0=Top clockwise" to standard trig for position calculation
            double trigAngleDeg = normalizedAngle - 90; // 0=Top → -90 in trig (Right=0)
            double trigAngleRad = trigAngleDeg * (Math.PI / 180.0);

            // Item position (center of button)
            double itemCenterX = CenterX + ItemRadius * Math.Cos(trigAngleRad);
            double itemCenterY = CenterY + ItemRadius * Math.Sin(trigAngleRad);
            item.X = itemCenterX - ButtonSize / 2;
            item.Y = itemCenterY - ButtonSize / 2;

            // Label position (further out from center)
            double labelDist = ItemRadius + LabelOffset;
            double labelCenterX = CenterX + labelDist * Math.Cos(trigAngleRad);
            double labelCenterY = CenterY + labelDist * Math.Sin(trigAngleRad);
            item.LabelX = labelCenterX;
            item.LabelY = labelCenterY;

            // Determine label side based on angle
            // Left side: angle 90-270 (items on the left half)
            bool isLeftSide = normalizedAngle > 90 && normalizedAngle < 270;
            item.LabelSide = isLeftSide ? "Left" : "Right";

            // Wedge angles for pie highlight
            double highlightSweep = angleStep * 0.75; // Narrow the sweep width
            item.WedgeStartAngle = normalizedAngle - highlightSweep / 2.0;
            item.WedgeSweepAngle = highlightSweep;
        }
    }

    public void UpdateHighlight(Point mousePos, Size windowSize)
    {
        double cx = windowSize.Width / 2;
        double cy = windowSize.Height / 2;

        double dx = mousePos.X - cx;
        double dy = mousePos.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // Deadzone: center (< 15px) or outside window bounds (> 95px)
        if (dist < 15 || dist > 95)
        {
            HighlightItem(null);
            return;
        }

        // Calculate angle (0 = North/Top, clockwise)
        double angle = Math.Atan2(dy, dx) * (180 / Math.PI);
        angle += 90;
        if (angle < 0) angle += 360;

        // Find closest item
        RadialMenuItem? closest = null;
        double minDiff = double.MaxValue;

        foreach (var item in VisibleItems)
        {
            double diff = Math.Abs(angle - item.Angle);
            if (diff > 180) diff = 360 - diff;

            if (diff < minDiff)
            {
                minDiff = diff;
                closest = item;
            }
        }

        // Check angular threshold
        double angleStep = Math.Min(360.0 / VisibleItems.Count, 45.0);
        double threshold = angleStep / 2.0;
        
        if (closest != null && minDiff < threshold)
        {
            // Handle navigation items on hover
            if (closest.IsNavigation && closest != _currentHighlightedItem)
            {
                if (closest.NavigationType == "more")
                {
                    CurrentPage = 1;
                    BuildVisibleItems();
                    return;
                }
                else if (closest.NavigationType == "back")
                {
                    CurrentPage = 0;
                    BuildVisibleItems();
                    return;
                }
            }

            HighlightItem(closest);
        }
        else
        {
            HighlightItem(null);
        }
    }

    private void HighlightItem(RadialMenuItem? item)
    {
        if (_currentHighlightedItem != item)
        {
            if (_currentHighlightedItem != null) _currentHighlightedItem.IsHighlighted = false;
            _currentHighlightedItem = item;
            
            if (_currentHighlightedItem != null)
            {
                _currentHighlightedItem.IsHighlighted = true;
                
                // Update wedge
                WedgeStart = _currentHighlightedItem.WedgeStartAngle;
                WedgeSweep = _currentHighlightedItem.WedgeSweepAngle;
                IsWedgeVisible = true;
                
                // Update label
                HighlightLabel = _currentHighlightedItem.FullName;
                HighlightLabelX = _currentHighlightedItem.LabelX;
                HighlightLabelY = _currentHighlightedItem.LabelY;
                IsLabelOnLeft = _currentHighlightedItem.LabelSide == "Left";
            }
            else
            {
                IsWedgeVisible = false;
                HighlightLabel = "";
            }
            
            OnPropertyChanged(nameof(HighlightedItem)); 
        }
    }

    /// <summary>
    /// Reset to page 1 when menu is shown
    /// </summary>
    public void ResetPage()
    {
        CurrentPage = 0;
        BuildVisibleItems();
    }

    public void ExecuteHighlighted()
    {
        if (_currentHighlightedItem != null && !_currentHighlightedItem.IsNavigation)
        {
            _ = ExecuteScript(_currentHighlightedItem.ScriptPath);
        }
        else
        {
            // Missed selection -> Close
            RequestClose?.Invoke();
        }
    }

    // ============================================================
    // Script Loading & Metadata (unchanged from original)
    // ============================================================

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
            
            if (!string.IsNullOrEmpty(config.IconKey))
            {
                 if (Application.Current!.TryGetResource(config.IconKey, null, out var res) && res is Avalonia.Media.StreamGeometry geom)
                 {
                     icon = geom;
                 }
            }
            
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
            Order = order,
            Icon = icon
        });
    }

    private void LoadMetadata()
    {
        try
        {
            var metaPath = Path.Combine(BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory(), "scripts.json");
            
            var folder = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            
            if (!File.Exists(metaPath))
            {
                var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
                if (File.Exists(defaultPath))
                {
                    File.Copy(defaultPath, metaPath);
                }
            }

            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    _scriptAliases = new(); 
                }
            }
        }
        catch { _scriptAliases = new(); }
    }

    // Kept for backward compat — no longer used in UI but still in logic
    [ObservableProperty]
    private string _centerText = "";

    [RelayCommand]
    public async Task ExecuteScript(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            _logService?.AddLog($"[INFO] Menjalankan Action Script: {fileName}");

            var context = new { 
                SourceFolders = new System.Collections.Generic.List<object>(),
                OutputBasePath = "",
                UseInput = false,
                UseOutput = false
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(context, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var tempPath = Path.Combine(Path.GetTempPath(), "bmachine_context.json");
            await File.WriteAllTextAsync(tempPath, json);
            
            _logService?.AddLog($"[INFO] Context written to: {tempPath}");
            
            var ext = Path.GetExtension(path).ToLower();
            
            if (ext == ".jsx")
            {
                 _logService?.AddLog($"[INFO] Launching JSX: {fileName}");
                 
                 var photoshopPath = await _database.GetAsync<string>("Configs.Master.PhotoshopPath");
                 if (string.IsNullOrEmpty(photoshopPath) || !_platformService.IsExecutableValid(photoshopPath))
                 {
                      _logService?.AddLog($"[ERROR] Photoshop path is invalid or not set. Please set it in Settings.\nPath checked: {photoshopPath}");
                      RequestClose?.Invoke();
                      return;
                 }
                 
                 _platformService.RunJsxInPhotoshop(path, photoshopPath);
                 _logService?.AddLog("[SUCCESS] JSX sent to Photoshop.");
            }
            else if (ext == ".pyw")
            {
                 _logService?.AddLog($"[INFO] Launching Python Script: {fileName}");
                 _platformService.RunPythonScript(path, true);
                 _logService?.AddLog("[SUCCESS] Python script launched.");
            }
            else
            {
                 _logService?.AddLog($"[INFO] Launching Script: {fileName}");
                 _platformService.RunPythonScript(path, false);
                 _logService?.AddLog("[SUCCESS] Script launched.");
            }
             
             RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[ERROR] Action Launch Failed: {ex.Message}");
        }
    }

    public event Action? RequestClose;
}

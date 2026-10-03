using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Services;

public class PreviewWindowSettings
{
    public const string DefaultShortcutNext = "Right";
    public const string DefaultShortcutPrevious = "Left";
    public const string DefaultShortcutPhotoshop = "P";
    public const string DefaultShortcutRotate = "R";
    public const string DefaultShortcutFitScreen = "F";

    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 600;
    public double Zoom { get; set; } = 1.0;
    public string PhotoshopPath { get; set; } = "";
    
    // Shortcuts
    public string ShortcutNext { get; set; } = DefaultShortcutNext;
    public string ShortcutPrevious { get; set; } = DefaultShortcutPrevious;
    public string ShortcutPhotoshop { get; set; } = DefaultShortcutPhotoshop;
    public string ShortcutRotate { get; set; } = DefaultShortcutRotate;
    public string ShortcutFitScreen { get; set; } = DefaultShortcutFitScreen;
    public Dictionary<string, string> EditorShortcuts { get; set; } = EditorShortcutMap.CreateDefaults();
    
    // Photopea settings
    public string PhotopeaSaveFormat { get; set; } = "png"; // "png" or "psd"
    public bool AutoAdvanceOnSave { get; set; } = false;

    // Background settings
    public int BackgroundType { get; set; } = 0; // 0: Default, 1: Checkerboard, 2: Solid
    public string SolidColorHex { get; set; } = "#00FF00";
    public string CheckerColor1 { get; set; } = "#333333";
    public string CheckerColor2 { get; set; } = "#4D4D4D";
    // Editor native masih beta; default off agar user biasa tidak melihat mode Edit.
    public bool EditorBetaMode { get; set; } = false;
    // ===== Editor (mask/selection) settings =====
    public int ActiveEditTool { get; set; } = -1; // -1 = belum pernah; index EditToolKind
    public int EditorBrushSize { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultBrushSize;
    public double EditorBrushHardness { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultBrushHardness;
    public double EditorBrushOpacity { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultBrushOpacity;
    public double EditorBrushFlow { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultBrushFlow;
    public bool EditorBrushRestore { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultBrushRestoreMode;
    public int EditorWandTolerance { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultWandTolerance;
    public bool EditorWandContiguous { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultWandContiguous;
    public bool EditorWandSampleAlpha { get; set; } = true;
    public double EditorSelectionFeather { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultSelectionFeather;
    public bool EditorAntiAlias { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultAntiAliasSelection;
    public int EditorUndoSteps { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultUndoSteps;
    public int EditorUndoMemoryMb { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultUndoMemoryLimitMb;
    public string EditorRefineHairModel { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultRefineHairModel;
    public int EditorRefineHairBandRadius { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultRefineHairBandRadius;
    public int EditorRefineHairFeather { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultRefineHairFeather;
    public bool EditorSaveAsCopy { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultSaveEditorAsCopy;
    public string EditorOutputSuffix { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultEditorOutputSuffix;
    public int EditorSelectionMode { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultSelectionMode;
    public bool EditorWand8Connected { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultWand8Connected;
    public int EditorSelGrowPx { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultSelGrowPx;
    public int EditorRefineEdgeSize { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultRefineEdgeSize;
    public int EditorRefineEdgeFeather { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultRefineEdgeFeather;
    public double EditorPenPathThickness { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultPenPathThickness;
    public string EditorPenPathColor { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultPenPathColor;
    public bool EditorShowRightPanel { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultShowRightPanel;
    public string EditorWorkspaceFocus { get; set; } = "All";
    public string EditorToolsDock { get; set; } = "Top";
    public string EditorPropertiesDock { get; set; } = "Right";
    public string EditorHistoryDock { get; set; } = "Right";
    public string EditorLayersDock { get; set; } = "Right";
    /// <summary>Lebar dock kanan (px) — bisa diubah dengan menyeret tepinya.</summary>
    public double EditorRightDockWidth { get; set; } = 240;
    /// <summary>Urutan panel di dalam sisi dock (nama setting: Tools/Properties/History/Layers).</summary>
    public List<string> EditorDockOrder { get; set; } = new() { "Tools", "Properties", "History", "Layers" };
    // Visibilitas panel editor (menu Window): key -> tampil. Key yang tidak ada = tampil (default ala Photoshop).
    public Dictionary<string, bool> EditorPanelVisibility { get; set; } = new();
    // Prefer two columns for a side-docked Tools rail; Auto still falls back when one column will clip.
    public bool EditorToolsPreferTwoColumns { get; set; } = false;
    /// <summary>Visibilitas tiap tool di rail (key = tag tool, mis. "Move","Pen","QuickMask"). Tidak ada = tampil.</summary>
    public Dictionary<string, bool> EditorToolVisibility { get; set; } = new();
    public int EditorAntsAnimationMs { get; set; } = PixelcutCompact.Models.EditorSettings.DefaultAntsAnimationMs;
    // Bahasa UI mode edit: "id" (default) atau "en".
    public string EditorLanguage { get; set; } = "id";

    public void RestoreKeyboardShortcutDefaults()
    {
        ShortcutNext = DefaultShortcutNext;
        ShortcutPrevious = DefaultShortcutPrevious;
        ShortcutPhotoshop = DefaultShortcutPhotoshop;
        ShortcutRotate = DefaultShortcutRotate;
        ShortcutFitScreen = DefaultShortcutFitScreen;
        EditorShortcuts = EditorShortcutMap.CreateDefaults();
    }

    private static string GetPath() => Path.Combine(AppContext.BaseDirectory, "preview_settings.json");

    public static PreviewWindowSettings Load()
    {
        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<PreviewWindowSettings>(json) ?? new PreviewWindowSettings();
                settings.EditorShortcuts = EditorShortcutMap.MergeWithDefaults(settings.EditorShortcuts);
                return settings;
            }
        }
        catch { }
        return new PreviewWindowSettings();
    }

    public void Save()
    {
        try
        {
            EditorShortcuts = EditorShortcutMap.MergeWithDefaults(EditorShortcuts);
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(GetPath(), json);
        }
        catch { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

/// <summary>
/// Layout mode edit ala Photoshop: menu bar → options bar → rail tools →
/// kanvas tengah → dock kanan → status bar.
/// Docking ringan berbasis Grid/StackPanel bawaan Avalonia: tiap panel bisa dipindah
/// ke sisi Top/Left/Right/Bottom lewat combo "Dock" (posisi disimpan di settings).
/// Logika session/mask/composite tidak disentuh di sini (murni presentation).
/// </summary>
public partial class PreviewWindow
{
    /// <summary>Photoshop: Tab menyembunyikan/menampilkan semua panel.</summary>
    private bool _tabPanelsHidden;
    private bool _workspaceLayoutUpdating;

    // Visibilitas per panel dari menu Window. Key yang tidak ada = tampil (default ala Photoshop).
    // Sumber kebenaran = _settings.EditorPanelVisibility supaya pilihan bertahan lintas sesi.
    private Dictionary<string, bool> PanelVisibility => _settings.EditorPanelVisibility;

    private static readonly (string Name, string Key)[] PanelMenuItems =
    {
        ("MiWindowToolBox", "toolrail"),
        ("MiWindowOptionsBar", "optionsbar"),
        ("MiWindowProperties", "properties"),
        ("MiWindowHistory", "history"),
        ("MiWindowLayers", "layers"),
        ("MiWindowRightDock", "rightdock"),
    };

    // Panel yang bisa di-dock: nama kontrol, key menu Window, nama setting posisi.
    private static readonly (string Name, string Key, string Setting)[] DockedPanels =
    {
        ("PanelToolRail", "toolrail", "Tools"),
        ("PanelProperties", "properties", "Properties"),
        ("PanelHistory", "history", "History"),
        ("PanelDocLayers", "layers", "Layers"),
    };

    // Grup tool di rail (dipakai untuk tata letak 1/2 kolom).
    private static readonly string[] ToolGroupNames =
        { "ToolSelectionGroup", "ToolPaintGroup", "ToolViewGroup", "ToolAiGroup" };

    /// <summary>Menu Window: centang = panel tampil, tidak centang = panel disembunyikan.</summary>
    private void OnTogglePanelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        var key = item.Tag as string;
        if (string.IsNullOrEmpty(key)) return;

        // ToggleType="CheckBox" sudah membalik IsChecked SEBELUM event Click naik
        // (lihat DefaultMenuInteractionHandler), jadi IsChecked di sini = nilai baru.
        PanelVisibility[key] = item.IsChecked;
        _settings.Save();
        ApplyEditorDockVisibility();
    }

    private void ConfigureEditorWorkspace()
    {
        SyncPanelMenuChecks();
        SyncDockCombos();
        if (this.FindControl<ToggleButton>("BtnToolsTwoColumns") is { } t) t.IsChecked = _settings.EditorToolsPreferTwoColumns;
        ArrangeDockedPanels();
        SetVisible("DockFocusBar", true);
        ApplyEditorDockVisibility();
    }

    /// <summary>Toggle "2 kolom" untuk rail Tools.</summary>
    private void OnToolsTwoColumnsChanged(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || sender is not ToggleButton tb) return;
        _settings.EditorToolsPreferTwoColumns = tb.IsChecked == true;
        _settings.Save();
        ConfigureToolOrientation(DockPositionOf("Tools"));
    }

    /// <summary>Samakan IsChecked tiap item menu Window dengan visibilitas tersimpan.</summary>
    private void SyncPanelMenuChecks()
    {
        foreach (var (name, key) in PanelMenuItems)
        {
            if (this.FindControl<MenuItem>(name) is { } mi)
                mi.IsChecked = Visible(key);
        }
    }

    private void SyncDockCombos()
    {
        _workspaceLayoutUpdating = true;
        SetDockCombo("CboToolsDock", DockPositionOf("Tools"));
        SetDockCombo("CboPropertiesDock", DockPositionOf("Properties"));
        SetDockCombo("CboHistoryDock", DockPositionOf("History"));
        SetDockCombo("CboLayersDock", DockPositionOf("Layers"));
        _workspaceLayoutUpdating = false;
    }

    /// <summary>Pindahkan tiap panel ke host sesuai posisi tersimpan.</summary>
    private void ArrangeDockedPanels()
    {
        var top = this.FindControl<StackPanel>("DockTopPanel");
        var left = this.FindControl<StackPanel>("DockLeftPanel");
        var right = this.FindControl<StackPanel>("DockRightPanel");
        var bottom = this.FindControl<StackPanel>("DockBottomPanel");
        if (top == null || left == null || right == null || bottom == null) return;

        foreach (var (name, _, setting) in DockedPanels)
        {
            if (this.FindControl<Control>(name) is not { } panel) continue;

            // Lepas dari parent mana pun (host dock ATAU grid awal), lalu pindah ke host tujuan.
            if (panel.Parent is Panel currentParent) currentParent.Children.Remove(panel);

            FindDockHost(DockPositionOf(setting), top, left, right, bottom).Children.Add(panel);
        }

        ConfigureToolOrientation(DockPositionOf("Tools"));
    }

    private static StackPanel FindDockHost(string position, StackPanel top, StackPanel left, StackPanel right, StackPanel bottom)
        => position switch
        {
            "Top" => top,
            "Left" => left,
            "Bottom" => bottom,
            _ => right
        };

    /// <summary>
    /// Rail tools selalu vertikal (hanya boleh Left/Right). Bisa 1 atau 2 kolom:
    /// 2 kolom kalau user mengaktifkannya (EditorToolsPreferTwoColumns) atau kalau
    /// satu kolom tidak muat di tinggi tersedia (kebijakan ToolDockColumnPolicy).
    /// </summary>
    private void ConfigureToolOrientation(string position)
    {
        var rail = this.FindControl<Border>("PanelToolRail");
        if (rail == null) return;

        rail.Height = double.NaN;
        rail.Padding = new Thickness(8, 10);
        rail.BorderThickness = position == "Right"
            ? new Thickness(1, 0, 0, 0)
            : new Thickness(0, 0, 1, 0);

        var grid = this.FindControl<Grid>("PanelToolContentGrid");
        var left = this.FindControl<StackPanel>("ToolColumnLeft");
        var right = this.FindControl<StackPanel>("ToolColumnRight");
        var groups = ToolGroupNames.Select(n => this.FindControl<StackPanel>(n)).Where(g => g != null).Select(g => g!).ToArray();
        if (grid == null || left == null || right == null || groups.Length == 0) return;

        // Pastikan semua grup berada di kolom kiri dulu (agar bisa diukur & dipindah).
        foreach (var g in groups)
        {
            if (g.Parent is Panel pp && pp != left) pp.Children.Remove(g);
            if (!left.Children.Contains(g)) left.Children.Add(g);
        }

        foreach (var g in groups) { g.Orientation = Orientation.Vertical; g.Spacing = 2; SetToolButtonSize(g, 36); }

        double oneColumnHeight = groups.Sum(g => { g.Measure(Size.Infinity); return g.DesiredSize.Height; })
                                 + Math.Max(0, groups.Length - 1) * 2 + 20;
        double available = GetAvailableToolHeight(position);
        bool two = ToolDockColumnPolicy.UseTwoColumns(available, oneColumnHeight, _settings.EditorToolsPreferTwoColumns);

        int size = two ? 34 : 36;
        int btnSpacing = two ? 5 : 2;      // jarak antar tombol (2 kolom = lebih lega)
        int groupSpacing = two ? 9 : 2;    // jarak antar grup tool
        foreach (var g in groups) { g.Spacing = btnSpacing; SetToolButtonSize(g, size); }

        // Divider hanya relevan saat 1 kolom; sembunyikan saat 2 kolom.
        foreach (var b in left.Children.OfType<Border>()) b.IsVisible = !two;

        right.Children.Clear();
        if (two)
        {
            foreach (var g in new[] { "ToolViewGroup", "ToolAiGroup" })
                if (this.FindControl<StackPanel>(g) is { } gp && left.Children.Contains(gp)) { left.Children.Remove(gp); right.Children.Add(gp); }
            right.IsVisible = true;
            left.Spacing = groupSpacing;
            right.Spacing = groupSpacing;
            rail.Width = size * 2 + 26;
        }
        else
        {
            right.IsVisible = false;
            left.Spacing = 2;
            rail.Width = size + 20;
        }
    }

    private double GetAvailableToolHeight(string position)
    {
        var host = this.FindControl<Border>(position == "Right" ? "PanelRightEditor" : "DockLeftHost");
        double h = host?.Bounds.Height ?? 0;
        if (h <= 0 && this.FindControl<Border>("PanelToolRail") is { } rail) h = rail.Bounds.Height;
        return h;
    }

    private static void SetToolButtonSize(StackPanel group, double size)
    {
        foreach (var button in group.Children.OfType<Button>())
        {
            button.Width = size;
            button.Height = size;
        }
    }

    private void ApplyEditorDockVisibility()
    {
        if (!_editMode) return;

        // Master dock kanan: menu "Dock Kanan" + tombol Panel (EditorShowRightPanel).
        bool rightMaster = Visible("rightdock") && _settings.EditorShowRightPanel;

        bool Shown(string key, string setting)
        {
            if (_tabPanelsHidden) return false;
            if (!Visible(key)) return false;
            if (DockPositionOf(setting) == "Right" && !rightMaster) return false;
            return true;
        }

        SetVisible("PanelToolRail", Shown("toolrail", "Tools"));
        SetVisible("PanelProperties", Shown("properties", "Properties"));
        SetVisible("PanelHistory", Shown("history", "History"));
        SetVisible("PanelDocLayers", Shown("layers", "Layers"));

        SetVisible("PanelOptionsBar", !_tabPanelsHidden && Visible("optionsbar"));
        SetVisible("PanelMenuBar", true);

        bool AnyVisibleAt(string position) => DockedPanels.Any(p =>
            DockPositionOf(p.Setting) == position && Shown(p.Key, p.Setting));

        // Host atas/kiri/bawah hanya tampil bila ada panel yang memakainya.
        SetVisible("DockTopHost", AnyVisibleAt("Top"));
        SetVisible("DockLeftHost", AnyVisibleAt("Left"));
        SetVisible("DockBottomHost", AnyVisibleAt("Bottom"));

        // Host kanan = PanelRightEditor (Border). Lebar mengikuti isi: panel penuh = 240,
        // hanya rail tools = 70, kosong = sembunyi.
        if (this.FindControl<Border>("PanelRightEditor") is { } right)
        {
            bool rightHasNonTool = DockedPanels.Any(p => p.Setting != "Tools" &&
                DockPositionOf(p.Setting) == "Right" && Shown(p.Key, p.Setting));
            bool rightHasAny = AnyVisibleAt("Right");
            right.Width = rightHasNonTool ? 240 : (rightHasAny ? 70 : 0);
            right.IsVisible = rightHasAny;
        }

        SetVisible("DockFocusBar", true);
    }

    private bool Visible(string key) => !PanelVisibility.TryGetValue(key, out var on) || on;

    /// <summary>Posisi dock tersimpan untuk sebuah panel (Tools hanya Left/Right).</summary>
    private string DockPositionOf(string setting) => setting switch
    {
        "Tools" => NormalizeToolDock(_settings.EditorToolsDock),
        "Properties" => NormalizeDock(_settings.EditorPropertiesDock),
        "History" => NormalizeDock(_settings.EditorHistoryDock),
        _ => NormalizeDock(_settings.EditorLayersDock)
    };

    private void SetEditorWorkspaceActive(bool active)
    {
        SetVisible("DockFocusBar", active);
        SetVisible("PanelMenuBar", active);
        if (active)
        {
            ApplyEditorDockVisibility();
            return;
        }

        SetVisible("PanelRightEditor", false);
        SetVisible("DockTopHost", false);
        SetVisible("DockLeftHost", false);
        SetVisible("DockBottomHost", false);
        SetVisible("PanelToolRail", false);
        SetVisible("PanelOptionsBar", false);
    }

    /// <summary>Photoshop: Tab = sembunyikan/tampilkan rail + options bar + dock kanan.</summary>
    private void ToggleEditorPanels()
    {
        if (!_editMode) return;
        _tabPanelsHidden = !_tabPanelsHidden;
        ApplyEditorDockVisibility();
    }

    private void OnEditorPanelDockChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_editMode || _workspaceLayoutUpdating || sender is not ComboBox combo) return;
        var position = NormalizeDock(SelectedTag(combo));
        switch (combo.Tag?.ToString())
        {
            case "Tools": _settings.EditorToolsDock = NormalizeToolDock(position); break;
            case "Properties": _settings.EditorPropertiesDock = position; break;
            case "History": _settings.EditorHistoryDock = position; break;
            case "Layers": _settings.EditorLayersDock = position; break;
            default: return;
        }

        _settings.Save();
        ArrangeDockedPanels();
        ApplyEditorDockVisibility();
    }

    private void OnResetEditorWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        _settings.EditorShowRightPanel = true;
        _settings.EditorToolsDock = "Left";
        _settings.EditorPropertiesDock = "Right";
        _settings.EditorHistoryDock = "Right";
        _settings.EditorLayersDock = "Right";
        _settings.Save();
        ConfigureEditorWorkspace();
    }

    private void SetDockCombo(string name, string tag)
    {
        if (this.FindControl<ComboBox>(name) is not { } combo) return;
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string SelectedTag(ComboBox combo)
        => combo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? string.Empty : string.Empty;

    private static string NormalizeDock(string? position) => position?.Trim().ToUpperInvariant() switch
    {
        "TOP" => "Top",
        "LEFT" => "Left",
        "BOTTOM" => "Bottom",
        _ => "Right"
    };

    /// <summary>Rail tools hanya boleh kiri/kanan (selalu vertikal); default kiri.</summary>
    private static string NormalizeToolDock(string? position) => position?.Trim().ToUpperInvariant() switch
    {
        "RIGHT" => "Right",
        _ => "Left"
    };
}

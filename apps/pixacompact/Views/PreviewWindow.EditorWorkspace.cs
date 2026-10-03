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
        if (this.FindControl<ToggleButton>("BtnToolsTwoColumns") is { } t) t.IsChecked = _settings.EditorToolsPreferTwoColumns;
        WireDockHosts();
        ArrangeDockedPanels();
        ApplyEditorDockVisibility();
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

    private void SyncDockCombos() { }   // combo lama sudah diganti menu ⋮ + drag

    /// <summary>Pindahkan tiap panel ke host sesuai posisi tersimpan, urut sesuai EditorDockOrder.</summary>
    private void ArrangeDockedPanels()
    {
        var top = this.FindControl<StackPanel>("DockTopPanel");
        var left = this.FindControl<StackPanel>("DockLeftPanel");
        var right = this.FindControl<StackPanel>("DockRightPanel");
        var bottom = this.FindControl<StackPanel>("DockBottomPanel");
        if (top == null || left == null || right == null || bottom == null) return;

        // Kosongkan host dulu agar urutan bisa dibangun ulang persis.
        top.Children.Clear(); left.Children.Clear(); right.Children.Clear(); bottom.Children.Clear();

        var order = EffectiveDockOrder();
        foreach (var setting in order)
        {
            var match = DockedPanels.FirstOrDefault(d => d.Setting == setting);
            if (match.Name == null) continue;
            if (IsFloating(match.Key)) continue;   // panel melayang → biarkan di jendelanya
            if (this.FindControl<Control>(match.Name) is not { } panel) continue;

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

        // Terapkan visibilitas tool custom (user boleh menyembunyikan tool tertentu).
        ApplyToolVisibility();

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

        // Hanya hitung grup yang punya tool terlihat.
        double oneColumnHeight = groups.Where(HasVisibleTool)
                                      .Sum(g => { g.Measure(Size.Infinity); return g.DesiredSize.Height; })
                                 + Math.Max(0, groups.Count(HasVisibleTool) - 1) * 2 + 20;
        double available = GetAvailableToolHeight(position);
        bool two = _settings.EditorToolsPreferTwoColumns;
        // Auto: hanya 2 kolom bila 1 kolom tidak muat.
        if (!two && available > 0 && oneColumnHeight > available) two = true;

        int size = two ? 34 : 36;
        int btnSpacing = two ? 7 : 2;      // jarak antar tombol (2 kolom = lebih lega)
        int groupSpacing = two ? 7 : 2;    // samakan dgn antar-tombol → 2 kolom persis sama tinggi (simetris)
        foreach (var g in groups) { g.Spacing = btnSpacing; SetToolButtonSize(g, size); }

        // Divider hanya relevan saat 1 kolom; sembunyikan saat 2 kolom.
        foreach (var b in left.Children.OfType<Border>()) b.IsVisible = !two;

        right.Children.Clear();
        if (two)
        {
            // Bagi rata 7/7: kiri = grup Seleksi; kanan = Paint + View + AI.
            // Kedua kolom jadi setinggi sama → simetris.
            foreach (var n in new[] { "ToolPaintGroup", "ToolViewGroup", "ToolAiGroup" })
                if (this.FindControl<StackPanel>(n) is { } gp && left.Children.Contains(gp)) { left.Children.Remove(gp); right.Children.Add(gp); }
            right.IsVisible = true;
            right.Margin = new Thickness(12, 0, 0, 0);   // jarak antar kolom
            left.Spacing = groupSpacing;
            right.Spacing = groupSpacing;
            rail.Width = size * 2 + 46;
        }
        else
        {
            right.IsVisible = false;
            right.Margin = new Thickness(0);
            left.Spacing = 2;
            rail.Width = size + 20;
        }
    }

    /// <summary>Daftar tombol tool di rail: nama kontrol → tag (key visibilitas).</summary>
    private static readonly (string Control, string Key)[] RailToolButtons =
    {
        ("BtnToolMove", "Move"),
        ("BtnToolRectMarquee", "RectMarquee"),
        ("BtnToolEllipseMarquee", "EllipseMarquee"),
        ("BtnToolLasso", "Lasso"),
        ("BtnToolPolyLasso", "PolyLasso"),
        ("BtnToolWand", "MagicWand"),
        ("BtnToolPen", "Pen"),
        ("BtnToolBrush", "Brush"),
        ("BtnToolEraser", "Eraser"),
        ("BtnToolRefineEdge", "RefineEdge"),
        ("BtnToolPan", "Pan"),
        ("BtnQuickMask", "QuickMask"),
        ("BtnMaskView", "MaskView"),
        ("BtnRefineHair", "RefineHair"),
    };

    private bool ToolShown(string key) =>
        !_settings.EditorToolVisibility.TryGetValue(key, out var on) || on;

    private bool HasVisibleTool(StackPanel group)
    {
        foreach (var c in group.Children)
        {
            if (c is Button b && b.Tag is string tag && ToolShown(tag)) return true;
            if (c is StackPanel inner && HasVisibleTool(inner)) return true;
        }
        return false;
    }

    /// <summary>Terapkan visibilitas tool custom; grup tanpa tool terlihat ikut disembunyikan.</summary>
    private void ApplyToolVisibility()
    {
        foreach (var (control, key) in RailToolButtons)
            if (this.FindControl<Button>(control) is { } b) b.IsVisible = ToolShown(key);

        // Grup kosong disembunyikan; grup tanpa tool terlihat disembunyikan.
        foreach (var n in ToolGroupNames)
            if (this.FindControl<StackPanel>(n) is { } g) g.IsVisible = HasVisibleTool(g);
    }

    /// <summary>Set jumlah kolom rail tools (1 atau 2) eksplisit, lalu simpan + render ulang.</summary>
    private void SetToolsColumns(bool two)
    {
        _settings.EditorToolsPreferTwoColumns = two;
        _settings.Save();
        ConfigureToolOrientation(DockPositionOf("Tools"));
    }

    /// <summary>Tampilkan/sembunyikan satu tool; bila tool aktif disembunyikan, pindah ke Pan.</summary>
    private void SetToolVisible(string key, bool visible)
    {
        _settings.EditorToolVisibility[key] = visible;
        _settings.Save();

        // Tool yang disembunyikan tidak boleh tetap aktif → pindah ke Pan.
        if (!visible)
        {
            foreach (var (control, k) in RailToolButtons)
            {
                if (k != key) continue;
                if (this.FindControl<Button>(control) is { } b && b.Tag is string tag
                    && Enum.TryParse<EditToolKind>(tag, out var kind) && _activeTool == kind)
                    SetActiveTool(EditToolKind.Pan);
            }
        }
        ConfigureToolOrientation(DockPositionOf("Tools"));
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
            if (IsFloating(key)) return true;    // melayang → panel hidup di jendelanya sendiri
            if (_tabPanelsHidden) return false;
            if (!Visible(key)) return false;
            if (DockPositionOf(setting) == "Right" && !rightMaster) return false;
            return true;
        }

        // Visibilitas host dock: panel melayang BUKAN bagian host (dihitung terpisah).
        bool DockedHere(string key, string setting)
        {
            if (IsFloating(key)) return false;
            return Shown(key, setting);
        }

        SetVisible("PanelToolRail", Shown("toolrail", "Tools"));
        SetVisible("PanelProperties", Shown("properties", "Properties"));
        SetVisible("PanelHistory", Shown("history", "History"));
        SetVisible("PanelDocLayers", Shown("layers", "Layers"));

        SetVisible("PanelOptionsBar", !_tabPanelsHidden && Visible("optionsbar"));
        SetVisible("PanelMenuBar", true);

        bool AnyVisibleAt(string position) => DockedPanels.Any(p =>
            DockPositionOf(p.Setting) == position && DockedHere(p.Key, p.Setting));

        // Host atas/kiri/bawah hanya tampil bila ada panel yang memakainya.
        SetVisible("DockTopHost", AnyVisibleAt("Top"));
        SetVisible("DockLeftHost", AnyVisibleAt("Left"));
        SetVisible("DockBottomHost", AnyVisibleAt("Bottom"));

        // Host kanan = PanelRightEditor (Border). Lebar mengikuti isi: panel penuh =
        // lebar tersimpan (bisa di-drag), hanya rail tools = 70, kosong = sembunyi.
        bool rightNonTool = false;
        if (this.FindControl<Border>("PanelRightEditor") is { } right)
        {
            rightNonTool = DockedPanels.Any(p => p.Setting != "Tools" &&
                DockPositionOf(p.Setting) == "Right" && DockedHere(p.Key, p.Setting));
            bool rightHasAny = AnyVisibleAt("Right");
            double savedW = _settings.EditorRightDockWidth >= 180 ? _settings.EditorRightDockWidth : 240;
            right.Width = rightNonTool ? savedW : (rightHasAny ? 70 : 0);
            right.IsVisible = rightHasAny;
        }
        SetVisible("RightDockSplitter", rightNonTool);
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

    private void OnEditorPanelDockChanged(object? sender, SelectionChangedEventArgs e) { }   // combo lama dihapus

    private void OnResetEditorWorkspaceClick(object? sender, RoutedEventArgs e) => ResetLayout();

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

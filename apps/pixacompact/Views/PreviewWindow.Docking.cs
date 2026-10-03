using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;

namespace PixelcutCompact.Views;

/// <summary>
/// Docking ala Photoshop untuk panel editor: panel bisa DICABUT jadi jendela melayang,
/// DIPASANG balik, dan DIRENDENGKAN ke sisi Kiri/Atas/Kanan/Bawah. Dikendalikan lewat
/// menu ⋮ di header tiap panel dan lewat seret header panel ke sisi tujuan.
/// Posisi disimpan di settings (EditorToolsDock/EditorPropertiesDock/... ).
/// </summary>
public partial class PreviewWindow
{
    private const string PanelDragFormat = "pxa-panel";

    // key → nama kontrol panel + nama setting posisi dock.
    private static readonly (string Key, string Control, string Setting)[] FloatablePanels =
    {
        ("toolrail",   "PanelToolRail",   "Tools"),
        ("properties", "PanelProperties", "Properties"),
        ("history",    "PanelHistory",    "History"),
        ("layers",     "PanelDocLayers",  "Layers"),
    };

    private readonly Dictionary<string, Window> _floatingPanels = new();
    private readonly Dictionary<string, (IBrush? Border, IBrush? Background)> _hostDefaults = new();
    private static readonly IBrush HighlightBorder = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.Parse("#333B82F6"));

    private string ControlOf(string key) => FloatablePanels.FirstOrDefault(p => p.Key == key).Control ?? "";
    private string SettingOf(string key) => FloatablePanels.FirstOrDefault(p => p.Key == key).Setting ?? "";

    private bool IsFloating(string key) => _floatingPanels.ContainsKey(key);

    private string PanelTitle(string key) => key switch
    {
        "toolrail" => "Tools",
        "properties" => T("Panel_Properties"),
        "history" => T("Panel_History"),
        "layers" => T("Panel_Layers"),
        _ => "Panel"
    };

    // ─────────────────────────── Menu ⋮ per panel ───────────────────────────

    private void OnPanelMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor) return;
        var key = anchor.Tag as string;
        if (string.IsNullOrEmpty(key)) return;

        var menu = new ContextMenu();

        var floatItem = new MenuItem { Header = T("Dock_Float") };
        floatItem.Click += (_, _) => FloatPanel(key);
        menu.Items.Add(floatItem);

        var dockItem = new MenuItem { Header = T("Dock_DockTo") };
        foreach (var (label, pos) in new[]
        {
            (T("Dock_Left"), "Left"), (T("Dock_Top"), "Top"),
            (T("Dock_Right"), "Right"), (T("Dock_Bottom"), "Bottom")
        })
        {
            var p = pos;
            var it = new MenuItem { Header = label };
            it.Click += (_, _) => DockPanelTo(key, p);
            dockItem.Items.Add(it);
        }
        menu.Items.Add(dockItem);

        if (key == "toolrail")
        {
            menu.Items.Add(new Separator());
            var two = new MenuItem { Header = T("Dock_TwoColumns"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.EditorToolsPreferTwoColumns };
            two.Click += (_, _) =>
            {
                _settings.EditorToolsPreferTwoColumns = two.IsChecked;
                _settings.Save();
                ConfigureToolOrientation(DockPositionOf("Tools"));
            };
            menu.Items.Add(two);
        }

        menu.Items.Add(new Separator());
        var closeItem = new MenuItem { Header = T("Dock_Close") };
        closeItem.Click += (_, _) => ClosePanel(key);
        menu.Items.Add(closeItem);

        var reset = new MenuItem { Header = T("Dock_Reset") };
        reset.Click += (_, _) => ResetLayout();
        menu.Items.Add(reset);

        menu.Open(anchor);
    }

    // ─────────────────────────── Aksi: float / dock / close ───────────────────────────

    private void FloatPanel(string key)
    {
        if (_floatingPanels.TryGetValue(key, out var existing)) { existing.Activate(); return; }
        if (this.FindControl<Control>(ControlOf(key)) is not { } panel) return;

        if (panel.Parent is Panel pp) pp.Children.Remove(panel);
        panel.Width = double.NaN;
        panel.Height = double.NaN;

        var win = new Window
        {
            Title = PanelTitle(key),
            Width = key == "toolrail" ? 120 : 300,
            Height = 420,
            MinWidth = 140,
            MinHeight = 160,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };

        _floatingPanels[key] = win;
        win.Closed += (_, _) =>
        {
            win.Content = null;            // lepas panel dari jendela
            _floatingPanels.Remove(key);
            ArrangeDockedPanels();         // pasang balik ke posisi tersimpan
            ApplyEditorDockVisibility();
        };

        win.Show(this);
        ApplyEditorDockVisibility();
    }

    private void DockPanelTo(string key, string position)
    {
        SetDockSetting(key, position);
        _settings.Save();
        if (_floatingPanels.TryGetValue(key, out var w))
        {
            w.Close();                     // handler Closed → pasang balik
        }
        else
        {
            ArrangeDockedPanels();
            ApplyEditorDockVisibility();
        }
    }

    private void ClosePanel(string key)
    {
        if (_floatingPanels.TryGetValue(key, out var w)) w.Close();
        PanelVisibility[key] = false;
        SyncPanelMenuChecks();
        _settings.Save();
        ApplyEditorDockVisibility();
    }

    private void ResetLayout()
    {
        _settings.EditorShowRightPanel = true;
        _settings.EditorToolsDock = "Left";
        _settings.EditorPropertiesDock = "Right";
        _settings.EditorHistoryDock = "Right";
        _settings.EditorLayersDock = "Right";
        foreach (var p in PanelVisibility.Keys.ToList()) PanelVisibility[p] = true;
        _settings.Save();
        ConfigureEditorWorkspace();
    }

    private void SetDockSetting(string key, string position)
    {
        switch (SettingOf(key))
        {
            case "Tools": _settings.EditorToolsDock = NormalizeToolDock(position); break;
            case "Properties": _settings.EditorPropertiesDock = NormalizeDock(position); break;
            case "History": _settings.EditorHistoryDock = NormalizeDock(position); break;
            case "Layers": _settings.EditorLayersDock = NormalizeDock(position); break;
        }
    }

    // ─────────────────────────── Seret header untuk pasang ke sisi ───────────────────────────

    /// <summary>Mulai seret panel dari header (kecuali saat menekan tombol ⋮).</summary>
#pragma warning disable CS0618 // DataObject/DragDrop lama; DataTransfer API baru belum lengkap di 11.3
    private async void OnPanelHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.Tag is not string key) return;
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var data = new DataObject();
        data.Set(PanelDragFormat, key);
        try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move); } catch { }
    }

    /// <summary>Pasang penanganan drag-drop ke host dock (Kiri/Atas/Kanan/Bawah).</summary>
    private void WireDockHosts()
    {
        foreach (var name in new[] { "DockTopHost", "DockLeftHost", "DockBottomHost", "PanelRightEditor" })
        {
            if (this.FindControl<Border>(name) is not { } host) continue;
            _hostDefaults[name] = (host.BorderBrush, host.Background);
            DragDrop.SetAllowDrop(host, true);
            host.RemoveHandler(DragDrop.DragOverEvent, OnDockHostDragOver);
            host.RemoveHandler(DragDrop.DropEvent, OnDockHostDrop);
            host.RemoveHandler(DragDrop.DragLeaveEvent, OnDockHostDragLeave);
            host.AddHandler(DragDrop.DragOverEvent, OnDockHostDragOver);
            host.AddHandler(DragDrop.DropEvent, OnDockHostDrop);
            host.AddHandler(DragDrop.DragLeaveEvent, OnDockHostDragLeave);
        }
    }

    private void OnDockHostDragOver(object? sender, DragEventArgs e)
    {
        bool ok = e.Data.Contains(PanelDragFormat);
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        if (ok && sender is Border host) HighlightHost(host, true);
        e.Handled = true;
    }

    private void OnDockHostDragLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is Border host) HighlightHost(host, false);
    }

    private void OnDockHostDrop(object? sender, DragEventArgs e)
    {
        if (sender is Border host) HighlightHost(host, false);
        if (sender is not Border h) return;
        var key = e.Data.Get(PanelDragFormat) as string;
        if (string.IsNullOrEmpty(key)) return;
        var position = HostPosition(h.Name);
        if (position != null) DockPanelTo(key, position);
        e.Handled = true;
    }

    /// <summary>Highlight host tujuan saat menyeret panel (ala Photoshop drop zone).</summary>
    private void HighlightHost(Border host, bool on)
    {
        if (!_hostDefaults.TryGetValue(host.Name ?? "", out var d)) return;
        host.BorderBrush = on ? HighlightBorder : d.Border;
        host.Background = on ? HighlightFill : d.Background;
    }

    private void ClearHostHighlights()
    {
        foreach (var name in new[] { "DockTopHost", "DockLeftHost", "DockBottomHost", "PanelRightEditor" })
            if (this.FindControl<Border>(name) is { } h) HighlightHost(h, false);
    }

    /// <summary>Seret tepi kiri dock kanan untuk mengubah lebarnya (disimpan).</summary>
    private void OnRightDockResizerDragDelta(object? sender, VectorEventArgs e)
    {
        if (this.FindControl<Border>("PanelRightEditor") is not { } right) return;
        double w = Math.Clamp(right.Width - e.Vector.X, 180, 560);
        _settings.EditorRightDockWidth = w;
        right.Width = w;
        _settings.Save();
    }

    private static string? HostPosition(string? hostName) => hostName switch
    {
        "DockTopHost" => "Top",
        "DockLeftHost" => "Left",
        "DockBottomHost" => "Bottom",
        "PanelRightEditor" => "Right",
        _ => null
    };
#pragma warning restore CS0618
}

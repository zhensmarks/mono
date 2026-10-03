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
/// Docking ala Photoshop untuk panel editor: panel bisa DICABUT jadi jendela melayang
/// (tanpa chrome OS; header nama panel = area geser), DIPASANG balik, dan DIRENDENGKAN
/// ke sisi Kiri/Atas/Kanan/Bawah. Beberapa panel boleh MENUMPUK di satu sisi — urutannya
/// diatur dengan menjatuhkan tepat di atas panel lain. Dikendalikan lewat menu ⋮ di header
/// tiap panel dan lewat seret header panel ke sisi tujuan. Posisi + urutan disimpan di settings.
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

    // Status drop-zone aktif (diisi saat DragOver, dipakai saat Drop).
    private string? _dropPosition;
    private string? _dropAnchor;
    private bool _dropAfter;

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
        panel.IsVisible = true;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.VerticalAlignment = VerticalAlignment.Stretch;

        var title = PanelTitle(key);
        var win = new Window
        {
            Title = title,
            Width = key == "toolrail" ? 150 : 320,
            Height = 460,
            MinWidth = 170,
            MinHeight = 220,
            SystemDecorations = SystemDecorations.None,   // tanpa chrome OS
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };

        // Header kustom: nama panel (mis. TOOLS) = area geser jendela.
        var headerText = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var closeBtn = new Button
        {
            Content = new PathIcon
            {
                Data = Geometry.Parse("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"),
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")),
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        closeBtn.Click += (_, _) => win.Close();

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerGrid.Children.Add(headerText);
        Grid.SetColumn(closeBtn, 1);
        headerGrid.Children.Add(closeBtn);

        var header = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A3A")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 6),
            Child = headerGrid,
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(win).Properties.IsLeftButtonPressed) win.BeginMoveDrag(e);
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var panelHost = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
        };
        root.Children.Add(panelHost);
        win.Content = root;

        _floatingPanels[key] = win;
        win.Closed += (_, _) =>
        {
            panelHost.Child = null;        // lepas panel dari jendela
            _floatingPanels.Remove(key);
            ArrangeDockedPanels();         // pasang balik ke posisi tersimpan
            ApplyEditorDockVisibility();
        };

        win.Show(this);
        ApplyEditorDockVisibility();
    }

    /// <summary>Pasang panel ke sisi tertentu. anchor = panel acuan untuk urutan bertumpuk.</summary>
    private void DockPanelTo(string key, string position, string? anchor = null, bool after = false)
    {
        var setting = SettingOf(key);
        if (string.IsNullOrEmpty(setting)) return;

        SetDockSetting(key, position);
        UpdateOrderForDock(setting, position, anchor, after);
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
        _settings.EditorDockOrder = new List<string> { "Tools", "Properties", "History", "Layers" };
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

    // ─────────────────────────── Urutan panel di dalam satu sisi ───────────────────────────

    /// <summary>Urutan panel efektif (setting yang hilang ditambahkan di akhir).</summary>
    private List<string> EffectiveDockOrder()
    {
        var all = DockedPanels.Select(d => d.Setting).ToList();
        var saved = _settings.EditorDockOrder?.Where(s => all.Contains(s)).Distinct().ToList() ?? new List<string>();
        foreach (var s in all) if (!saved.Contains(s)) saved.Add(s);
        return saved;
    }

    /// <summary>Taruh <paramref name="setting"/> sebelum/sesudah anchor, atau di ujung grup sisi.</summary>
    private void UpdateOrderForDock(string setting, string position, string? anchor, bool after)
    {
        var order = EffectiveDockOrder();
        order.Remove(setting);

        if (!string.IsNullOrEmpty(anchor) && anchor != setting && order.Contains(anchor))
        {
            int i = order.IndexOf(anchor) + (after ? 1 : 0);
            order.Insert(Math.Clamp(i, 0, order.Count), setting);
        }
        else
        {
            int last = -1;
            for (int i = 0; i < order.Count; i++)
                if (DockPositionOf(order[i]) == position) last = i;
            order.Insert(last + 1, setting);
        }
        _settings.EditorDockOrder = order;
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

    /// <summary>Drop-zone di seluruh area workspace (ala Photoshop) — tidak bergantung host kosong.</summary>
    private void WireDockHosts()
    {
        if (this.FindControl<Grid>("EditorWorkspaceGrid") is not { } ws) return;
        DragDrop.SetAllowDrop(ws, true);
        ws.RemoveHandler(DragDrop.DragOverEvent, OnWorkspaceDragOver);
        ws.RemoveHandler(DragDrop.DropEvent, OnWorkspaceDrop);
        ws.RemoveHandler(DragDrop.DragLeaveEvent, OnWorkspaceDragLeave);
        ws.AddHandler(DragDrop.DragOverEvent, OnWorkspaceDragOver);
        ws.AddHandler(DragDrop.DropEvent, OnWorkspaceDrop);
        ws.AddHandler(DragDrop.DragLeaveEvent, OnWorkspaceDragLeave);
    }

    private void OnWorkspaceDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(PanelDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            HideDropZones();
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        if (sender is Grid ws)
        {
            var (position, anchor, after) = ComputeDropTarget(ws, e.GetPosition(ws));
            _dropPosition = position; _dropAnchor = anchor; _dropAfter = after;
            ShowDropZone(position);
        }
        e.Handled = true;
    }

    private void OnWorkspaceDragLeave(object? sender, RoutedEventArgs e)
    {
        HideDropZones();
        _dropPosition = null;
    }

    private void OnWorkspaceDrop(object? sender, DragEventArgs e)
    {
        HideDropZones();
        var key = e.Data.Get(PanelDragFormat) as string;
        if (string.IsNullOrEmpty(key) || _dropPosition == null) { _dropPosition = null; return; }

        var position = _dropPosition;
        var anchor = _dropAnchor;
        bool after = _dropAfter;
        _dropPosition = null;
        DockPanelTo(key, position, anchor, after);
        e.Handled = true;
    }

    /// <summary>Tentukan sisi tujuan dari posisi kursor; di tengah = menumpuk di panel acuan.</summary>
    private (string Position, string? Anchor, bool After) ComputeDropTarget(Grid ws, Point p)
    {
        double w = ws.Bounds.Width, h = ws.Bounds.Height;
        double fx = w > 0 ? p.X / w : 0.5, fy = h > 0 ? p.Y / h : 0.5;

        if (fy < 0.20) return ("Top", null, false);
        if (fy > 0.80) return ("Bottom", null, false);
        if (fx < 0.15) return ("Left", null, false);
        if (fx > 0.85) return ("Right", null, false);

        // Tengah: panel di bawah kursor → pasang sebelum/sesudahnya (menumpuk satu sisi).
        if (ws.InputHitTest(p) is Control hit)
        {
            for (Control? c = hit; c != null; c = c.Parent as Control)
            {
                if (string.IsNullOrEmpty(c.Name)) continue;
                var match = DockedPanels.FirstOrDefault(d => d.Name == c.Name);
                if (match.Name == null) continue;

                var pos = DockPositionOf(match.Setting);
                var topLeft = c.TranslatePoint(new Point(0, 0), ws);
                double top = topLeft?.Y ?? 0;
                double mid = top + c.Bounds.Height / 2;
                return (pos, match.Setting, p.Y > mid);
            }
        }

        return (fx < 0.5 ? "Left" : "Right", null, false);
    }

    private void ShowDropZone(string position)
    {
        SetDropZone("DropZoneTop", position == "Top");
        SetDropZone("DropZoneLeft", position == "Left");
        SetDropZone("DropZoneRight", position == "Right");
        SetDropZone("DropZoneBottom", position == "Bottom");
        SetDropZone("DropZoneCenter", position is not ("Top" or "Bottom" or "Left" or "Right"));
        if (this.FindControl<Grid>("DockDropOverlay") is { } overlay) overlay.IsVisible = true;
    }

    private void HideDropZones()
    {
        if (this.FindControl<Grid>("DockDropOverlay") is { } overlay) overlay.IsVisible = false;
    }

    private void SetDropZone(string name, bool visible)
    {
        if (this.FindControl<Border>(name) is { } zone) zone.IsVisible = visible;
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
#pragma warning restore CS0618
}

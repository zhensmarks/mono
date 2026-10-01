using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace PixelcutCompact.Views;

/// <summary>
/// Docking ringan berbasis Grid/StackPanel bawaan Avalonia untuk workspace masking.
/// Tidak membuat window/visual effect terpisah; panel dipindahkan di dalam satu window.
/// </summary>
public partial class PreviewWindow
{
    private bool _workspaceLayoutUpdating;

    private static readonly (string Name, string Focus, string Setting)[] DockedPanels =
    {
        ("PanelToolRail", "Tools", "Tools"),
        ("PanelProperties", "Properties", "Properties"),
        ("PanelHistory", "History", "History"),
        ("PanelDocLayers", "Layers", "Layers")
    };

    private void ConfigureEditorWorkspace()
    {
        _workspaceLayoutUpdating = true;
        SetDockCombo("CboEditorFocus", NormalizeFocus(_settings.EditorWorkspaceFocus));
        SetDockCombo("CboToolsDock", NormalizeDock(_settings.EditorToolsDock));
        SetDockCombo("CboPropertiesDock", NormalizeDock(_settings.EditorPropertiesDock));
        SetDockCombo("CboHistoryDock", NormalizeDock(_settings.EditorHistoryDock));
        SetDockCombo("CboLayersDock", NormalizeDock(_settings.EditorLayersDock));
        _workspaceLayoutUpdating = false;

        ArrangeDockedPanels();
        SetVisible("DockFocusBar", true);
        ApplyEditorDockVisibility();
    }

    private void ArrangeDockedPanels()
    {
        var parking = this.FindControl<StackPanel>("EditorToolParking");
        var top = this.FindControl<StackPanel>("DockTopPanel");
        var left = this.FindControl<StackPanel>("DockLeftPanel");
        var right = this.FindControl<StackPanel>("DockRightPanel");
        var bottom = this.FindControl<StackPanel>("DockBottomPanel");
        if (top == null || left == null || right == null || bottom == null) return;

        var hosts = new List<StackPanel>(5);
        if (parking != null) hosts.Add(parking);
        hosts.AddRange(new[] { top, left, right, bottom });

        foreach (var (name, _, setting) in DockedPanels)
        {
            if (this.FindControl<Control>(name) is not { } panel) continue;
            foreach (var host in hosts) host.Children.Remove(panel);
            var position = setting switch
            {
                "Tools" => NormalizeDock(_settings.EditorToolsDock),
                "Properties" => NormalizeDock(_settings.EditorPropertiesDock),
                "History" => NormalizeDock(_settings.EditorHistoryDock),
                _ => NormalizeDock(_settings.EditorLayersDock)
            };
            FindDockHost(position, top, left, right, bottom).Children.Add(panel);
        }

        ConfigureToolOrientation(NormalizeDock(_settings.EditorToolsDock));
    }

    private static StackPanel FindDockHost(string position, StackPanel top, StackPanel left, StackPanel right, StackPanel bottom)
        => position switch
        {
            "Top" => top,
            "Left" => left,
            "Bottom" => bottom,
            _ => right
        };

    private void ConfigureToolOrientation(string position)
    {
        var rail = this.FindControl<Border>("PanelToolRail");
        var grid = this.FindControl<Grid>("PanelToolContentGrid");
        if (rail == null || grid == null) return;

        var vertical = position is "Left" or "Right";
        rail.Width = vertical ? 62 : double.NaN;
        rail.Height = vertical ? double.NaN : 52;
        rail.Padding = vertical ? new Thickness(4) : new Thickness(8, 6);
        rail.BorderThickness = position switch
        {
            "Left" => new Thickness(0, 0, 1, 0),
            "Right" => new Thickness(1, 0, 0, 0),
            "Bottom" => new Thickness(0, 1, 0, 0),
            _ => new Thickness(0, 0, 0, 1)
        };

        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        if (vertical)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        var groups = new[] { "ToolSelectionGroup", "ToolPaintGroup", "ToolQuickGroup", "ToolSaveGroup" };
        for (var i = 0; i < groups.Length; i++)
        {
            if (this.FindControl<StackPanel>(groups[i]) is not { } group) continue;
            group.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            Grid.SetRow(group, vertical ? i : 0);
            Grid.SetColumn(group, vertical ? 0 : i);
        }
    }

    private bool ShouldShowDockedPanel(string focus)
    {
        if (!_editMode || string.Equals(NormalizeFocus(_settings.EditorWorkspaceFocus), "Canvas", StringComparison.Ordinal))
            return false;

        var selectedFocus = NormalizeFocus(_settings.EditorWorkspaceFocus);
        if (selectedFocus != "All" && selectedFocus != focus) return false;
        if (focus != "Tools" && !_settings.EditorShowRightPanel) return false;
        if (focus == "Layers" && !_editorV2Active) return false;
        return true;
    }

    private void ApplyEditorDockVisibility()
    {
        var visibleByName = new Dictionary<string, bool>
        {
            ["PanelToolRail"] = ShouldShowDockedPanel("Tools"),
            ["PanelProperties"] = ShouldShowDockedPanel("Properties"),
            ["PanelHistory"] = ShouldShowDockedPanel("History"),
            ["PanelDocLayers"] = ShouldShowDockedPanel("Layers")
        };

        foreach (var (panelName, isVisible) in visibleByName)
            SetVisible(panelName, isVisible);

        bool HasVisiblePanelAt(string position) => DockedPanels.Any(panel =>
            NormalizeDock(GetDockSetting(panel.Setting)) == position && visibleByName[panel.Name]);

        SetVisible("DockTopHost", HasVisiblePanelAt("Top"));
        SetVisible("DockLeftHost", HasVisiblePanelAt("Left"));
        SetVisible("DockBottomHost", HasVisiblePanelAt("Bottom"));

        var rightHasNonTool = DockedPanels.Any(panel => panel.Setting != "Tools" &&
            NormalizeDock(GetDockSetting(panel.Setting)) == "Right" && visibleByName[panel.Name]);
        var rightHasAny = HasVisiblePanelAt("Right");
        if (this.FindControl<Border>("PanelRightEditor") is { } rightPanel)
        {
            rightPanel.Width = rightHasNonTool ? 240 : (rightHasAny ? 70 : 0);
            rightPanel.IsVisible = _editMode && rightHasAny;
        }
        SetVisible("DockFocusBar", _editMode);
    }

    private string GetDockSetting(string panel) => panel switch
    {
        "Tools" => _settings.EditorToolsDock,
        "Properties" => _settings.EditorPropertiesDock,
        "History" => _settings.EditorHistoryDock,
        _ => _settings.EditorLayersDock
    };

    private void SetEditorWorkspaceActive(bool active)
    {
        SetVisible("DockFocusBar", active);
        if (active)
        {
            ApplyEditorDockVisibility();
            return;
        }

        SetVisible("DockTopHost", false);
        SetVisible("DockLeftHost", false);
        SetVisible("PanelRightEditor", false);
        SetVisible("DockBottomHost", false);
        SetVisible("PanelToolRail", false);
    }

    private void OnEditorFocusChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_editMode || _workspaceLayoutUpdating || sender is not ComboBox combo) return;
        var focus = NormalizeFocus(SelectedTag(combo));
        _settings.EditorWorkspaceFocus = focus;
        if (focus is "All" or "Properties" or "History" or "Layers")
            _settings.EditorShowRightPanel = true;
        _settings.Save();
        ApplyEditorDockVisibility();
    }

    private void OnEditorPanelDockChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_editMode || _workspaceLayoutUpdating || sender is not ComboBox combo) return;
        var position = NormalizeDock(SelectedTag(combo));
        switch (combo.Tag?.ToString())
        {
            case "Tools": _settings.EditorToolsDock = position; break;
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
        _settings.EditorWorkspaceFocus = "All";
        _settings.EditorShowRightPanel = true;
        _settings.EditorToolsDock = "Top";
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

    private static string NormalizeFocus(string? focus) => focus?.Trim().ToUpperInvariant() switch
    {
        "TOOLS" => "Tools",
        "PROPERTIES" => "Properties",
        "HISTORY" => "History",
        "LAYERS" => "Layers",
        "CANVAS" => "Canvas",
        _ => "All"
    };
}

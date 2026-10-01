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
/// Docking lightweightly with Avalonia's in-window panels. Panels sharing a host become real tabs;
/// each panel retains its own saved dock position so moving it out separates it from the group.
/// </summary>
public partial class PreviewWindow
{
    private bool _workspaceLayoutUpdating;
    private bool _toolLayoutUpdating;
    private string _lastToolDockPosition = string.Empty;
    private bool? _lastToolTwoColumnLayout;

    private static readonly (string Name, string Focus, string Setting)[] DockedPanels =
    {
        ("PanelToolRail", "Tools", "Tools"),
        ("PanelProperties", "Properties", "Properties"),
        ("PanelHistory", "History", "History"),
        ("PanelDocLayers", "Layers", "Layers")
    };

    private static readonly string[] ToolGroupNames =
    {
        "ToolSelectionGroup", "ToolPaintGroup", "ToolQuickGroup", "ToolSaveGroup"
    };

    private void ConfigureEditorWorkspace()
    {
        _workspaceLayoutUpdating = true;
        try
        {
            SetDockCombo("CboEditorFocus", NormalizeFocus(_settings.EditorWorkspaceFocus));
            SetDockCombo("CboToolsDock", NormalizeDock(_settings.EditorToolsDock));
            SetDockCombo("CboPropertiesDock", NormalizeDock(_settings.EditorPropertiesDock));
            SetDockCombo("CboHistoryDock", NormalizeDock(_settings.EditorHistoryDock));
            SetDockCombo("CboLayersDock", NormalizeDock(_settings.EditorLayersDock));
            if (this.FindControl<ToggleButton>("BtnToolsTwoColumns") is { } columnsToggle)
                columnsToggle.IsChecked = _settings.EditorToolsPreferTwoColumns;
        }
        finally
        {
            _workspaceLayoutUpdating = false;
        }

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

        var positionedPanels = DockedPanels
            .Select(panel => (panel.Name, Control: this.FindControl<Control>(panel.Name), Position: NormalizeDock(GetDockSetting(panel.Setting))))
            .Where(panel => panel.Control != null)
            .ToList();

        // Detach content before clearing any old TabControl or host, including panels that were tab content.
        foreach (var panel in positionedPanels)
            DetachFromParent(panel.Control!);
        foreach (var host in hosts)
            host.Children.Clear();

        var previousUpdating = _workspaceLayoutUpdating;
        _workspaceLayoutUpdating = true;
        try
        {
            foreach (var dockGroup in positionedPanels.GroupBy(panel => panel.Position))
            {
                var host = FindDockHost(dockGroup.Key, top, left, right, bottom);
                var panels = dockGroup.ToList();
                if (panels.Count == 1)
                {
                    host.Children.Add(panels[0].Control!);
                    continue;
                }

                var tabs = new TabControl
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                tabs.Classes.Add("editor-dock-tabs");
                foreach (var panel in panels)
                {
                    var tab = new TabItem
                    {
                        Header = GetDockPanelTitle(panel.Name),
                        Tag = panel.Name,
                        Content = panel.Control
                    };
                    tab.Classes.Add("editor-dock-tab");
                    tabs.Items.Add(tab);
                }
                tabs.SelectedIndex = 0;
                host.Children.Add(tabs);
            }
        }
        finally
        {
            _workspaceLayoutUpdating = previousUpdating;
        }

        ConfigureToolOrientation(NormalizeDock(_settings.EditorToolsDock), force: true);
        UpdateToolColumnsPreferenceControl();
    }

    private static string GetDockPanelTitle(string name) => name switch
    {
        "PanelToolRail" => "Tools",
        "PanelProperties" => "Properties",
        "PanelHistory" => "History",
        _ => "Layers"
    };

    private static void DetachFromParent(Control control)
    {
        switch (control.Parent)
        {
            case Panel parentPanel:
                parentPanel.Children.Remove(control);
                break;
            case ContentControl parentContent when ReferenceEquals(parentContent.Content, control):
                parentContent.Content = null;
                break;
        }
    }

    private static StackPanel FindDockHost(string position, StackPanel top, StackPanel left, StackPanel right, StackPanel bottom)
        => position switch
        {
            "Top" => top,
            "Left" => left,
            "Bottom" => bottom,
            _ => right
        };

    private void ConfigureToolOrientation(string position, bool force = false)
    {
        if (_toolLayoutUpdating) return;

        var rail = this.FindControl<Border>("PanelToolRail");
        var grid = this.FindControl<Grid>("PanelToolContentGrid");
        var columnLeft = this.FindControl<StackPanel>("ToolColumnLeft");
        var columnRight = this.FindControl<StackPanel>("ToolColumnRight");
        var groups = ToolGroupNames.Select(name => this.FindControl<StackPanel>(name)).ToArray();
        if (rail == null || grid == null || columnLeft == null || columnRight == null || groups.Any(group => group == null)) return;

        var vertical = position is "Left" or "Right";
        var toolGroups = groups.Select(group => group!).ToArray();
        var useTwoColumns = false;
        if (vertical)
        {
            rail.Padding = new Thickness(4, 3);
            rail.BorderThickness = position switch
            {
                "Left" => new Thickness(0, 0, 1, 0),
                _ => new Thickness(1, 0, 0, 0)
            };

            foreach (var group in toolGroups)
            {
                group.Orientation = Orientation.Vertical;
                group.Spacing = 2;
                SetToolButtonSize(group, 40);
                SetToolDividerStyle(group, vertical: true);
                group.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            var oneColumnRequiredHeight = toolGroups.Sum(group => group.DesiredSize.Height)
                + Math.Max(0, toolGroups.Length - 1) * 2
                + rail.Padding.Top + rail.Padding.Bottom
                + rail.BorderThickness.Top + rail.BorderThickness.Bottom;
            var availableHeight = GetAvailableToolHeight(position);
            useTwoColumns = ToolDockColumnPolicy.UseTwoColumns(
                availableHeight,
                oneColumnRequiredHeight,
                _settings.EditorToolsPreferTwoColumns);
        }

        if (!force && _lastToolDockPosition == position && _lastToolTwoColumnLayout == useTwoColumns)
        {
            if (vertical)
            {
                foreach (var group in toolGroups)
                {
                    group.Spacing = useTwoColumns ? 1 : 2;
                    SetToolButtonSize(group, useTwoColumns ? 35 : 40);
                    SetToolDividerStyle(group, vertical: true);
                }
                columnLeft.Spacing = 1;
                columnRight.Spacing = 1;
            }
            return;
        }

        _toolLayoutUpdating = true;
        try
        {
            if (vertical)
            {
                grid.RowDefinitions.Clear();
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Clear();
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                foreach (var group in toolGroups)
                    DetachFromParent(group);
                columnLeft.Children.Clear();
                columnRight.Children.Clear();

                columnLeft.Orientation = Orientation.Vertical;
                columnRight.Orientation = Orientation.Vertical;
                columnLeft.Spacing = 1;
                columnRight.Spacing = 1;
                columnLeft.IsVisible = true;
                columnRight.IsVisible = useTwoColumns;
                if (!grid.Children.Contains(columnLeft)) grid.Children.Add(columnLeft);
                if (!grid.Children.Contains(columnRight)) grid.Children.Add(columnRight);
                Grid.SetRow(columnLeft, 0);
                Grid.SetColumn(columnLeft, 0);
                Grid.SetRow(columnRight, 0);
                Grid.SetColumn(columnRight, 1);

                var sideButtonSize = useTwoColumns ? 35 : 40;
                foreach (var group in toolGroups)
                {
                    group.Spacing = useTwoColumns ? 1 : 2;
                    SetToolButtonSize(group, sideButtonSize);
                    SetToolDividerStyle(group, vertical: true);
                }

                if (useTwoColumns)
                {
                    columnLeft.Children.Add(toolGroups[0]);
                    columnLeft.Children.Add(toolGroups[1]);
                    columnRight.Children.Add(toolGroups[2]);
                    columnRight.Children.Add(toolGroups[3]);
                }
                else
                {
                    foreach (var group in toolGroups)
                        columnLeft.Children.Add(group);
                }

                rail.Width = useTwoColumns ? 94 : 62;
                rail.Height = double.NaN;
            }
            else
            {
                foreach (var group in toolGroups)
                    DetachFromParent(group);
                columnLeft.Children.Clear();
                columnRight.Children.Clear();
                columnLeft.IsVisible = false;
                columnRight.IsVisible = false;

                grid.RowDefinitions.Clear();
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Clear();
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                for (var i = 0; i < toolGroups.Length; i++)
                {
                    var group = toolGroups[i];
                    group.Orientation = Orientation.Horizontal;
                    group.Spacing = 2;
                    SetToolButtonSize(group, 40);
                    SetToolDividerStyle(group, vertical: false);
                    Grid.SetRow(group, 0);
                    Grid.SetColumn(group, i);
                    if (!grid.Children.Contains(group)) grid.Children.Add(group);
                }

                rail.Width = double.NaN;
                rail.Height = 52;
            }

            rail.Padding = vertical ? new Thickness(4, 3) : new Thickness(8, 6);
            if (!vertical)
            {
                rail.BorderThickness = position switch
                {
                    "Bottom" => new Thickness(0, 1, 0, 0),
                    _ => new Thickness(0, 0, 0, 1)
                };
            }

            _lastToolDockPosition = position;
            _lastToolTwoColumnLayout = useTwoColumns;
        }
        finally
        {
            _toolLayoutUpdating = false;
        }

        UpdateToolColumnsPreferenceControl();
    }

    private double GetAvailableToolHeight(string position)
    {
        var host = this.FindControl<Border>(position == "Left" ? "DockLeftHost" : "PanelRightEditor");
        if (host == null) return 0;

        var panelCount = DockedPanels.Count(panel => NormalizeDock(GetDockSetting(panel.Setting)) == position);
        var tabHeaderHeight = panelCount > 1 ? 36 : 0;
        return Math.Max(0,
            host.Bounds.Height
            - host.Padding.Top - host.Padding.Bottom
            - host.BorderThickness.Top - host.BorderThickness.Bottom
            - tabHeaderHeight);
    }

    private static void SetToolButtonSize(StackPanel group, double size)
    {
        foreach (var button in group.Children.OfType<Button>())
        {
            button.Width = size;
            button.Height = size;
        }
    }

    private static void SetToolDividerStyle(StackPanel group, bool vertical)
    {
        foreach (var divider in group.Children.OfType<Border>())
        {
            divider.Width = vertical ? double.NaN : 1;
            divider.Height = vertical ? 1 : double.NaN;
            divider.Margin = vertical ? new Thickness(2, 1) : new Thickness(6, 4);
        }
    }

    private void UpdateToolColumnsPreferenceControl()
    {
        if (this.FindControl<ToggleButton>("BtnToolsTwoColumns") is not { } toggle) return;
        var previousUpdating = _workspaceLayoutUpdating;
        _workspaceLayoutUpdating = true;
        try
        {
            toggle.IsVisible = NormalizeDock(_settings.EditorToolsDock) is "Left" or "Right";
            toggle.IsChecked = _settings.EditorToolsPreferTwoColumns;
        }
        finally
        {
            _workspaceLayoutUpdating = previousUpdating;
        }
    }

    private void OnToolDockHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_editMode || _workspaceLayoutUpdating || _toolLayoutUpdating) return;
        var position = NormalizeDock(_settings.EditorToolsDock);
        if (position is not ("Left" or "Right")) return;

        ConfigureToolOrientation(position);
        ApplyEditorDockVisibility();
    }

    private void OnToolsTwoColumnsChanged(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _workspaceLayoutUpdating || sender is not ToggleButton toggle) return;
        _settings.EditorToolsPreferTwoColumns = toggle.IsChecked == true;
        _settings.Save();
        ConfigureToolOrientation(NormalizeDock(_settings.EditorToolsDock), force: true);
        ApplyEditorDockVisibility();
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
        var visibleByName = DockedPanels.ToDictionary(panel => panel.Name, panel => ShouldShowDockedPanel(panel.Focus));

        foreach (var (panelName, isVisible) in visibleByName)
            SetVisible(panelName, isVisible);

        var hosts = new[]
        {
            this.FindControl<StackPanel>("DockTopPanel"),
            this.FindControl<StackPanel>("DockLeftPanel"),
            this.FindControl<StackPanel>("DockRightPanel"),
            this.FindControl<StackPanel>("DockBottomPanel")
        };
        foreach (var tabs in hosts.Where(host => host != null).SelectMany(host => host!.Children.OfType<TabControl>()))
        {
            var tabItems = tabs.Items.OfType<TabItem>().ToArray();
            foreach (var tab in tabItems)
                tab.IsVisible = tab.Tag is string name && visibleByName.TryGetValue(name, out var isVisible) && isVisible;

            if (tabs.SelectedItem is TabItem selected && !selected.IsVisible)
                tabs.SelectedItem = tabItems.FirstOrDefault(tab => tab.IsVisible);
        }

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
            var toolWidth = this.FindControl<Border>("PanelToolRail")?.Width ?? 62;
            rightPanel.Width = rightHasNonTool ? 240 : (rightHasAny ? toolWidth : 0);
            rightPanel.IsVisible = _editMode && rightHasAny;
        }

        UpdateToolColumnsPreferenceControl();
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
        _settings.EditorToolsPreferTwoColumns = false;
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

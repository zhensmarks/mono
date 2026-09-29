using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class CustomMergeWindow : MantraDialogBase
{
    public List<string> SelectedColumns { get; private set; } = new();
    public string MergeFormat { get; private set; } = "{A} {B}";
    public string Separator { get; private set; } = " ";
    public string NewColumnName { get; private set; } = string.Empty;
    public bool DeleteSourceColumns { get; private set; } = true;
    public bool Confirmed { get; private set; }

    private readonly List<string> _chosen = new();
    private readonly List<string> _allColumns = new();
    private readonly TableDataRow? _sampleRow;

    public CustomMergeWindow()
    {
        InitializeComponent();
    }

    public CustomMergeWindow(IEnumerable<string> columns, IEnumerable<string>? preselected = null, TableDataRow? sampleRow = null) : this()
    {
        _allColumns = columns.ToList();
        _sampleRow = sampleRow;

        var pre = preselected?.Where(c => _allColumns.Contains(c)).ToList() ?? new List<string>();
        if (pre.Count >= 2)
        {
            _chosen.AddRange(pre);
        }
        else
        {
            if (pre.Count == 1) _chosen.Add(pre[0]);
            foreach (var col in _allColumns)
            {
                if (!_chosen.Contains(col))
                {
                    _chosen.Add(col);
                    if (_chosen.Count >= 2) break;
                }
            }
        }

        RebuildColumnBadges();
        UpdateSuggestedNameAndPreview();
    }

    private void RebuildColumnBadges()
    {
        if (PanelSelectedColumns == null) return;
        PanelSelectedColumns.Children.Clear();

        foreach (var col in _chosen)
        {
            var border = new Border
            {
                Background = SolidColorBrush.Parse("#1E2433"),
                BorderBrush = SolidColorBrush.Parse("#2E384D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 6, 2),
                Padding = new Thickness(8, 3, 6, 3)
            };

            var sp = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock
            {
                Text = col,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("#EDEDED"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            if (_chosen.Count > 2)
            {
                var btnRemove = new Button
                {
                    Content = "x",
                    FontSize = 9,
                    Width = 16,
                    Height = 16,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = SolidColorBrush.Parse("#828896"),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Tag = col
                };
                btnRemove.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is string cName)
                    {
                        _chosen.Remove(cName);
                        RebuildColumnBadges();
                        UpdateSuggestedNameAndPreview();
                    }
                };
                sp.Children.Add(btnRemove);
            }

            border.Child = sp;
            PanelSelectedColumns.Children.Add(border);
        }

        var remaining = _allColumns.Except(_chosen).ToList();
        if (remaining.Count > 0)
        {
            var btnAdd = new Button
            {
                Content = "Tambah Kolom",
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 2, 0, 2),
                Background = SolidColorBrush.Parse("#181B24"),
                BorderBrush = SolidColorBrush.Parse("#2E3342"),
                BorderThickness = new Thickness(1),
                Foreground = SolidColorBrush.Parse("#60A5FA"),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            var flyout = new MenuFlyout();
            foreach (var rCol in remaining)
            {
                var item = new MenuItem { Header = rCol, Tag = rCol };
                item.Click += (s, e) =>
                {
                    if (s is MenuItem mi && mi.Tag is string colToAdd)
                    {
                        _chosen.Add(colToAdd);
                        RebuildColumnBadges();
                        UpdateSuggestedNameAndPreview();
                    }
                };
                flyout.Items.Add(item);
            }

            btnAdd.Flyout = flyout;
            PanelSelectedColumns.Children.Add(btnAdd);
        }
    }

    private void OnPresetChanged(object? sender, RoutedEventArgs e)
    {
        if (PanelCustomSep != null && RbCustom != null)
        {
            PanelCustomSep.IsVisible = RbCustom.IsChecked == true;
        }
        UpdateSuggestedNameAndPreview();
    }

    private void OnCustomSepChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSuggestedNameAndPreview();
    }

    private void UpdateSuggestedNameAndPreview()
    {
        if (TxtNewColName == null || TxtPreview == null || RbBracket == null) return;

        string format = "{A} {B}";
        string sep = " ";

        if (RbBracket.IsChecked == true)
        {
            format = "{A} ({B})";
            sep = " ";
        }
        else if (RbComma?.IsChecked == true)
        {
            format = "{A}, {B}";
            sep = ", ";
        }
        else if (RbPipe?.IsChecked == true)
        {
            format = "{A} | {B}";
            sep = " | ";
        }
        else if (RbNewline?.IsChecked == true)
        {
            format = "separator";
            sep = "\n";
        }
        else if (RbCustom?.IsChecked == true)
        {
            format = "separator";
            sep = TxtCustomSep?.Text ?? " ";
        }

        if (_chosen.Count == 2)
        {
            if (RbBracket.IsChecked == true)
                TxtNewColName.Text = $"{_chosen[0]} ({_chosen[1]})";
            else if (RbComma?.IsChecked == true)
                TxtNewColName.Text = $"{_chosen[0]}, {_chosen[1]}";
            else
                TxtNewColName.Text = $"{_chosen[0]} + {_chosen[1]}";
        }
        else
        {
            TxtNewColName.Text = string.Join(" + ", _chosen);
        }

        if (_chosen.Count >= 2)
        {
            var vals = _chosen.Select(c => _sampleRow != null ? (_sampleRow[c]?.Trim() ?? string.Empty) : c).ToList();

            string preview;
            if (_chosen.Count == 2 && format != "separator")
            {
                var a = vals[0];
                var b = vals[1];
                if (string.IsNullOrEmpty(a)) preview = b;
                else if (string.IsNullOrEmpty(b)) preview = a;
                else preview = format.Replace("{A}", a).Replace("{B}", b);
            }
            else
            {
                var nonEmpty = vals.Where(v => !string.IsNullOrEmpty(v));
                preview = string.Join(sep == "\n" ? " [Baris Baru] " : sep, nonEmpty);
            }

            TxtPreview.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
        }
        else
        {
            TxtPreview.Text = "Pilih minimal 2 kolom";
        }
    }

    private void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (_chosen.Count < 2) return;

        var newName = TxtNewColName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(newName)) return;

        SelectedColumns = _chosen.ToList();
        NewColumnName = newName;
        DeleteSourceColumns = ChkDeleteSource?.IsChecked == true;

        if (RbBracket?.IsChecked == true)
        {
            MergeFormat = "{A} ({B})";
            Separator = " ";
        }
        else if (RbComma?.IsChecked == true)
        {
            MergeFormat = "{A}, {B}";
            Separator = ", ";
        }
        else if (RbPipe?.IsChecked == true)
        {
            MergeFormat = "{A} | {B}";
            Separator = " | ";
        }
        else if (RbNewline?.IsChecked == true)
        {
            MergeFormat = "separator";
            Separator = "\n";
        }
        else if (RbCustom?.IsChecked == true)
        {
            MergeFormat = "separator";
            Separator = TxtCustomSep?.Text ?? " ";
        }
        else
        {
            MergeFormat = "{A} {B}";
            Separator = " ";
        }

        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class SplitColumnWindow : MantraDialogBase
{
    public string SourceColumn { get; private set; } = string.Empty;
    public string Separator { get; private set; } = ", ";
    public string NewColumnA { get; private set; } = string.Empty;
    public string NewColumnB { get; private set; } = string.Empty;
    public bool DeleteSource { get; private set; }
    public bool Confirmed { get; private set; }

    public SplitColumnWindow()
    {
        InitializeComponent();
    }

    public SplitColumnWindow(IEnumerable<string> columns, string? source = null) : this()
    {
        var colList = columns.ToList();
        CmbSource.ItemsSource = colList;
        if (colList.Count == 0) return;

        CmbSource.SelectedItem = (!string.IsNullOrEmpty(source) && colList.Contains(source))
            ? source
            : colList[0];

        UpdateDefaultNames();
    }

    private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDefaultNames();
    }

    private void UpdateDefaultNames()
    {
        if (CmbSource == null || TxtColA == null || TxtColB == null) return;
        var src = CmbSource.SelectedItem?.ToString() ?? "KOL";
        TxtColA.Text = src + "_1";
        TxtColB.Text = src + "_2";
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var src = CmbSource.SelectedItem?.ToString();
        var a = TxtColA.Text?.Trim() ?? string.Empty;
        var b = TxtColB.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
        {
            return;
        }

        SourceColumn = src;
        Separator = TxtSeparator.Text ?? ", ";
        NewColumnA = a;
        NewColumnB = b;
        DeleteSource = ChkDeleteSource.IsChecked == true;
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

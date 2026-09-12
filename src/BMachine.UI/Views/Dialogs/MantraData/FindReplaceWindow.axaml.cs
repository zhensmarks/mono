using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class FindReplaceWindow : MantraDialogBase
{
    public string FindText { get; private set; } = string.Empty;
    public string ReplaceText { get; private set; } = string.Empty;
    public string? TargetColumn { get; private set; }
    public bool MatchCase { get; private set; }
    public bool Confirmed { get; private set; }

    public FindReplaceWindow()
    {
        InitializeComponent();
    }

    public FindReplaceWindow(IEnumerable<string> columns, string? currentColumn = null) : this()
    {
        var items = new List<string> { "(Semua kolom)" };
        items.AddRange(columns);
        CmbColumn.ItemsSource = items;
        if (!string.IsNullOrEmpty(currentColumn) && items.Contains(currentColumn))
            CmbColumn.SelectedItem = currentColumn;
        else
            CmbColumn.SelectedIndex = 0;

        TxtFind.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var find = TxtFind.Text;
        if (string.IsNullOrEmpty(find))
        {
            return;
        }

        FindText = find;
        ReplaceText = TxtReplace.Text ?? string.Empty;
        var col = CmbColumn.SelectedItem?.ToString();
        TargetColumn = string.IsNullOrEmpty(col) || col == "(Semua kolom)" ? null : col;
        MatchCase = ChkMatchCase.IsChecked == true;
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

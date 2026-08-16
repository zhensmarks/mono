using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views;

public partial class ExplorerSettingsWindow : Window
{
    public ExplorerSettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

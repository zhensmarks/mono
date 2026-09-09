using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class MantraAlertDialog : Window
{
    public MantraAlertDialog()
    {
        InitializeComponent();
    }

    public MantraAlertDialog(string title, string message) : this()
    {
        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

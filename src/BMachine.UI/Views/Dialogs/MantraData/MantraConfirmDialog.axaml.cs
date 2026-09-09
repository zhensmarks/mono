using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class MantraConfirmDialog : Window
{
    public bool Confirmed { get; private set; } = false;

    public MantraConfirmDialog()
    {
        InitializeComponent();
    }

    public MantraConfirmDialog(string title, string message) : this()
    {
        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

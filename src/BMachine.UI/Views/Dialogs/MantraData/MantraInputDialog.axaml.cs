using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class MantraInputDialog : MantraDialogBase
{
    public string InputValue { get; private set; } = string.Empty;
    public bool Confirmed { get; private set; } = false;

    public MantraInputDialog()
    {
        InitializeComponent();
    }

    public MantraInputDialog(string title, string prompt, string defaultValue = "") : this()
    {
        Title = title;
        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;
        InputBox.Text = defaultValue;
        if (!string.IsNullOrEmpty(defaultValue))
        {
            InputBox.SelectionStart = 0;
            InputBox.SelectionEnd = defaultValue.Length;
        }
    }

    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClick(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelClick(sender, e);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        InputValue = InputBox.Text?.Trim() ?? string.Empty;
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

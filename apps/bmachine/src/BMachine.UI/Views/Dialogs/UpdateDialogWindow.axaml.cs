using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BMachine.UI.Views.Dialogs;

public partial class UpdateDialogWindow : Window
{
    public UpdateDialogWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

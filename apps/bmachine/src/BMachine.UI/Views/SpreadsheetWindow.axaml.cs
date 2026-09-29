using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BMachine.UI.Views;

public partial class SpreadsheetWindow : Window
{
    public SpreadsheetWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is BMachine.UI.ViewModels.SpreadsheetViewModel vm)
        {
            _ = vm.SaveColumnWidthsAsync();
        }
        base.OnClosing(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

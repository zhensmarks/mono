using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class ExplorerSettingsWindow : Window
{
    public ExplorerSettingsWindow()
    {
        InitializeComponent();
        this.AddHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && e.ClickCount == 2)
        {
            e.Handled = true;
            this.Close();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

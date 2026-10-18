using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class ExplorerSettingsWindow : Window
{
    public ExplorerSettingsWindow()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CompactDialogSizing.FitToScreen(this);
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && e.ClickCount == 2)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

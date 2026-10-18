using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class AvatarSelectionWindow : Window
{
    public AvatarSelectionWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CompactDialogSizing.FitToScreen(this);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            CancelAndClose();
            e.Handled = true;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => CancelAndClose();

    private void CancelAndClose()
    {
        if (DataContext is AvatarSelectionViewModel vm)
            vm.CancelCommand.Execute(null);
        if (IsVisible)
            Close();
    }
}

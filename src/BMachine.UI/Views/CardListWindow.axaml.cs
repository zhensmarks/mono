using Avalonia.Controls;
using System;
using Avalonia.Input;
using Avalonia;

namespace BMachine.UI.Views;

public partial class CardListWindow : Window
{
    public CardListWindow()
    {
        InitializeComponent();
    }

    private long _lastRightClickTime = 0;
    
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        
        // Window Drag (Left Click)
        if (props.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
        
        // Double Right Click Close
        if (props.IsRightButtonPressed)
        {
            long now = DateTime.Now.Ticks;
            if (now - _lastRightClickTime < TimeSpan.FromMilliseconds(500).Ticks)
            {
                this.Close();
            }
            _lastRightClickTime = now;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.Close();
            e.Handled = true;
        }
    }

    private void OnCloseWindow(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
         this.Close();
    }
}

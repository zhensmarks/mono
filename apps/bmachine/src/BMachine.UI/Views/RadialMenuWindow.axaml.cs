using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.Models;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class RadialMenuWindow : Window
{
    public RadialMenuWindow()
    {
        InitializeComponent();
        this.Deactivated += (s, e) => this.Hide(); // Auto-hide on focus loss
        
        // Track mouse movement for highlight
        this.PointerMoved += OnPointerMoved;
        this.PointerReleased += OnPointerReleased;
        
        // Reset page when window becomes visible
        this.Opened += (s, e) =>
        {
            if (DataContext is RadialMenuViewModel vm)
            {
                vm.ResetPage();
            }
        };
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is RadialMenuViewModel vm)
        {
            var pos = e.GetPosition(this);
            vm.UpdateHighlight(pos, new Size(this.Width, this.Height));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is RadialMenuViewModel vm)
        {
            var pos = e.GetPosition(this);
            var w = this.Width;
            var h = this.Height;
            double dx = pos.X - w / 2;
            double dy = pos.Y - h / 2;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            // Deadzone atau luar window → clear cincin luar
            if (dist < 40 || dist > 154)
            {
                vm.UpdateHighlight(new Point(w / 2, h / 2), new Size(w, h));
            }
            else
            {
                vm.UpdateHighlight(pos, new Size(w, h));
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

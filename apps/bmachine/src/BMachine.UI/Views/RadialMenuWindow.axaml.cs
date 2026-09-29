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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

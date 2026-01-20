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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

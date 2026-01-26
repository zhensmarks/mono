using Avalonia.Controls;
using BMachine.UI.ViewModels;
using BMachine.SDK;

namespace BMachine.UI.Views;

public partial class PixelcutWindow : Window
{
    public PixelcutWindow()
    {
        InitializeComponent();
        
        // Auto-inject ViewModel if not set (Simulated IoC)
        if (DataContext == null)
        {
             DataContext = new PixelcutViewModel(null); 
        }

        // Set Window Mode
        var view = this.FindControl<CompactPixelcutView>("CompactView");
        if (view != null)
        {
            view.IsWindowMode = true;
        }
    }
    
    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }
}

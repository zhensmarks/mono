using Avalonia.Controls;
using BMachine.UI.ViewModels;
using BMachine.SDK;

namespace BMachine.UI.Views;

public partial class GdriveWindow : Window
{
    public GdriveWindow()
    {
        InitializeComponent();
        
        // Auto-inject ViewModel if not set (Simulated IoC fallback, though typically set by command)
        if (DataContext == null)
        {
             // Typically this won't be hit if created via OpenSeparateWindow
        }

        // Set Window Mode on the View
        var view = this.FindControl<CompactGdriveView>("CompactView");
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

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

        // Window Interaction Logic
        this.PointerPressed += (s, e) =>
        {
            var point = e.GetCurrentPoint(this);
            
            // Right Click -> Close
            if (point.Properties.IsRightButtonPressed)
            {
                this.Close();
                e.Handled = true;
            }
            // Left Click -> Drag Move (Simulate Title Bar everywhere or check hit test)
            else if (point.Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
            }
        };
    }
}

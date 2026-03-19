using Avalonia.Controls;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class UnifiedTrelloView : UserControl
{
    public UnifiedTrelloView()
    {
        InitializeComponent();
    }

    private void OnRootGridSizeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e is Avalonia.Controls.SizeChangedEventArgs sizeEventArgs && sender is Grid grid)
        {
            var movePanel = this.FindControl<Border>("Part_MovePanel");
            if (movePanel == null) return;
            
            bool isMobile = sizeEventArgs.NewSize.Width < 800;
            if (isMobile)
            {
                movePanel.Width = double.NaN;
                movePanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                movePanel.Margin = new Avalonia.Thickness(0);
            }
            else
            {
                movePanel.Width = 400;
                movePanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                movePanel.Margin = new Avalonia.Thickness(0);
            }
        }
    }
}

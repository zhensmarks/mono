using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using Avalonia.Input;
using System.Linq;
using Avalonia.VisualTree;

namespace BMachine.UI.Views;

public partial class OutputExplorerView : UserControl
{
    public OutputExplorerView()
    {
        InitializeComponent();
        this.PointerPressed += OnRootPointerPressed;
        this.KeyDown += OnRootKeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ExplorerItemViewModel viewModel)
        {
            if (this.DataContext is OutputExplorerViewModel context)
            {
                context.OpenItemCommand.Execute(viewModel);
            }
        }
    }

    private void OnHorizontalListPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Redirect vertical scroll wheel to horizontal scrolling for the horizontal list
        if (sender is ListBox listBox && listBox.DataContext is OutputExplorerViewModel context && context.IsHorizontalLayout)
        {
             var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
             if (scrollViewer != null)
             {
                 if (e.Delta.Y != 0 && e.Delta.X == 0)
                 {
                     // Convert Y delta to X offset
                     double speed = 50; 
                     double offset = scrollViewer.Offset.X - (e.Delta.Y * speed);
                     
                     // Clamp
                     if (offset < 0) offset = 0;
                     if (offset > scrollViewer.Extent.Width - scrollViewer.Viewport.Width) 
                        offset = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;
                        
                     scrollViewer.Offset = new Vector(offset, scrollViewer.Offset.Y);
                     e.Handled = true;
                 }
             }
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back)
        {
             var context = this.DataContext as OutputExplorerViewModel;
             if (context != null && context.GoBackCommand.CanExecute(null))
             {
                 context.GoBackCommand.Execute(null);
                 e.Handled = true;
             }
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var context = this.DataContext as OutputExplorerViewModel;
        if (context == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed)
        {
            if (context.GoBackCommand.CanExecute(null))
            {
                context.GoBackCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (context.GoForwardCommand.CanExecute(null))
            {
                context.GoForwardCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}

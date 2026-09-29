using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System.ComponentModel;

namespace BMachine.UI.Views;

public partial class DocFloatingWindow : Window
{
    private bool _isClosingFromButton = false;

    public DocFloatingWindow()
    {
        InitializeComponent();
        this.Opened += OnOpened;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is DashboardViewModel dashboard && dashboard.BatchVM != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var bounds = await dashboard.BatchVM.GetFloatingDocBounds();
                if (bounds != null)
                {
                    this.Position = new Avalonia.PixelPoint(bounds.Value.X, bounds.Value.Y);
                    this.Width = bounds.Value.Width;
                    this.Height = bounds.Value.Height;
                }
            });
        }
        else if (DataContext is BatchViewModel batchVm)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var bounds = await batchVm.GetFloatingDocBounds();
                if (bounds != null)
                {
                    this.Position = new Avalonia.PixelPoint(bounds.Value.X, bounds.Value.Y);
                    this.Width = bounds.Value.Width;
                    this.Height = bounds.Value.Height;
                }
            });
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        
        if (!_isClosingFromButton)
        {
            // If closed via Alt+F4 or system menu
            if (DataContext is DashboardViewModel dashboard && dashboard.BatchVM != null)
            {
                if (dashboard.BatchVM.IsDocFloating)
                {
                    dashboard.BatchVM.ToggleDocFloatingCommand.Execute(null);
                }
            }
            else if (DataContext is BatchViewModel batchVm)
            {
                 if (batchVm.IsDocFloating)
                 {
                     batchVm.ToggleDocFloatingCommand.Execute(null);
                 }
            }
        }

        // Save bounds
        if (DataContext is DashboardViewModel dashboard2 && dashboard2.BatchVM != null)
        {
            _ = dashboard2.BatchVM.SaveFloatingDocBounds(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
        }
        else if (DataContext is BatchViewModel batchVm2)
        {
            _ = batchVm2.SaveFloatingDocBounds(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
        }
    }
}

using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Input;
using BMachine.App.ViewModels;

namespace BMachine.App.Views;

public partial class MainWindow : Window
{
    private const double LogPanelWidth = 290;

    public MainWindow()
    {
        InitializeComponent();
        InitializeMessenger();
        
        // Exit Gesture: Double Right Click
        this.PointerPressed += (s, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed && e.ClickCount == 2)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OpenExitConfirmCommand.Execute(null);
                    e.Handled = true;
                }
            }
        };
    }

    // Custom Window Commands
    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }

    // Constructor Continuation
    private void InitializeMessenger()
    {
        // Register for Log Panel Toggling
        WeakReferenceMessenger.Default.Register<ToggleLogPanelMessage>(this, (r, m) =>
        {
            // Thread safety for UI updates
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (m.Value) // Opening
                {
                    var screen = this.Screens.ScreenFromVisual(this);
                    if (screen != null)
                    {
                        var workingArea = screen.WorkingArea;
                        double newWidth = this.Width + LogPanelWidth;
                        
                        // Check if expanding right hits the edge
                        if (this.Position.X + newWidth > workingArea.X + workingArea.Width)
                        {
                            // Shift left to accommodate
                            var newX = this.Position.X - LogPanelWidth;
                            // Ensure we don't shift off sreen left
                            if (newX < workingArea.X) newX = workingArea.X;
                            
                            this.Position = new PixelPoint((int)newX, this.Position.Y);
                        }
                    }
                    this.Width += LogPanelWidth;
                }
                else // Closing
                {
                    // Basic shrink. If we shifted left, we *could* shift back, but maybe better to stay put?
                    // User might have moved window.
                    // Just shrinking width is safest behavior.
                    if (this.Width > LogPanelWidth) 
                        this.Width -= LogPanelWidth;
                }
            });
        });
    }

    protected override async void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is BMachine.App.ViewModels.MainWindowViewModel vm)
        {
            var saved = await vm.GetSavedWindowState();
            if (saved != null)
            {
                this.Width = saved.Value.W;
                this.Height = saved.Value.H;
                this.WindowState = saved.Value.State;
                // Only restore position if valid (not 0,0 typically, unless intended)
                // But 0,0 is valid. Maybe checks if screen bounds contain it?
                // For now, trust the saved value.
                this.Position = new Avalonia.PixelPoint(saved.Value.X, saved.Value.Y);
                this.WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is BMachine.App.ViewModels.MainWindowViewModel vm)
        {
            // Fire and forget save
            _ = vm.SaveWindowState(this.Width, this.Height, this.WindowState, this.Position.X, this.Position.Y);
        }
    }
}
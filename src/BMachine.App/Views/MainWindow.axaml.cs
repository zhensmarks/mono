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

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is BMachine.App.ViewModels.MainWindowViewModel vm)
        {
            // Initial log panel state has ALREADY been loaded by App.axaml.cs BEFORE Show()
            // and placed into vm.InitialLogPanelOpen.
            if (vm.CurrentView is BMachine.UI.ViewModels.DashboardViewModel dashVm)
            {
                dashVm.IsLogPanelOpen = vm.InitialLogPanelOpen;
                dashVm.MarkInitialLoadComplete();
            }

            // Fallback forced layout update just in case
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                this.UpdateLayout();
                var root = this.FindControl<Control>("MainRoot");
                if (root != null)
                {
                    root.InvalidateMeasure();
                    root.InvalidateArrange();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is BMachine.App.ViewModels.MainWindowViewModel vm)
        {
            // Get log panel state from Dashboard ViewModel
            bool isLogPanelOpen = false;
            if (vm.CurrentView is BMachine.UI.ViewModels.DashboardViewModel dashVm)
            {
                isLogPanelOpen = dashVm.IsLogPanelOpen;
            }
            
            // Synchronous save to ensure data persists before process exits
            try
            {
                double saveW = this.Bounds.Width > 0 ? this.Bounds.Width : this.Width;
                double saveH = this.Bounds.Height > 0 ? this.Bounds.Height : this.Height;

                vm.SaveWindowState(saveW, saveH, this.WindowState, this.Position.X, this.Position.Y, isLogPanelOpen)
                    .GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window state: {ex.Message}");
            }
        }
    }
}
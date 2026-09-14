using System;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Input;

namespace BMachine.App.Views;

public partial class MainWindow : Window
{
    private const double LogPanelWidth = 290;
    private bool _windowStateRestored;

    public MainWindow()
    {
        InitializeComponent();
        InitializeMessenger();
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && (e.Key == Key.E || e.Key == Key.N))
        {
            WeakReferenceMessenger.Default.Send(new RequestOpenExplorerWindowMessage());
            e.Handled = true;
        }
    }

    private void InitializeMessenger()
    {
        WeakReferenceMessenger.Default.Register<ToggleLogPanelMessage>(this, (r, m) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (m.Value)
                {
                    var screen = Screens.ScreenFromVisual(this);
                    if (screen != null)
                    {
                        var area = screen.WorkingArea;
                        var newWidth = Width + LogPanelWidth;
                        if (Position.X + newWidth > area.X + area.Width)
                            Position = new PixelPoint((int)Math.Max(area.X, Position.X - LogPanelWidth), Position.Y);
                    }
                    Width += LogPanelWidth;
                }
                else if (Width > LogPanelWidth)
                {
                    Width -= LogPanelWidth;
                }
            });
        });
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_windowStateRestored || DataContext is not BMachine.App.ViewModels.MainWindowViewModel vm) return;
        _windowStateRestored = true;

        var saved = await vm.GetSavedWindowState();
        if (saved.HasValue && saved.Value.W > MinWidth && saved.Value.H > MinHeight)
        {
            Width = saved.Value.W;
            Height = saved.Value.H;
            if (saved.Value.X != 0 || saved.Value.Y != 0)
                Position = new PixelPoint(saved.Value.X, saved.Value.Y);
            WindowState = saved.Value.State == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        if (vm.CurrentView is BMachine.UI.ViewModels.DashboardViewModel dashVm)
        {
            dashVm.IsLogPanelOpen = vm.InitialLogPanelOpen;
            dashVm.MarkInitialLoadComplete();
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            var root = this.FindControl<Control>("MainRoot");
            root?.InvalidateMeasure();
            root?.InvalidateArrange();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is not BMachine.App.ViewModels.MainWindowViewModel vm) return;

        var isLogPanelOpen = vm.CurrentView is BMachine.UI.ViewModels.DashboardViewModel dashVm && dashVm.IsLogPanelOpen;
        try
        {
            if (WindowState == WindowState.Normal)
            {
                var saveW = Bounds.Width > 0 ? Bounds.Width : Width;
                var saveH = Bounds.Height > 0 ? Bounds.Height : Height;
                vm.SaveWindowState(saveW, saveH, WindowState, Position.X, Position.Y, isLogPanelOpen)
                    .GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving window state: {ex.Message}");
        }
    }
}


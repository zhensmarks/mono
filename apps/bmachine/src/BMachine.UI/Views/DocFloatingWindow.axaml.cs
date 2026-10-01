using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class DocFloatingWindow : Window
{
    private bool _dockRequested;

    public DocFloatingWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        var batchVm = ResolveBatchVm();
        if (batchVm == null) return;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var bounds = await batchVm.GetFloatingDocBounds();
            if (bounds == null) return;

            Width = System.Math.Max(MinWidth, bounds.Value.Width);
            Height = System.Math.Max(MinHeight, bounds.Value.Height);
            Position = new PixelPoint(bounds.Value.X, bounds.Value.Y);
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnOpenDocSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var batchVm = ResolveBatchVm();
        if (batchVm == null) return;

        var dialog = new Dialogs.DocSettingsWindow(batchVm);
        _ = dialog.ShowDialog(this);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var batchVm = ResolveBatchVm();
        if (batchVm?.IsDocFloating == true)
        {
            _dockRequested = true;
            batchVm.ToggleDocFloatingCommand.Execute(null);
        }

        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Window-manager close (or Close button) returns Doc to its existing tab.
        if (!_dockRequested && ResolveBatchVm() is { IsDocFloating: true } batchVm)
            batchVm.ToggleDocFloatingCommand.Execute(null);

        if (ResolveBatchVm() is { } currentBatchVm)
            _ = currentBatchVm.SaveFloatingDocBounds(Position.X, Position.Y, Bounds.Width, Bounds.Height);
    }

    private BatchViewModel? ResolveBatchVm() => DataContext switch
    {
        DashboardViewModel dashboard => dashboard.BatchVM,
        BatchViewModel batchVm => batchVm,
        _ => null
    };
}

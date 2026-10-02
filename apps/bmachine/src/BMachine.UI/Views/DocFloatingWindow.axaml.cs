using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BMachine.UI.Views;

public partial class DocFloatingWindow : Window
{
    private bool _dockRequested;
    private bool _restoringBounds;
    private bool _boundsRestored;
    private CancellationTokenSource? _boundsSaveCts;

    public DocFloatingWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        PositionChanged += OnBoundsChanged;
        PropertyChanged += OnWindowPropertyChanged;
    }

    private async void OnOpened(object? sender, System.EventArgs e)
    {
        if (ResolveBatchVm() is not { } batchVm) return;

        _restoringBounds = true;
        try
        {
            var bounds = await batchVm.GetFloatingDocBounds();
            if (bounds is { } saved && IsPositionOnAnyScreen(saved.X, saved.Y))
            {
                Width = System.Math.Max(MinWidth, saved.Width);
                Height = System.Math.Max(MinHeight, saved.Height);
                Position = new PixelPoint(saved.X, saved.Y);
            }
        }
        finally
        {
            _boundsRestored = true;
            _restoringBounds = false;
        }
    }

    private static bool IsPositionOnAnyScreen(int x, int y)
    {
        var screens = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow?.Screens
            : null;
        return screens?.All.Any(screen =>
            x < screen.Bounds.X + screen.Bounds.Width && x + 40 > screen.Bounds.X &&
            y < screen.Bounds.Y + screen.Bounds.Height && y + 40 > screen.Bounds.Y) ?? true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WidthProperty || e.Property == Window.HeightProperty ||
            e.Property == Window.WindowStateProperty)
            ScheduleBoundsSave();
    }

    private void OnBoundsChanged(object? sender, System.EventArgs e) => ScheduleBoundsSave();

    private void ScheduleBoundsSave()
    {
        if (!_boundsRestored || _restoringBounds || WindowState != WindowState.Normal) return;
        CancelPendingBoundsSave();
        _boundsSaveCts = new CancellationTokenSource();
        _ = SaveBoundsAfterDelayAsync(_boundsSaveCts);
    }

    private async Task SaveBoundsAfterDelayAsync(CancellationTokenSource saveCts)
    {
        try
        {
            await Task.Delay(350, saveCts.Token);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!saveCts.IsCancellationRequested && WindowState == WindowState.Normal)
                    await SaveCurrentBoundsAsync();
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_boundsSaveCts, saveCts))
                _boundsSaveCts = null;
            saveCts.Dispose();
        }
    }

    private Task SaveCurrentBoundsAsync()
    {
        if (ResolveBatchVm() is not { } batchVm || Bounds.Width <= 0 || Bounds.Height <= 0)
            return Task.CompletedTask;
        return batchVm.SaveFloatingDocBounds(Position.X, Position.Y, Bounds.Width, Bounds.Height);
    }

    private void CancelPendingBoundsSave()
    {
        var pending = _boundsSaveCts;
        _boundsSaveCts = null;
        if (pending is null) return;

        try { pending.Cancel(); }
        catch (ObjectDisposedException) { }
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

        CancelPendingBoundsSave();
        _ = SaveCurrentBoundsAsync();
    }

    private BatchViewModel? ResolveBatchVm() => DataContext switch
    {
        DashboardViewModel dashboard => dashboard.BatchVM,
        BatchViewModel batchVm => batchVm,
        _ => null
    };
}

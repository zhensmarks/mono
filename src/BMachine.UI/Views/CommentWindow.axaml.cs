using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System;

namespace BMachine.UI.Views;

public partial class CommentWindow : Window
{
    public CommentWindow()
    {
        InitializeComponent();
    }

    private Vector _savedScrollOffset;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // We no longer rely solely on ViewModel properties because updates might happen
        // without toggling IsLoadingComments.
        // Instead, we will rely on window events to preserve state.
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.Activated += OnWindowActivated;
        this.Deactivated += OnWindowDeactivated;
        
        // Also listen to pointer events to ensure we capture the explicit interaction
        // specifically if the "click" causes a reset.
        this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnClosed(EventArgs e)
    {
        this.Activated -= OnWindowActivated;
        this.Deactivated -= OnWindowDeactivated;
        this.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerPressed);
        base.OnClosed(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        SaveScrollOffset();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        RestoreScrollOffset();
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // If the user clicks, we assume they want to be where they are.
        // Just in case some other event fires, let's ensure we have the latest.
        SaveScrollOffset();
    }

    private void SaveScrollOffset()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
        if (scrollViewer != null)
        {
            _savedScrollOffset = scrollViewer.Offset;
        }
    }

    private void RestoreScrollOffset()
    {
         var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
         if (scrollViewer != null)
         {
             // Only restore if we have a valid saved offset > 0 (or if we really want to lock it)
             // But if it's 0, it might be intentional.
             // The issue is "jumps to top", so we want to prevent unintended jumps to 0.
             if (_savedScrollOffset.Y > 0 && Math.Abs(scrollViewer.Offset.Y - _savedScrollOffset.Y) > 1.0)
             {
                 Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                 {
                     scrollViewer.Offset = _savedScrollOffset;
                 }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
             }
         }
    }
}

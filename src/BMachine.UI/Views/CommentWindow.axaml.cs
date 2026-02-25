using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.KeyDown += CommentTextBox_KeyDown;
        }

        var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
        if (scrollViewer != null)
        {
            scrollViewer.AddHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        }
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        this.Activated -= OnWindowActivated;
        this.Deactivated -= OnWindowDeactivated;
        this.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerPressed);
        
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.KeyDown -= CommentTextBox_KeyDown;
        }

        base.OnClosed(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Save the current position when user leaves the window.
        SaveScrollOffset();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Avalonia layout engine resets scroll on re-activation.
        // We must restore it asynchronously after the layout pass.
        if (_savedScrollOffset.Y > 0)
        {
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
                 if (scrollViewer != null)
                 {
                     scrollViewer.Offset = _savedScrollOffset;
                 }
             }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Save the current position on click, so we have a fresh value if needed.
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


    private async void CommentTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
         if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
         {
             if (sender is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText))
             {
                 var topLevel = TopLevel.GetTopLevel(this);
                 if (topLevel?.Clipboard != null)
                 {
                     await topLevel.Clipboard.SetTextAsync(tb.SelectedText);
                     // Prevent event from bubbling and causing unintended UI changes.
                     e.Handled = true;
                 }
             }
         }
    }
}

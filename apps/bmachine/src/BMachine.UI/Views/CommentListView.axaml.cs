using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BMachine.UI.Views;

public partial class CommentListView : UserControl
{
    public CommentListView()
    {
        InitializeComponent();

        // Tunnel dari root UserControl: request BringIntoView dari teks comment
        // diblok sebelum sampai ke ScrollViewer mana pun (panel atau CommentWindow).
        AddHandler(
            Control.RequestBringIntoViewEvent,
            OnCommentRequestBringIntoView,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnCommentRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.Views;

public partial class LeaderboardView : UserControl
{
    public LeaderboardView()
    {
        InitializeComponent();
    }

    private void OnRootPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            // Navigate Back
            CommunityToolkit.Mvvm.Messaging.IMessenger messenger = CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default;
            messenger.Send(new BMachine.UI.Messages.NavigateBackMessage());
            e.Handled = true;
        }
    }
}

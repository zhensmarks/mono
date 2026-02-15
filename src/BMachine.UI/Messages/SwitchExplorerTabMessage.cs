using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Sent when user presses Ctrl+Tab to cycle to the next tab in the ExplorerWindow.</summary>
public class SwitchExplorerTabMessage : ValueChangedMessage<object?>
{
    public SwitchExplorerTabMessage() : base(null!) { }
}

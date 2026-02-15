using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Request to focus the path bar / address bar in the active Output Explorer.</summary>
public class FocusExplorerPathBarMessage : ValueChangedMessage<object?>
{
    public FocusExplorerPathBarMessage() : base(null!) { }
}

using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Sent by the ViewModel AFTER it has finished reloading shortcut gestures from DB. Views should re-apply key bindings upon receiving this.</summary>
public class ExplorerShortcutsReadyMessage : ValueChangedMessage<object?>
{
    public ExplorerShortcutsReadyMessage() : base(null!) { }
}

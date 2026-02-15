using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Sent when explorer shortcuts are saved in Settings; explorers should reload and re-apply key bindings.</summary>
public class ExplorerShortcutsChangedMessage : ValueChangedMessage<object?>
{
    public ExplorerShortcutsChangedMessage() : base(null!) { }
}

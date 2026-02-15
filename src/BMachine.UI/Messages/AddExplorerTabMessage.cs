using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Request to add a new tab in the Explorer window. Value = window that should add the tab.</summary>
public class AddExplorerTabMessage : ValueChangedMessage<object?>
{
    public AddExplorerTabMessage(object? targetWindow) : base(targetWindow ?? null!) { }
}

using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Request to close the current Explorer tab or window (e.g. Ctrl+W). Value = window that should handle (to target the right window); pass from view so only that window reacts.</summary>
public class RequestCloseExplorerWindowMessage : ValueChangedMessage<object?>
{
    public RequestCloseExplorerWindowMessage(object? targetWindow) : base(targetWindow ?? null!) { }
}

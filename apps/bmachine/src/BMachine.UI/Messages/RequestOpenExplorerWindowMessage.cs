using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class RequestOpenExplorerWindowMessage : ValueChangedMessage<object?>
{
    public RequestOpenExplorerWindowMessage(object? value = null) : base(value) { }
}

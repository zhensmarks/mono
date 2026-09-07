using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class DocFloatingChangedMessage : ValueChangedMessage<bool>
{
    public DocFloatingChangedMessage(bool isFloating) : base(isFloating)
    {
    }
}

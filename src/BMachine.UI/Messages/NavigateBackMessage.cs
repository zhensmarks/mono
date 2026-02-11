using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class NavigateBackMessage : ValueChangedMessage<bool>
{
    public NavigateBackMessage() : base(true)
    {
    }
}

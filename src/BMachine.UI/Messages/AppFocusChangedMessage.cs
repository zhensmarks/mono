using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class AppFocusChangedMessage : ValueChangedMessage<bool>
{
    public AppFocusChangedMessage(bool isFocused) : base(isFocused)
    {
    }
}

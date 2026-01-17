using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class NavSettingsChangedMessage : ValueChangedMessage<bool>
{
    public NavSettingsChangedMessage(bool value = true) : base(value)
    {
    }
}

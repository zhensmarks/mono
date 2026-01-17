using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class OrbBreathingToggledMessage : ValueChangedMessage<bool>
{
    public OrbBreathingToggledMessage(bool isEnabled) : base(isEnabled)
    {
    }
}

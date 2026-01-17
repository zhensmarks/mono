using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class StopProcessMessage : ValueChangedMessage<bool>
{
    public StopProcessMessage(bool shouldStop = true) : base(shouldStop)
    {
    }
}

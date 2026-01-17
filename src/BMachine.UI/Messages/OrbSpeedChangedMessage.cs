using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class OrbSpeedChangedMessage : ValueChangedMessage<int>
{
    public OrbSpeedChangedMessage(int speedIndex) : base(speedIndex)
    {
    }
}

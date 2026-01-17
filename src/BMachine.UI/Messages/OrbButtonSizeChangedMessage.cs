namespace BMachine.UI.Messages;

public class OrbButtonSizeChangedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<(double Width, double Height)>
{
    public OrbButtonSizeChangedMessage(double width, double height) : base((width, height)) { }
}

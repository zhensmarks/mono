using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class ToggleLogPanelMessage : ValueChangedMessage<bool>
{
    public ToggleLogPanelMessage(bool isOpen) : base(isOpen)
    {
    }
}

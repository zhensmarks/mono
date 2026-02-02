using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class DashboardVisibilityChangedMessage : ValueChangedMessage<bool>
{
    public DashboardVisibilityChangedMessage(bool value = true) : base(value)
    {
    }
}

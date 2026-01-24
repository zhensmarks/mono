using CommunityToolkit.Mvvm.Messaging.Messages;
using BMachine.UI.Models;

namespace BMachine.UI.Messages;

public class UpdateAvailableMessage : ValueChangedMessage<UpdateInfo>
{
    public UpdateAvailableMessage(UpdateInfo value) : base(value)
    {
    }
}

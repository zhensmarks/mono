using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class ProfileUpdatedMessage : ValueChangedMessage<(string UserName, string AvatarSource)>
{
    public ProfileUpdatedMessage(string userName, string avatarSource) : base((userName, avatarSource))
    {
    }
}

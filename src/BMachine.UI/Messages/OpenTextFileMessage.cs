using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class OpenTextFileMessage : ValueChangedMessage<string>
{
    public OpenTextFileMessage(string path) : base(path)
    {
    }
}

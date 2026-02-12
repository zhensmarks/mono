using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class SettingsChangedMessage : ValueChangedMessage<string>
{
    public string Key { get; }
    public SettingsChangedMessage(string key, string value) : base(value)
    {
        Key = key;
    }
}

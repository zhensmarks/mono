using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Request to focus Explorer Settings view (e.g. so it can capture the next key for shortcut recording).</summary>
public class ExplorerSettingsFocusMessage : ValueChangedMessage<object?>
{
    public ExplorerSettingsFocusMessage() : base(null!) { }
}

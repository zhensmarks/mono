using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class ThemeSettingsChangedMessage
{
    public string? DarkBackgroundColor { get; }
    public string? LightBackgroundColor { get; }

    public ThemeSettingsChangedMessage(string? darkBg, string? lightBg)
    {
        DarkBackgroundColor = darkBg;
        LightBackgroundColor = lightBg;
    }
}

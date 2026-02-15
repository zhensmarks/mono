using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class BatchAddFoldersMessage(string[] paths, string? scriptToSelect = null) : ValueChangedMessage<string[]>(paths)
{
    public string? ScriptToSelect { get; } = scriptToSelect;
}

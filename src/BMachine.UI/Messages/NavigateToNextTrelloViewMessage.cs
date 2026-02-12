using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class NavigateToNextTrelloViewMessage : ValueChangedMessage<string>
{
    public object? SourceWindow { get; init; }

    public NavigateToNextTrelloViewMessage(string currentView, object? sourceWindow = null) : base(currentView) 
    {
        SourceWindow = sourceWindow;
    }
}

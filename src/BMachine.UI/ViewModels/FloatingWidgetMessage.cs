using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.ViewModels;

public class FloatingWidgetMessage
{
    public bool IsVisible { get; }
    public FloatingWidgetMessage(bool isVisible)
    {
        IsVisible = isVisible;
    }
}

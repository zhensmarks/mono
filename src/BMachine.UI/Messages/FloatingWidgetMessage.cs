namespace BMachine.UI.Messages;

public class FloatingWidgetMessage
{
    public bool IsVisible { get; }

    public FloatingWidgetMessage(bool isVisible)
    {
        IsVisible = isVisible;
    }
}

namespace BMachine.UI.Messages;

public class NavigateToPageMessage
{
    public string PageName { get; }

    public NavigateToPageMessage(string pageName)
    {
        PageName = pageName;
    }
}

using BMachine.UI.ViewModels;

namespace BMachine.UI.Messages;

public class ExplorerSettingsChangedMessage {}

public class ScrollToExplorerItemMessage
{
    public object Item { get; }
    public ScrollToExplorerItemMessage(object item)
    {
        Item = item;
    }
}

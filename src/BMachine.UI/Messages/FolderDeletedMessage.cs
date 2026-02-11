using CommunityToolkit.Mvvm.Messaging.Messages;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Messages;

public class FolderDeletedMessage : ValueChangedMessage<BatchNodeItem>
{
    public FolderDeletedMessage(BatchNodeItem item) : base(item)
    {
    }
}

using Avalonia.Media;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class OrbColorChangedMessage : ValueChangedMessage<IBrush>
{
    public OrbColorChangedMessage(IBrush color) : base(color)
    {
    }
}

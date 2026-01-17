using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

public class ProcessStatusMessage : ValueChangedMessage<bool>
{
    public string? ProcessName { get; }
    
    public ProcessStatusMessage(bool isRunning, string? processName = null) : base(isRunning)
    {
        ProcessName = processName;
    }
}

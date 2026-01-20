using CommunityToolkit.Mvvm.Messaging.Messages;
using BMachine.UI.Models;

namespace BMachine.UI.Messages;

public class SetRecordingModeMessage : ValueChangedMessage<bool>
{
    public SetRecordingModeMessage(bool isRecording) : base(isRecording) { }
}

public class TriggerRecordedMessage : ValueChangedMessage<TriggerConfig>
{
    public TriggerRecordedMessage(TriggerConfig config) : base(config) { }
}

public class UpdateTriggerConfigMessage : ValueChangedMessage<TriggerConfig>
{
    public UpdateTriggerConfigMessage(TriggerConfig config) : base(config) { }
}

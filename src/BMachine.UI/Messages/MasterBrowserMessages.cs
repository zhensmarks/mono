using BMachine.UI.ViewModels;

namespace BMachine.UI.Messages;

// Trigger open master browser
public record OpenMasterBrowserMessage(BatchNodeItem TargetNode, string Side); // "Left" or "Right"

// Notify paths changed internally
public record MasterPathsChangedMessage;

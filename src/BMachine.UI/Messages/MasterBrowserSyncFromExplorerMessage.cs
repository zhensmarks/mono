namespace BMachine.UI.Messages;

/// <summary>Switch terminal to MASTER and set "Copy to" to the given folder (from explorer context menu).</summary>
public class MasterBrowserSyncFromExplorerMessage(string folderPath)
{
    public string FolderPath { get; } = folderPath;
}

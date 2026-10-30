namespace BMachine.UI.ViewModels;

internal static class SpreadsheetSaveStatus
{
    public static string GetCompletionMessage(int savedCount)
    {
        return savedCount > 0
            ? $"Save request accepted for {savedCount} cells."
            : "No changes to save.";
    }
}

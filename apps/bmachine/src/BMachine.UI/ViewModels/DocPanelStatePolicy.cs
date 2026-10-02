namespace BMachine.UI.ViewModels;

/// <summary>The result of opening or docking the floating Doc window.</summary>
public readonly record struct FloatingDocToggleState(
    bool IsFloating,
    int SelectedActivityMode,
    int? ActivityModeToRestore);

/// <summary>
/// Pure state rules shared by BatchViewModel and regression tests. Activity
/// mode 3 is Doc; modes 0-2 are the Log Panel's non-Doc views.
/// </summary>
public static class DocPanelStatePolicy
{
    public const int DocActivityMode = 3;

    public static bool IsVisibleInPanel(int selectedActivityMode, bool isFloating) =>
        selectedActivityMode == DocActivityMode && !isFloating;

    public static FloatingDocToggleState ToggleFloating(
        bool isFloating,
        int selectedActivityMode,
        int lastNonDocActivityMode,
        int? activityModeToRestore)
    {
        if (!isFloating)
        {
            var returnMode = selectedActivityMode == DocActivityMode
                ? lastNonDocActivityMode
                : selectedActivityMode;
            var nextSelectedMode = selectedActivityMode == DocActivityMode
                ? returnMode
                : selectedActivityMode;

            return new FloatingDocToggleState(true, nextSelectedMode, returnMode);
        }

        var selectedAfterDock = activityModeToRestore.HasValue
            && selectedActivityMode == activityModeToRestore.Value
                ? DocActivityMode
                : selectedActivityMode;

        return new FloatingDocToggleState(false, selectedAfterDock, null);
    }
}

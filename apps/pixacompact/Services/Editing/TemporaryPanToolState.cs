namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Keeps the selected editor tool intact while Space temporarily routes canvas
/// gestures through the Pan tool.
/// </summary>
public sealed class TemporaryPanToolState
{
    public TemporaryPanToolState(EditToolKind selectedTool = EditToolKind.Pan)
    {
        SelectedTool = selectedTool;
    }

    public EditToolKind SelectedTool { get; private set; }
    public bool IsSpacePanActive { get; private set; }
    public EditToolKind EffectiveTool => IsSpacePanActive ? EditToolKind.Pan : SelectedTool;

    public void SelectTool(EditToolKind tool) => SelectedTool = tool;

    /// <summary>Begin temporary pan; repeated key-down events remain idempotent.</summary>
    public bool HoldSpace()
    {
        if (IsSpacePanActive) return false;
        IsSpacePanActive = true;
        return true;
    }

    /// <summary>Restore the selected tool after key-up or focus loss.</summary>
    public bool ReleaseSpace()
    {
        if (!IsSpacePanActive) return false;
        IsSpacePanActive = false;
        return true;
    }
}

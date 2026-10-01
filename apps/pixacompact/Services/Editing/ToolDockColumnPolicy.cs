namespace PixelcutCompact.Services.Editing;

/// <summary>Chooses a safe column count for the vertical Tools dock.</summary>
public static class ToolDockColumnPolicy
{
    public static bool UseTwoColumns(double availableHeight, double singleColumnRequiredHeight, bool preferTwoColumns)
    {
        if (preferTwoColumns) return true;
        if (double.IsNaN(availableHeight) || availableHeight <= 0) return true;
        if (double.IsNaN(singleColumnRequiredHeight) || singleColumnRequiredHeight <= 0) return false;
        return singleColumnRequiredHeight > availableHeight;
    }
}

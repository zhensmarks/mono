using Avalonia.Controls;

namespace BMachine.UI.Views;

/// <summary>
/// Keeps the Log Panel tab slots equal while removing hidden Explorer/Doc slots
/// from the layout instead of leaving blank columns behind.
/// </summary>
public static class PanelNavigationLayout
{
    public const int ColumnCount = 5;

    public static GridLength[] GetColumnWidths(bool explorerVisible, bool docVisible)
    {
        var tab = new GridLength(1, GridUnitType.Star);
        var collapsed = new GridLength(0);

        return
        [
            explorerVisible ? tab : collapsed,
            explorerVisible ? GridLength.Auto : collapsed,
            tab,
            tab,
            docVisible ? tab : collapsed
        ];
    }
}

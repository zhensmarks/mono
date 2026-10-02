using Avalonia.Controls;
using Avalonia.Media;
using BMachine.UI.Converters;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

public class DocPanelStatePolicyTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    public void DocTabIsVisibleOnlyWhenSelectedAndDocked(
        int selectedMode,
        bool isFloating,
        bool expected)
    {
        Assert.Equal(expected, DocPanelStatePolicy.IsVisibleInPanel(selectedMode, isFloating));
    }

    [Fact]
    public void PopOutFallsBackAndDockRestoresDocWhenUserHasNotChangedPanel()
    {
        var floating = DocPanelStatePolicy.ToggleFloating(
            isFloating: false,
            selectedActivityMode: DocPanelStatePolicy.DocActivityMode,
            lastNonDocActivityMode: 1,
            activityModeToRestore: null);

        Assert.True(floating.IsFloating);
        Assert.Equal(1, floating.SelectedActivityMode);
        Assert.Equal(1, floating.ActivityModeToRestore);
        Assert.False(DocPanelStatePolicy.IsVisibleInPanel(floating.SelectedActivityMode, floating.IsFloating));

        var docked = DocPanelStatePolicy.ToggleFloating(
            floating.IsFloating,
            floating.SelectedActivityMode,
            lastNonDocActivityMode: 1,
            floating.ActivityModeToRestore);

        Assert.False(docked.IsFloating);
        Assert.Equal(DocPanelStatePolicy.DocActivityMode, docked.SelectedActivityMode);
        Assert.Null(docked.ActivityModeToRestore);
        Assert.True(DocPanelStatePolicy.IsVisibleInPanel(docked.SelectedActivityMode, docked.IsFloating));
    }

    [Fact]
    public void DockDoesNotStealSelectionIfUserNavigatedWhileDocWasFloating()
    {
        var docked = DocPanelStatePolicy.ToggleFloating(
            isFloating: true,
            selectedActivityMode: 0,
            lastNonDocActivityMode: 0,
            activityModeToRestore: 1);

        Assert.False(docked.IsFloating);
        Assert.Equal(0, docked.SelectedActivityMode);
    }
}

public class PanelNavigationLayoutTests
{
    [Theory]
    [InlineData(true, true, 4)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(false, false, 2)]
    public void HiddenTabsCollapseAndRemainingSlotsKeepEqualStarWidths(
        bool explorerVisible,
        bool docVisible,
        int expectedVisibleTabs)
    {
        var widths = PanelNavigationLayout.GetColumnWidths(explorerVisible, docVisible);

        Assert.Equal(PanelNavigationLayout.ColumnCount, widths.Length);
        Assert.Equal(expectedVisibleTabs, widths.Count(width => width.IsStar && width.Value == 1));
        Assert.Equal(explorerVisible, widths[1].IsAuto);
        Assert.Equal(!explorerVisible, widths[1].IsAbsolute && widths[1].Value == 0);
        Assert.Equal(!explorerVisible, widths[0].IsAbsolute && widths[0].Value == 0);
        Assert.Equal(!docVisible, widths[4].IsAbsolute && widths[4].Value == 0);
    }
}

public class TreeFontWeightRegressionTests
{
    [Theory]
    [InlineData(true, "Bold")]
    [InlineData(false, "Normal")]
    public void DirectoryNamesStayBoldAndFileNamesStayRegular(bool isDirectory, string expected)
    {
        var weight = RootFontWeightConverter.Instance.Convert(
            isDirectory,
            typeof(FontWeight),
            parameter: null,
            culture: System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, weight?.ToString());
    }
}

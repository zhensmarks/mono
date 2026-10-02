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

public class NavigationVisualRegressionTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "WORKSPACES.md")))
                return System.IO.File.ReadAllText(System.IO.Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("Could not locate the mono repository root from the test output directory.");
    }

    [Fact]
    public void LogPanelRetainsVisibleOnlyExplorerDividerAndOutlinedNavigationContainer()
    {
        var view = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/LogPanelSidebar.axaml");

        Assert.Contains("BorderBrush=\"{DynamicResource NavigationDividerBrush}\"", view);
        Assert.Contains("ColumnDefinitions=\"*,Auto,*,*,*\"", view);
        Assert.Contains("x:Name=\"ExplorerLogSeparator\"", view);
        Assert.Contains("Classes=\"navigationSeparator\"", view);
        Assert.Contains("IsVisible=\"{Binding IsOutputExplorerVisible}\"", view);
        Assert.Contains("IsVisible=\"{Binding !BatchVM.IsDocFloating}\"", view);
    }

    [Fact]
    public void DashboardSeparatesTheAccountGroupAndIconTargetsRemainAtLeast32Px()
    {
        var dashboard = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/DashboardView.axaml");
        var designSystem = ReadRepoFile("apps/bmachine/src/BMachine.UI/Styles/BMachineDesignSystem.axaml");

        Assert.Contains("BorderBrush=\"{DynamicResource NavigationDividerBrush}\"", dashboard);
        Assert.Contains("Classes=\"navigationSeparator\"", dashboard);
        Assert.Contains("Button.IconBtn, Button.iconBtn", designSystem);
        Assert.Contains("Style Selector=\"Button.docTool\"", designSystem);
        Assert.Contains("MinWidth\" Value=\"32\"", designSystem);
        Assert.Contains("MinHeight\" Value=\"32\"", designSystem);
    }

    [Theory]
    [InlineData("LightTheme.axaml", "#121212")]
    [InlineData("DarkTheme.axaml", "#FFFFFF")]
    public void RadialHoveredItemUsesThemeContrastForeground(string themeFile, string expectedColor)
    {
        var theme = ReadRepoFile($"apps/bmachine/src/BMachine.UI/Themes/{themeFile}");
        var radial = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/RadialMenuWindow.axaml");
        var designSystem = ReadRepoFile("apps/bmachine/src/BMachine.UI/Styles/BMachineDesignSystem.axaml");

        Assert.Contains($"x:Key=\"RadialMenuHoverForegroundBrush\" Color=\"{expectedColor}\"", theme);
        Assert.Contains("Classes.highlighted=\"{Binding IsHighlighted}\"", radial);
        Assert.Contains("Button.MenuButton.highlighted", radial);
        Assert.Contains("DynamicResource RadialMenuHoverForegroundBrush", radial);
        Assert.Contains("Button PathIcon", designSystem);
        Assert.Contains("RelativeSource AncestorType=Button", designSystem);
        Assert.DoesNotContain("PathIcon Data=\"{Binding Icon}\" Width=\"18\" Height=\"18\" Foreground=", radial);
    }

    [Fact]
    public void DocPanelKeepsVisibleBordersAndComfortableLogoActionTargets()
    {
        var docPanel = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/DocPanelView.axaml");

        Assert.Contains("BorderBrush=\"{DynamicResource BorderDefaultBrush}\"", docPanel);
        Assert.Contains("Style Selector=\"Button.logoAction\"", docPanel);
        Assert.Contains("MinWidth\" Value=\"32\"", docPanel);
        Assert.Contains("MinHeight\" Value=\"32\"", docPanel);
        Assert.Contains("Button.logoAction:pointerover", docPanel);
        Assert.Contains("PathIcon Data=\"{StaticResource IconClipboard}\" Width=\"14\" Height=\"14\"", docPanel);
    }

    [Fact]
    public void TablerDictionaryRetainsLegacyIconKeysAndNewPickerIcons()
    {
        var icons = ReadRepoFile("apps/bmachine/src/BMachine.UI/Styles/TablerIcons.axaml");
        var picker = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/IconPickerWindow.axaml.cs");

        foreach (var key in new[]
        {
            "IconEye", "IconEyeOff", "IconFilter", "IconLayers", "IconList", "IconLogin", "IconLogout", "IconMap",
            "IconGraduation", "IconImageSimple", "IconMessageSquare", "IconSettingsCircle", "IconSettingsSimple", "IconUploadCloud", "IconZap",
            "IconLogIn", "IconLogOut"
        })
        {
            Assert.Contains($"x:Key=\"{key}\"", icons);
        }

        foreach (var key in new[] { "IconEye", "IconEyeOff", "IconFilter", "IconLayers", "IconList", "IconLogin", "IconLogout", "IconMap" })
        {
            Assert.Contains($"\"{key}\"", picker);
        }
    }
}

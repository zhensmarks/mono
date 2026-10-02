using System.Xml.Linq;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

public class NavigationNoIconsRegressionTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mono repository root from the test output directory.");
    }

    private static XDocument ReadAxaml(string relativePath) => XDocument.Parse(ReadRepoFile(relativePath));

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name == "Name" ? Xaml + name : XName.Get(name))?.Value;

    private static bool IsIconElement(XElement element) => element.Name.LocalName is
        "PathIcon" or "Image" or "Icon" or "FontIcon" or "GeometryIcon" or "SymbolIcon" or "BitmapIcon";

    private static bool ContainsVisualIcon(XElement control) =>
        control.DescendantsAndSelf().Any(IsIconElement);

    private static bool ContainsNavigationGlyph(XElement control) =>
        control.DescendantsAndSelf()
            .Where(element => element.Name == Avalonia + "TextBlock")
            .Select(element => Attribute(element, "Text") ?? string.Empty)
            .Any(text => text.Any(character => character is '\u2190' or '\u2192' or '\u2630' or '\u25A6' or '\u2728'));

    private static string[] TextLabels(XElement control) => control.DescendantsAndSelf()
        .Where(element => element.Name == Avalonia + "TextBlock")
        .Select(element => Attribute(element, "Text") ?? string.Empty)
        .ToArray();

    private static XElement FindNamed(XDocument document, string name) =>
        document.Descendants().Single(element => Attribute(element, "Name") == name);

    [Fact]
    public void LogPanelTabsAreTextOnlyAndKeepEqualCompactSlotsAndStateStyles()
    {
        var document = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/LogPanelSidebar.axaml");
        var tabs = document.Descendants(Avalonia + "RadioButton")
            .Where(tab => Attribute(tab, "GroupName") == "LogPanelTabs" && Attribute(tab, "IsVisible") != "False")
            .ToArray();

        Assert.Equal(new[] { "EXPLORER", "LOG", "MASTER", "DOC" }, tabs.Select(tab => Assert.Single(TextLabels(tab))));
        foreach (var tab in tabs)
        {
            Assert.False(ContainsVisualIcon(tab));
            Assert.False(ContainsNavigationGlyph(tab));
            Assert.Equal("LogPanelTab", Attribute(tab, "Classes")?.Split(' ').Last());
            Assert.Equal("2", Attribute(tab.Descendants(Avalonia + "StackPanel").Single(), "Spacing"));
            Assert.Equal("NoWrap", Attribute(tab.Descendants(Avalonia + "TextBlock").Single(), "TextWrapping"));
        }

        Assert.Equal("*,Auto,*,*,*", Attribute(FindNamed(document, "PanelTabsGrid"), "ColumnDefinitions"));
        Assert.Equal(4, PanelNavigationLayout.GetColumnWidths(explorerVisible: true, docVisible: true).Count(width => width.IsStar));

        var styles = ReadRepoFile("apps/bmachine/src/BMachine.UI/Styles/TrelloNavigation.axaml");
        Assert.Contains("<Setter Property=\"Height\" Value=\"36\" />", styles);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"0\" />", styles);
        Assert.Contains("RadioButton.LogPanelTab:checked", styles);
        Assert.Contains("RadioButton.LogPanelTab:pointerover", styles);
        Assert.Contains("RadioButton.LogPanelTab:focus", styles);
        Assert.Contains("RadioButton.LogPanelTab:pressed", styles);
    }

    [Fact]
    public void SettingsSectionNavigationKeepsTextOrderSeparatorsAndInteractionStatesWithoutIcons()
    {
        var document = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml");
        var expected = new (string Name, string Label, string? Classes)[]
        {
            ("SettingsItemGeneral", "{Binding Language[Nav.General]}", null),
            ("SettingsItemAppearance", "{Binding Language[Nav.Appearance]}", null),
            ("SettingsItemAccount", "{Binding Language[Nav.Account]}", "sep"),
            ("SettingsItemExtensions", "{Binding Language[Nav.Extensions]}", null),
            ("SettingsItemScriptManager", "Script Manager", "sep"),
            ("SettingsItemPaths", "Paths", null),
            ("SettingsItemAbout", "About", "sep")
        };

        var items = document.Descendants(Avalonia + "ListBoxItem")
            .Where(item => Attribute(item, "Name")?.StartsWith("SettingsItem", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(expected.Length, items.Length);

        foreach (var (item, entry) in items.Zip(expected))
        {
            Assert.Equal(entry.Name, Attribute(item, "Name"));
            Assert.Equal(entry.Classes, Attribute(item, "Classes"));
            Assert.False(ContainsVisualIcon(item));
            Assert.False(ContainsNavigationGlyph(item));
            var text = Assert.Single(item.Descendants(Avalonia + "TextBlock"));
            Assert.Equal(entry.Label, Attribute(text, "Text"));
            Assert.Equal("12", Attribute(text, "FontSize"));
            Assert.Equal("9", Attribute(item.Descendants(Avalonia + "StackPanel").Single(), "Spacing"));
        }

        var source = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml");
        Assert.Contains("ListBoxItem:pointerover", source);
        Assert.Contains("ListBoxItem:selected", source);
        Assert.Contains("ListBoxItem:focus", source);
        Assert.Contains("Height\" Value=\"22\"", source);

        var back = document.Descendants(Avalonia + "Button").Single(button => Attribute(button, "Click") == "OnMobileBackClick");
        Assert.False(ContainsVisualIcon(back));
        Assert.False(ContainsNavigationGlyph(back));
        Assert.Equal("{Binding Language[Nav.Back]}", Attribute(Assert.Single(back.Descendants(Avalonia + "TextBlock")), "Text"));
        Assert.Equal("4", Attribute(back, "Padding"));
        Assert.Equal("0,0,6,0", Attribute(back, "Margin"));

        var language = ReadRepoFile("apps/bmachine/src/BMachine.UI/Services/LanguageService.cs");
        Assert.Contains("[\"Nav.Back\"] = \"Back\"", language);
        Assert.Contains("[\"Nav.Back\"] = \"Kembali\"", language);
    }

    [Fact]
    public void DashboardAndAdvancedModeNavigationAreTextOnly()
    {
        var dashboard = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/DashboardView.axaml");
        var dashboardTabs = dashboard.Descendants(Avalonia + "RadioButton")
            .Where(tab => Attribute(tab, "GroupName") == "NavTabs")
            .ToArray();
        Assert.Equal(new[] { "HOME", "BATCH", "LOCKER" }, dashboardTabs.Select(tab => Assert.Single(TextLabels(tab))));
        Assert.All(dashboardTabs, tab =>
        {
            Assert.False(ContainsVisualIcon(tab));
            Assert.False(ContainsNavigationGlyph(tab));
        });

        var profile = FindNamed(dashboard, "Part_ProfileNavButton");
        Assert.False(ContainsVisualIcon(profile));
        var settingsEntry = dashboard.Descendants(Avalonia + "Button").Single(button => Attribute(button, "Click") == "OnSettingsClick");
        Assert.Contains("Settings", TextLabels(settingsEntry));
        Assert.False(ContainsVisualIcon(settingsEntry));

        var toolbox = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/ToolboxWindow.axaml");
        var back = toolbox.Descendants(Avalonia + "Button")
            .Single(button => (Attribute(button, "Command") ?? string.Empty).Contains("ExitAdvancedModeCommand", StringComparison.Ordinal));
        Assert.Equal("Kembali", Assert.Single(TextLabels(back)));
        Assert.False(ContainsVisualIcon(back));
        Assert.False(ContainsNavigationGlyph(back));
        Assert.Equal("8", Attribute(back.Descendants(Avalonia + "StackPanel").Single(), "Spacing"));

        var advancedEntry = toolbox.Descendants(Avalonia + "Button")
            .Single(button => (Attribute(button, "Command") ?? string.Empty).Contains("ToggleAdvancedModeCommand", StringComparison.Ordinal));
        Assert.Equal(new[] { "Advanced Mode" }, TextLabels(advancedEntry));
        Assert.False(ContainsVisualIcon(advancedEntry));
        Assert.False(ContainsNavigationGlyph(advancedEntry));
        Assert.Equal("7", Attribute(advancedEntry.Descendants(Avalonia + "StackPanel").Single(), "Spacing"));
    }
}

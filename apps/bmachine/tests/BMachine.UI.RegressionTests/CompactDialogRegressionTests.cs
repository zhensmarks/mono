using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.Views;
using BMachine.UI.ViewModels;
using BMachine.UI.Services;
using Avalonia.VisualTree;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class CompactDialogRegressionTests
{
    [AvaloniaFact]
    public void IconPickerIsCompactResizableAndEscapeCancels()
    {
        var window = new IconPickerWindow();
        try
        {
            Assert.Equal(440, window.Width);
            Assert.Equal(380, window.Height);
            Assert.True(window.CanResize);
            window.Show();
            Assert.NotNull(window.FindControl<Border>("DragHeader"));
            window.KeyPress(Key.Escape, RawInputModifiers.None);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExplorerSettingsHasExplicitCloseAndSafeMinimumSize()
    {
        var window = new ExplorerSettingsWindow();
        try
        {
            Assert.Equal(480, window.Width);
            Assert.Equal(540, window.Height);
            Assert.Equal(440, window.MinWidth);
            Assert.True(window.CanResize);
            window.Show();
            Assert.NotNull(window.FindControl<Border>("DragHeader"));
            var root = Assert.IsType<Border>(window.Content);
            var grid = Assert.IsType<Grid>(root.Child);
            var footer = Assert.IsType<Border>(grid.Children.Last());
            var footerGrid = Assert.IsType<Grid>(footer.Child);
            var done = Assert.Single(footerGrid.Children.OfType<Button>());
            done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExplorerSettingsShowsOnlyTheSelectedTab()
    {
        var vm = new OutputExplorerViewModel(new FakeDatabase(), new FakeNotificationService(),
            new FileOperationManager(), new FakePlatformService(), new FakeExplorerClipboard());
        var window = new ExplorerSettingsWindow { DataContext = vm };
        try
        {
            window.Show();
            var appearance = window.GetVisualDescendants().OfType<ExplorerSettingsView>().Single();
            vm.ExplorerSettingsVM.IsGeneralTabVisible = true;
            vm.ExplorerSettingsVM.IsShortcutsTabVisible = false;
            Assert.False(appearance.IsEffectivelyVisible);
            vm.ExplorerSettingsVM.IsGeneralTabVisible = false;
            vm.ExplorerSettingsVM.IsShortcutsTabVisible = true;
            Assert.True(appearance.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("IconPickerWindow.axaml")]
    [InlineData("ExplorerSettingsWindow.axaml")]
    public void HeaderDragDoesNotIncludeCloseButton(string file)
    {
        var xml = LoadView(file);
        var header = xml.Descendants().Single(e => (string?)e.Attribute("Name") == "DragHeader");
        Assert.Equal("OnHeaderPointerPressed", (string?)header.Attribute("PointerPressed"));
        Assert.DoesNotContain(header.Descendants(), e => e.Name.LocalName == "Button");
        Assert.Contains(header.Parent!.Elements(), e => e.Name.LocalName == "Button" && e.Attribute("Click") != null);
    }

    [Fact]
    public void ExplorerTabsDoNotOverlapScrollingContent()
    {
        var xml = LoadView("ExplorerSettingsWindow.axaml");
        var main = xml.Descendants().First(e => (string?)e.Attribute("RowDefinitions") == "Auto,Auto,*,Auto");
        Assert.NotNull(main);
        var nestedView = xml.Descendants().Single(e => e.Name.LocalName == "ExplorerSettingsView");
        Assert.DoesNotContain(nestedView.Ancestors(), e => e.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(xml.Descendants(), e => (string?)e.Attribute("Width") == "300");
    }

    private static XDocument LoadView(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "apps/bmachine/src/BMachine.UI/Views", file);
            if (File.Exists(path)) return XDocument.Load(path);
        }
        throw new FileNotFoundException(file);
    }
}

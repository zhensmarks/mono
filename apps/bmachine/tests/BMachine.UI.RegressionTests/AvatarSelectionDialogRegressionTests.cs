using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class AvatarSelectionDialogRegressionTests
{
    [AvaloniaFact]
    public void DialogHasCompactResizableGeometryAndFooterFitsAtMinimumSize()
    {
        var window = new AvatarSelectionWindow { DataContext = new AvatarSelectionViewModel() };
        try
        {
            Assert.Equal(480, window.Width);
            Assert.Equal(400, window.Height);
            Assert.Equal(400, window.MinWidth);
            Assert.Equal(320, window.MinHeight);
            Assert.True(window.CanResize);

            window.Show();
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.UpdateLayout();

            Assert.NotNull(window.FindControl<Border>("DragHeader"));
            Assert.NotNull(window.FindControl<ScrollViewer>("PresetScroll"));
            Assert.NotNull(window.FindControl<Control>("PreviewPanel"));
            var footer = window.FindControl<Control>("DialogFooter");
            Assert.NotNull(footer);
            Assert.True(footer.IsEffectivelyVisible);
            Assert.True(footer.Bounds.Height > 0);
            Assert.True(footer.Bounds.Bottom <= window.Bounds.Height + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeClosesDialogWithoutSaving()
    {
        var vm = new AvatarSelectionViewModel();
        var saveCount = 0;
        vm.OnAvatarSelected += _ => saveCount++;
        var window = new AvatarSelectionWindow { DataContext = vm };
        try
        {
            window.Show();
            window.KeyPress(Key.Escape, RawInputModifiers.None);
            Assert.False(window.IsVisible);
            Assert.Equal(0, saveCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExplicitCloseButtonClosesDialogWithoutSaving()
    {
        var vm = new AvatarSelectionViewModel();
        var saveCount = 0;
        vm.OnAvatarSelected += _ => saveCount++;
        var window = new AvatarSelectionWindow { DataContext = vm };
        try
        {
            window.Show();
            var close = window.FindControl<Button>("CloseButton");
            Assert.NotNull(close);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(0, saveCount);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SelectingSyntheticPresetUpdatesPreviewAndSourceAndConfirmRaisesOnce()
    {
        var vm = new AvatarSelectionViewModel();
        var item = new AvatarSelectionViewModel.AvatarItem
        {
            Source = "preset:synthetic.png",
            Image = CreateTestBitmap()
        };
        vm.Presets.Clear();
        vm.Presets.Add(item);

        var selectedSources = new List<string>();
        vm.OnAvatarSelected += selectedSources.Add;

        vm.SelectAvatarCommand.Execute(item);
        Assert.Same(item, vm.SelectedAvatar);
        Assert.Same(item.Image, vm.PreviewImage);
        Assert.Equal(item.Source, vm.SelectedSource);

        vm.ConfirmCommand.Execute(null);
        Assert.Single(selectedSources);
        Assert.Equal(item.Source, selectedSources[0]);
    }

    [Fact]
    public void CancelRaisesCancelOnlyAndDoesNotSave()
    {
        var vm = new AvatarSelectionViewModel();
        var saveCount = 0;
        var cancelCount = 0;
        vm.OnAvatarSelected += _ => saveCount++;
        vm.OnCancel += () => cancelCount++;

        vm.CancelCommand.Execute(null);

        Assert.Equal(0, saveCount);
        Assert.Equal(1, cancelCount);
    }

    [Fact]
    public async Task CustomUploadUsesTemporaryImageAndBecomesSelectedSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avatar-regression-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        try
        {
            var vm = new AvatarSelectionViewModel
            {
                PickCustomImageFunc = () => Task.FromResult<string?>(path)
            };

            await vm.UploadPhotoCommand.ExecuteAsync(null);

            Assert.StartsWith("custom:", vm.SelectedSource);
            Assert.Equal($"custom:{path}", vm.SelectedSource);
            Assert.NotNull(vm.PreviewImage);
            Assert.Null(vm.SelectedAvatar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderMarkupKeepsCloseButtonOutsideDragSurface()
    {
        var xml = LoadView("AvatarSelectionWindow.axaml");
        var header = xml.Descendants().Single(e => (string?)e.Attribute("Name") == "DragHeader");
        Assert.Equal("OnHeaderPointerPressed", (string?)header.Attribute("PointerPressed"));
        Assert.DoesNotContain(header.Descendants(), e => e.Name.LocalName == "Button");
        Assert.Contains(xml.Descendants(), e => (string?)e.Attribute("Name") == "CloseButton" && e.Name.LocalName == "Button");
        Assert.Contains(xml.Descendants(), e => (string?)e.Attribute("Name") == "CloseButton" && (string?)e.Attribute("Click") == "OnCancelClick");
    }

    private static Bitmap CreateTestBitmap()
    {
        return new Bitmap(new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")));
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

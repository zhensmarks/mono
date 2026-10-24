using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
using Xunit;

namespace FrameStudio.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void StartsWithExplicitPhotoTextAndLogoSlots()
    {
        var viewModel = new MainViewModel();

        Assert.Contains(viewModel.Slots, slot => slot.Id == "xl" && slot.IsPhoto);
        Assert.Contains(viewModel.Slots, slot => slot.Id == "s" && slot.IsPhoto);
        Assert.Contains(viewModel.Slots, slot => slot.Id == "name" && slot.IsText);
        Assert.Contains(viewModel.Slots, slot => slot.Id == "address" && slot.IsText);
        Assert.Contains(viewModel.Slots, slot => slot.Id == "school-logo" && slot.IsLogo);
    }

    [Fact]
    public void SelectingMasterDoesNotEnableExportOrClaimItWasParsed()
    {
        var viewModel = new MainViewModel();

        viewModel.SetMaster("/samples/master.psd");

        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
        Assert.False(viewModel.PsdEngineReady);
        Assert.False(viewModel.CanExport);
        Assert.Contains("belum dibaca", viewModel.TemplateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectingMasterWithPreviewAdapterReportsUnsupportedWithoutReplacingExampleSlots()
    {
        var viewModel = new MainViewModel();
        viewModel.SetMaster("/samples/master.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.False(viewModel.PsdEngineReady);
        Assert.False(viewModel.CanExport);
        Assert.Contains("belum dibaca", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.Slots, slot => slot.Id == "xl");
    }

    [Fact]
    public void AssigningPhotoToPhotoSlotDoesNotChangeMasterPath()
    {
        var viewModel = new MainViewModel();
        viewModel.SetMaster("/samples/master.psd");
        var photo = new SourcePhoto("/samples/student.jpg", null);
        viewModel.AddSourcePhoto(photo);

        var assigned = viewModel.AssignSelectedPhoto(viewModel.Slots.Single(slot => slot.Id == "xl"));

        Assert.True(assigned);
        var photoSlot = viewModel.Slots.Single(slot => slot.Id == "xl");
        Assert.Same(photo, photoSlot.Photo);
        Assert.Equal("student.jpg", photoSlot.DisplayValue);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
        Assert.False(viewModel.CanExport);
        Assert.Contains("belum mengubah PSD", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhotoCannotBeAssignedToTextLayer()
    {
        var viewModel = new MainViewModel();
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.jpg", null));
        var textSlot = viewModel.Slots.Single(slot => slot.Id == "name");

        Assert.False(viewModel.AssignSelectedPhoto(textSlot));
        Assert.Null(textSlot.Photo);
    }

    [Fact]
    public void ClearingQueueRemovesDemoAssignmentsButPreservesMasterPath()
    {
        var viewModel = new MainViewModel();
        viewModel.SetMaster("/samples/master.psd");
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.png", null));
        viewModel.AssignSelectedPhoto(viewModel.Slots.Single(slot => slot.Id == "xl"));

        viewModel.ClearQueue();

        Assert.Empty(viewModel.SourcePhotos);
        Assert.Null(viewModel.Slots.Single(slot => slot.Id == "xl").Photo);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
    }

    [Fact]
    public void PhotoPickerAcceptsCommonRasterImagesButNotPsdAsPhotos()
    {
        var fileService = new PhotoFileService();

        Assert.True(fileService.IsSupported("STUDENT.JPG"));
        Assert.True(fileService.IsSupported("student.webp"));
        Assert.False(fileService.IsSupported("template.psd"));
    }

    [Fact]
    public void PreviewModeButtonsChangeOnlyLocalCropState()
    {
        var viewModel = new MainViewModel();
        viewModel.SetMaster("/samples/master.psd");
        viewModel.Zoom = 1.5;
        viewModel.Rotation = 12;

        viewModel.SetPreviewMode(showWholePhoto: true);

        Assert.True(viewModel.IsFitMode);
        Assert.Equal(1, viewModel.Zoom);
        Assert.Equal(0, viewModel.OffsetX);
        Assert.Equal(0, viewModel.OffsetY);
        Assert.False(viewModel.CanExport);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
    }

    [Theory]
    [InlineData(-1, 0.5)]
    [InlineData(0.6, 0.6)]
    [InlineData(8, 3)]
    public void ZoomIsClampedToSupportedPreviewRange(double requested, double expected)
    {
        var viewModel = new MainViewModel { Zoom = requested };

        Assert.Equal(expected, viewModel.Zoom);
    }
}

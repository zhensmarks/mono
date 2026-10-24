using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace FrameStudio.Tests;

public sealed class MainViewModelTests
{
    private static readonly byte[] PreviewPng = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void StartsEmptyWithoutSyntheticSlotsOrPreview()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter());

        Assert.Empty(viewModel.Slots);
        Assert.Null(viewModel.TemplatePreviewPng);
        Assert.False(viewModel.HasTemplatePreview);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Fact]
    public void EngineStatusShowsRuntimePlatformAndKeepsLayeredPsdExportDisabled()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter());

        Assert.False(string.IsNullOrWhiteSpace(viewModel.RuntimePlatformLabel));
        Assert.Contains(viewModel.RuntimePlatformLabel, viewModel.EngineNotice);
        Assert.Contains("PsdSharp 1.2.0", viewModel.EngineNotice);
        Assert.Contains("PSD berlapis: nonaktif", viewModel.EngineNotice);
        Assert.Contains("psd-tools belum diaktifkan", viewModel.EngineDetails);
        Assert.Contains("hanya PNG raster", viewModel.ExportNotice);
    }

    [Fact]
    public void EngineStatusExplainsWhenTheReaderIsUnavailable()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { IsAvailable = false });

        Assert.Contains("pembaca PSD tidak tersedia", viewModel.EngineNotice);
        Assert.Contains("PSD berlapis: nonaktif", viewModel.EngineNotice);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Fact]
    public async Task InspectionReplacesSlotsWithOnlyNameInferredCandidatesAndComposite()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = Inspection(
                new PsdSlotDescriptor("photo-xl", "Photo XL", PsdSlotType.Photo, "Photo XL"),
                new PsdSlotDescriptor("student-name", "Student Name", PsdSlotType.Text, "Student Name"),
                new PsdSlotDescriptor("school-logo", "School Logo", PsdSlotType.Logo, "School Logo"))
        };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/master.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.Equal(3, viewModel.Slots.Count);
        Assert.Equal(SlotKind.Photo, viewModel.Slots[0].Kind);
        Assert.Equal(SlotKind.Text, viewModel.Slots[1].Kind);
        Assert.Equal(SlotKind.Logo, viewModel.Slots[2].Kind);
        Assert.All(viewModel.Slots, slot => Assert.Contains("Kandidat dari nama layer", slot.Helper));
        Assert.All(viewModel.Slots, slot => Assert.Null(slot.Value));
        Assert.Equal(PreviewPng, viewModel.TemplatePreviewPng);
        Assert.Equal("120 × 180 px", viewModel.DocumentDimensions);
        Assert.Equal(120, viewModel.DocumentWidth);
        Assert.Equal(180, viewModel.DocumentHeight);
        Assert.True(viewModel.HasTemplatePreview);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.Contains("kandidat nama layer", viewModel.TemplateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedInspectionClearsPriorCandidatesAndComposite()
    {
        var adapter = new FakePsdTemplateAdapter { InspectionResult = Inspection(new PsdSlotDescriptor("xl", "XL", PsdSlotType.Photo, "XL")) };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/first.psd");
        await viewModel.InspectSelectedMasterAsync();
        Assert.True(viewModel.HasTemplatePreview);
        Assert.Single(viewModel.Slots);

        adapter.InspectionResult = new PsdTemplateInspection(false, "broken PSD", Array.Empty<PsdSlotDescriptor>());
        viewModel.SetMaster("/samples/broken.psd");
        await viewModel.InspectSelectedMasterAsync();

        Assert.Empty(viewModel.Slots);
        Assert.False(viewModel.HasTemplatePreview);
        Assert.Contains("broken PSD", viewModel.Status);
    }

    [Fact]
    public async Task OlderInspectionCannotReplaceNewerMasterResult()
    {
        var adapter = new FakePsdTemplateAdapter();
        var pendingOldResult = new TaskCompletionSource<PsdTemplateInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.EnqueueInspection((_, _) => pendingOldResult.Task);
        adapter.EnqueueInspection((_, _) => Task.FromResult(Inspection(new PsdSlotDescriptor("new", "New Photo", PsdSlotType.Photo, "New Photo"))));
        var viewModel = new MainViewModel(adapter);

        viewModel.SetMaster("/samples/old.psd");
        var olderInspection = viewModel.InspectSelectedMasterAsync();
        viewModel.SetMaster("/samples/new.psd");
        await viewModel.InspectSelectedMasterAsync();
        pendingOldResult.SetResult(Inspection(new PsdSlotDescriptor("old", "Old Photo", PsdSlotType.Photo, "Old Photo")));
        await olderInspection;

        Assert.Equal("/samples/new.psd", viewModel.MasterPath);
        Assert.Single(viewModel.Slots);
        Assert.Equal("new", viewModel.Slots[0].Id);
        Assert.Equal(PreviewPng, viewModel.TemplatePreviewPng);
    }

    [Fact]
    public async Task AssigningPhotoUpdatesRasterPlacementWithoutChangingMasterOrBaseComposite()
    {
        var adapter = new FakePsdTemplateAdapter { InspectionResult = Inspection(new PsdSlotDescriptor("xl", "XL", PsdSlotType.Photo, "XL")) };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();
        var previewBefore = viewModel.TemplatePreviewPng;
        var photo = new SourcePhoto("/samples/student.jpg", null);
        viewModel.AddSourcePhoto(photo);

        var assigned = viewModel.AssignSelectedPhoto(viewModel.Slots.Single());

        Assert.True(assigned);
        Assert.Same(photo, viewModel.Slots.Single().Photo);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
        Assert.Same(previewBefore, viewModel.TemplatePreviewPng);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.Contains("PSD master tidak diubah", viewModel.Status);
    }

    [Fact]
    public async Task PhotoCannotBeAssignedToTextCandidate()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { InspectionResult = Inspection(new PsdSlotDescriptor("name", "Name", PsdSlotType.Text, "Name")) });
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.jpg", null));

        Assert.False(viewModel.AssignSelectedPhoto(viewModel.Slots.Single()));
        Assert.Null(viewModel.Slots.Single().Photo);
    }

    [Fact]
    public async Task SelectingEmptyPhotoCandidatePreservesQueueSelection()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { InspectionResult = Inspection(new PsdSlotDescriptor("xl", "XL", PsdSlotType.Photo, "XL")) });
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();
        var photo = new SourcePhoto("/samples/student.jpg", null);
        viewModel.AddSourcePhoto(photo);

        viewModel.SelectSlot(viewModel.Slots.Single());

        Assert.Same(photo, viewModel.SelectedPhoto);
    }

    [Fact]
    public async Task CropTransformsRemainIndependentPerSelectedSlot()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = Inspection(
                new PsdSlotDescriptor("photo-a", "Photo A", PsdSlotType.Photo, "Photo A", Bounds: new PsdSlotBounds(5, 6, 40, 60)),
                new PsdSlotDescriptor("photo-b", "Photo B", PsdSlotType.Photo, "Photo B", Bounds: new PsdSlotBounds(50, 6, 40, 60)),
                new PsdSlotDescriptor("student-name", "Student Name", PsdSlotType.Text, "Student Name", Bounds: new PsdSlotBounds(10, 80, 70, 20)))
        };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();

        var first = viewModel.Slots[0];
        var second = viewModel.Slots[1];
        viewModel.SelectSlot(first);
        Assert.True(viewModel.IsImageSlotSelected);
        viewModel.Zoom = 1.8;
        viewModel.Rotation = 12;
        viewModel.OffsetX = 0.2;
        viewModel.SelectSlot(second);
        viewModel.Zoom = 1.2;
        viewModel.OffsetY = -0.15;
        viewModel.SelectSlot(viewModel.Slots[2]);
        Assert.True(viewModel.HasTextCandidateSelected);
        Assert.False(viewModel.IsImageSlotSelected);

        viewModel.SelectSlot(first);

        Assert.Contains("%", viewModel.ZoomLabel);
        Assert.Equal("12°", viewModel.RotationLabel);
        Assert.Equal(1.8, viewModel.Zoom);
        Assert.Equal(12, viewModel.Rotation);
        Assert.Equal(0.2, viewModel.OffsetX);
        Assert.Equal(0, viewModel.OffsetY);
        Assert.Equal(new FrameSlotBounds(5, 6, 40, 60), first.Bounds);
        Assert.Equal(1.2, second.Zoom);
        Assert.Equal(-0.15, second.OffsetY);
    }

    [Fact]
    public void RasterComposerDrawsPhotoIntoEstimatedRectangleWithoutChangingPixelsOutsideIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-compose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var photoPath = Path.Combine(root, "photo.png");
        try
        {
            var background = CreateSolidPng(10, 10, new SKColor(20, 40, 180));
            File.WriteAllBytes(photoPath, CreateSolidPng(4, 4, new SKColor(230, 40, 30)));
            var slot = new FrameSlot("photo", "Photo", SlotKind.Photo, "synthetic", bounds: new FrameSlotBounds(3, 3, 4, 4))
            {
                Photo = new SourcePhoto(photoPath, null)
            };

            var output = new RasterPngComposer().Compose(background, new[] { slot });
            using var composed = SKBitmap.Decode(output);

            Assert.NotNull(composed);
            Assert.Equal(10, composed!.Width);
            Assert.Equal(10, composed.Height);
            Assert.Equal(new SKColor(230, 40, 30), composed.GetPixel(4, 4));
            Assert.Equal(new SKColor(20, 40, 180), composed.GetPixel(1, 1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RasterComposerRendersTextCandidateAsAnOverlay()
    {
        var background = CreateSolidPng(100, 40, new SKColor(20, 40, 180));
        var slot = new FrameSlot("name", "Nama", SlotKind.Text, "synthetic", bounds: new FrameSlotBounds(10, 5, 80, 30))
        {
            Value = "NAMA TEST"
        };

        var output = new RasterPngComposer().Compose(background, new[] { slot });
        using var composed = SKBitmap.Decode(output);

        Assert.NotNull(composed);
        var foundBrightTextPixel = false;
        for (var y = 5; y < 35 && !foundBrightTextPixel; y++)
        for (var x = 10; x < 90; x++)
        {
            var pixel = composed!.GetPixel(x, y);
            if (pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240)
            {
                foundBrightTextPixel = true;
                break;
            }
        }

        Assert.True(foundBrightTextPixel, "The text candidate should be rasterized into the output bounds.");
    }

    private static byte[] CreateSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
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
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter());
        viewModel.SetMaster("/samples/master.psd");
        viewModel.Zoom = 1.5;
        viewModel.Rotation = 12;

        viewModel.SetPreviewMode(showWholePhoto: true);

        Assert.True(viewModel.IsFitMode);
        Assert.Equal(1, viewModel.Zoom);
        Assert.Equal(0, viewModel.OffsetX);
        Assert.Equal(0, viewModel.OffsetY);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
    }

    [Theory]
    [InlineData(-1, 0.5)]
    [InlineData(0.6, 0.6)]
    [InlineData(8, 3)]
    public void ZoomIsClampedToSupportedPreviewRange(double requested, double expected)
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter()) { Zoom = requested };

        Assert.Equal(expected, viewModel.Zoom);
    }

    private static PsdTemplateInspection Inspection(params PsdSlotDescriptor[] slots) =>
        new(true, "Read PSD; candidates are based on layer names.", slots, PreviewPng, 120, 180);

    private sealed class FakePsdTemplateAdapter : IPsdTemplateAdapter
    {
        private readonly Queue<Func<string, CancellationToken, Task<PsdTemplateInspection>>> _inspectionResponses = new();

        public bool IsAvailable { get; set; } = true;
        public PsdTemplateInspection InspectionResult { get; set; } = new(false, "not inspected", Array.Empty<PsdSlotDescriptor>());

        public void EnqueueInspection(Func<string, CancellationToken, Task<PsdTemplateInspection>> response) => _inspectionResponses.Enqueue(response);

        public Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default) =>
            _inspectionResponses.Count > 0
                ? _inspectionResponses.Dequeue()(masterPath, cancellationToken)
                : Task.FromResult(InspectionResult);
    }
}

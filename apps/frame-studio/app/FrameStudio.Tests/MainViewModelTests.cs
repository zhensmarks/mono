using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
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
        Assert.False(viewModel.CanExportFlattenedPreview);
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
        Assert.All(viewModel.Slots, slot => Assert.Contains("Ditebak dari nama layer", slot.Helper));
        Assert.All(viewModel.Slots, slot => Assert.Null(slot.Value));
        Assert.Equal(PreviewPng, viewModel.TemplatePreviewPng);
        Assert.Equal("120 × 180 px", viewModel.DocumentDimensions);
        Assert.True(viewModel.HasTemplatePreview);
        Assert.False(viewModel.CanExportFlattenedPreview);
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
    public async Task AssigningPhotoChangesOnlyLocalPreviewState()
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
        Assert.False(viewModel.CanExportFlattenedPreview);
        Assert.Contains("PSD tidak diubah", viewModel.Status);
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
    public async Task FlattenedPreviewExportSendsNoSlotAssignments()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = Inspection(new PsdSlotDescriptor("xl", "XL", PsdSlotType.Photo, "XL")),
            ExportResult = new PsdExportResult(true, "Stored composite exported; assignments not applied.", "/out/master-flattened.png")
        };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();
        viewModel.OutputPath = "/out";
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.jpg", null));
        viewModel.AssignSelectedPhoto(viewModel.Slots.Single());

        Assert.True(viewModel.CanExportFlattenedPreview);
        await viewModel.ExportFlattenedPreviewAsync();

        Assert.NotNull(adapter.LastExportRequest);
        Assert.Empty(adapter.LastExportRequest!.SlotAssignments);
        Assert.Equal("/samples/master.psd", adapter.LastExportRequest.MasterPath);
        Assert.Contains("assignments not applied", viewModel.Status, StringComparison.OrdinalIgnoreCase);
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
        Assert.False(viewModel.CanExportFlattenedPreview);
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

        public bool IsAvailable => true;
        public PsdTemplateInspection InspectionResult { get; set; } = new(false, "not inspected", Array.Empty<PsdSlotDescriptor>());
        public PsdExportResult ExportResult { get; set; } = new(false, "not exported");
        public PsdExportRequest? LastExportRequest { get; private set; }

        public void EnqueueInspection(Func<string, CancellationToken, Task<PsdTemplateInspection>> response) => _inspectionResponses.Enqueue(response);

        public Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default) =>
            _inspectionResponses.Count > 0
                ? _inspectionResponses.Dequeue()(masterPath, cancellationToken)
                : Task.FromResult(InspectionResult);

        public Task<PsdExportResult> ExportCopyAsync(PsdExportRequest request, CancellationToken cancellationToken = default)
        {
            LastExportRequest = request;
            return Task.FromResult(ExportResult);
        }
    }
}

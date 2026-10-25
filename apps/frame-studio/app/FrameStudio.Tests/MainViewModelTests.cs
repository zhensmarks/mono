using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
using FrameStudio.Engine;
using SkiaSharp;
using System.Runtime.InteropServices;
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
        Assert.False(viewModel.CanAssignSelectedPhoto);
        Assert.True(viewModel.ShowEmptyInspector);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Fact]
    public void EngineStatusShowsRuntimePlatformAndKeepsLayeredPsdExportDisabled()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter());

        Assert.False(string.IsNullOrWhiteSpace(viewModel.RuntimePlatformLabel));
        Assert.Contains(viewModel.RuntimePlatformLabel, viewModel.EngineNotice);
        Assert.Contains("PsdSharp 1.2.0", viewModel.EngineNotice);
        Assert.Contains("PSD berlapis nonaktif", viewModel.EngineNotice);
        Assert.Contains("Layered PSD writing is disabled", viewModel.EngineDetails);
        Assert.Contains("Ekspor PNG nonaktif", viewModel.ExportNotice);
    }

    [Fact]
    public void DefaultAdapterReportsPlatformGateAndBoundedReadOnlyFallback()
    {
        var adapter = new PsdTemplateEngineUiAdapter();
        var supportedPlatform = OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

        if (supportedPlatform)
            Assert.Contains("Linux X64", adapter.CapabilityNotice);
        else
        {
            Assert.Contains("tidak aktif", adapter.CapabilityNotice);
            Assert.Null(adapter.Capabilities.OptionalFallback);
        }

        Assert.Contains("Python 3.11+", adapter.CapabilityDetails);
        Assert.Contains("psd-tools==1.26.0", adapter.CapabilityDetails);
        Assert.Contains("512 MiB input", adapter.CapabilityDetails);
        Assert.Contains("3 GiB child-process address-space cap", adapter.CapabilityDetails);
        Assert.Contains("120-second timeout", adapter.CapabilityDetails);
        Assert.Contains("PSD berlapis nonaktif", adapter.CapabilityNotice);
        Assert.False(adapter.Capabilities.CanWriteLayeredPsd);
    }

    [Fact]
    public void EngineStatusExplainsWhenTheReaderIsUnavailable()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { IsAvailable = false });

        Assert.Contains("pembaca PSD tidak tersedia", viewModel.EngineNotice);
        Assert.Contains("PSD berlapis nonaktif", viewModel.EngineNotice);
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
        Assert.Equal(new[] { "Foto", "Teks", "Logo" }, viewModel.Slots.Select(slot => slot.KindLabel));
        Assert.All(viewModel.Slots, slot => Assert.Contains("Posisi otomatis", slot.Helper));
        Assert.Equal(new[] { "Foto 1", "Teks 1", "Logo 1" }, viewModel.Slots.Select(slot => slot.Label));
        Assert.DoesNotContain("Student Name", string.Join(" ", viewModel.Slots.Select(slot => $"{slot.Label} {slot.Helper} {slot.DisplayValue}")), StringComparison.Ordinal);
        Assert.All(viewModel.Slots, slot => Assert.Null(slot.Value));
        Assert.Equal(PreviewPng, viewModel.TemplatePreviewPng);
        Assert.Equal("120 × 180 px", viewModel.DocumentDimensions);
        Assert.Equal(120, viewModel.DocumentWidth);
        Assert.Equal(180, viewModel.DocumentHeight);
        Assert.True(viewModel.HasTemplatePreview);
        Assert.True(viewModel.HasValidPhotoSlotSetup);
        Assert.True(viewModel.ShowDetectedAreaList);
        Assert.False(viewModel.ShowPhotoSlotSetupNotice);
        Assert.True(viewModel.HasSelectedSlot);
        Assert.False(viewModel.CanAssignSelectedPhoto);
        Assert.False(viewModel.ShowEmptyInspector);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.Contains("1 slot foto siap", viewModel.TemplateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnvalidatedPhotoCandidateIsHiddenAndKeepsPlacementAndExportDisabled()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = new PsdTemplateInspection(
                true,
                "read-only metadata",
                new[]
                {
                    new PsdSlotDescriptor(
                        "xl",
                        "XL",
                        PsdSlotType.Photo,
                        "XL",
                        Bounds: new PsdSlotBounds(5, 6, 40, 60),
                        IsPlacementReady: true)
                },
                PreviewPng,
                120,
                180)
        };
        var viewModel = new MainViewModel(adapter) { OutputPath = "/tmp/frame-studio-output" };
        viewModel.SetMaster("/samples/master.psd");

        await viewModel.InspectSelectedMasterAsync();
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.jpg", null));

        Assert.Empty(viewModel.Slots);
        Assert.False(viewModel.HasValidPhotoSlotSetup);
        Assert.False(viewModel.ShowDetectedAreaList);
        Assert.True(viewModel.ShowPhotoSlotSetupNotice);
        Assert.Contains("aperture biner", viewModel.PhotoSlotSetupNotice, StringComparison.Ordinal);
        Assert.False(viewModel.CanPlaceSelectedSlot);
        Assert.False(viewModel.CanAssignSelectedPhoto);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(119, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 179)]
    public async Task OutOfCanvasPhotoMaskIsRejectedAtUiBoundary(int x, int y)
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = Inspection(
                new PsdSlotDescriptor(
                    "outside",
                    "Synthetic outside bounds",
                    PsdSlotType.Photo,
                    "Synthetic outside bounds",
                    Bounds: new PsdSlotBounds(x, y, 2, 2),
                    IsPlacementReady: true,
                    ApertureMask: [255, 255, 255, 255]))
        };
        var viewModel = new MainViewModel(adapter) { OutputPath = "/tmp/frame-studio-output" };
        viewModel.SetMaster("/samples/synthetic-master.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.Empty(viewModel.Slots);
        Assert.False(viewModel.HasValidPhotoSlotSetup);
        Assert.False(viewModel.CanPlaceSelectedSlot);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Fact]
    public async Task ExportCanEnableOnlyAfterValidatedPhotoSlotIsPresent()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter
        {
            InspectionResult = Inspection(new PsdSlotDescriptor("photo", "Photo", PsdSlotType.Photo, "Photo"))
        }) { OutputPath = "/tmp/frame-studio-output" };
        viewModel.SetMaster("/samples/master.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.True(viewModel.HasValidPhotoSlotSetup);
        Assert.True(viewModel.CanExportPngCopies);
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
        Assert.Contains("Pemeriksaan PSD gagal", viewModel.Status);
    }

    [Fact]
    public async Task ReaderFailureDoesNotClaimThePsdIsCorrupt()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = new PsdTemplateInspection(
                false,
                "reader limitation",
                Array.Empty<PsdSlotDescriptor>(),
                FailureKind: FramePsdReadFailureKind.ReaderCouldNotInterpretInput)
        };
        var viewModel = new MainViewModel(adapter);
        viewModel.SetMaster("/samples/unsupported.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.Contains("tidak membuktikan file rusak", viewModel.Status);
        Assert.Equal("Pratinjau belum tersedia. Periksa status pembacaan di bawah.", viewModel.TemplatePreviewNotice);
        Assert.False(viewModel.HasTemplatePreview);
    }

    [Fact]
    public async Task OlderInspectionCannotReplaceNewerMasterResult()
    {
        var adapter = new FakePsdTemplateAdapter();
        var pendingOldResult = new TaskCompletionSource<PsdTemplateInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.EnqueueInspection((_, _) => pendingOldResult.Task);
        adapter.EnqueueInspection((_, _) => Task.FromResult(Inspection(new PsdSlotDescriptor("new", "New Photo", PsdSlotType.Photo, "New Photo", Bounds: new PsdSlotBounds(2, 3, 4, 5)))));
        var viewModel = new MainViewModel(adapter);

        viewModel.SetMaster("/samples/old.psd");
        var olderInspection = viewModel.InspectSelectedMasterAsync();
        viewModel.SetMaster("/samples/new.psd");
        await viewModel.InspectSelectedMasterAsync();
        pendingOldResult.SetResult(Inspection(new PsdSlotDescriptor("old", "Old Photo", PsdSlotType.Photo, "Old Photo", Bounds: new PsdSlotBounds(6, 7, 8, 9))));
        await olderInspection;

        Assert.Equal("/samples/new.psd", viewModel.MasterPath);
        Assert.Single(viewModel.Slots);
        Assert.Equal("slot/1", viewModel.Slots[0].Id);
        Assert.Equal(new FrameSlotBounds(2, 3, 4, 5), viewModel.Slots[0].Bounds);
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

        Assert.True(viewModel.CanAssignSelectedPhoto);
        var assigned = viewModel.AssignSelectedPhoto(viewModel.Slots.Single());

        Assert.True(assigned);
        Assert.Same(photo, viewModel.Slots.Single().Photo);
        Assert.Equal("/samples/master.psd", viewModel.MasterPath);
        Assert.Same(previewBefore, viewModel.TemplatePreviewPng);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.Contains("PSD master tidak diubah", viewModel.Status);
    }

    [Fact]
    public async Task OptionalFallbackShowsAnonymousTreeAndBlocksEditsAndPngExport()
    {
        var tree = new FramePsdLayerTreeSummary(
            2, 1, 1, 1, 0, 0,
            new[]
            {
                new FramePsdLayerSummary(
                    "private-id", "PRIVATE GROUP", "Group", true, true, 255, new FrameBounds(0, 0, 100, 100), false, false, false, false,
                    new[]
                    {
                        new FramePsdLayerSummary("private-child", "PRIVATE TEXT VALUE", "Text layer", false, true, 255, new FrameBounds(10, 20, 50, 15), false, true, false, false, Array.Empty<FramePsdLayerSummary>())
                    })
            });
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = new PsdTemplateInspection(
                true,
                "Potentially sensitive reader text is ignored",
                new[] { new PsdSlotDescriptor("private-slot", "PRIVATE NAME", PsdSlotType.Text, "PRIVATE NAME", "PRIVATE VALUE") },
                PreviewPng,
                120,
                180,
                tree,
                ActiveReaderName: "psd-tools desktop sidecar")
        };
        var viewModel = new MainViewModel(adapter) { OutputPath = "/tmp/frame-studio-output" };
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/private-photo.jpg", null));
        viewModel.SetMaster("/samples/master.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.True(viewModel.IsFallbackPreviewMode);
        Assert.False(viewModel.CanEditPhotoTextLogo);
        Assert.False(viewModel.CanAssignSelectedPhoto);
        Assert.Empty(viewModel.Slots);
        Assert.Empty(viewModel.SourcePhotos);
        Assert.True(viewModel.HasTemplatePreview);
        Assert.False(viewModel.CanExportPngCopies);
        Assert.True(viewModel.ShowPhotoSlotSetupNotice);
        Assert.False(viewModel.ShowDetectedAreaList);
        Assert.Contains("nonaktif", viewModel.ExportNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Group 1", viewModel.LayerTreeRows[0].DisplayName);
        Assert.Equal("Text layer 1", viewModel.LayerTreeRows[1].DisplayName);
        Assert.Contains("mask", viewModel.LayerTreeRows[1].Details);
        var uiText = string.Join(" ", viewModel.LayerTreeRows.Select(row => $"{row.DisplayName} {row.Kind} {row.Details}"))
            + " " + viewModel.TemplateStatus + " " + viewModel.Status + " " + viewModel.EngineNotice + " " + viewModel.EngineDetails;
        Assert.DoesNotContain("PRIVATE", uiText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualRectangleUnlocksPngOverlayPlacementInReadOnlyFallbackOnly()
    {
        var adapter = new FakePsdTemplateAdapter
        {
            InspectionResult = new PsdTemplateInspection(
                true,
                "read-only synthetic composite",
                Array.Empty<PsdSlotDescriptor>(),
                PreviewPng,
                120,
                180,
                ActiveReaderName: "psd-tools desktop sidecar")
        };
        var viewModel = new MainViewModel(adapter) { OutputPath = "/tmp/frame-studio-output" };
        viewModel.SetMaster("/samples/disposable-master-copy.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.True(viewModel.IsFallbackPreviewMode);
        Assert.False(viewModel.CanEditPhotoTextLogo);
        Assert.False(viewModel.CanPlaceSelectedSlot);
        Assert.True(viewModel.CanUseManualFrameTool);
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/synthetic-student.png", null));
        viewModel.ToggleManualFrameTool();
        Assert.True(viewModel.IsManualFrameDrawing);

        var frame = viewModel.AddManualPhotoFrame(10, 20, 30, 40);

        Assert.NotNull(frame);
        Assert.True(frame!.IsManualFrame);
        Assert.True(frame.HasTrustedApertureMask);
        Assert.All(frame.ApertureMask.ToArray(), sample => Assert.Equal(255, sample));
        Assert.True(viewModel.CanPlaceSelectedSlot);
        Assert.True(viewModel.CanAssignSelectedPhoto);
        Assert.True(viewModel.AssignSelectedPhoto(frame));
        Assert.True(viewModel.CanExportPngCopies);
        Assert.False(viewModel.CanEditPhotoTextLogo);
        Assert.True(viewModel.IsFallbackPreviewMode);
        Assert.Null(viewModel.AddManualPhotoFrame(115, 175, 10, 10));
        Assert.Equal("/samples/disposable-master-copy.psd", viewModel.MasterPath);
    }

    [Fact]
    public async Task GroupHintsNeverUnlockAutomaticPlacementOrExport()
    {
        var inspection = Inspection() with
        {
            WorkflowHints = new FramePsdWorkflowHints(1, 1, 1, 1, 1, 1)
        };
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { InspectionResult = inspection })
        {
            OutputPath = "/tmp/frame-studio-output"
        };
        viewModel.SetMaster("/samples/disposable-master-copy.psd");

        await viewModel.InspectSelectedMasterAsync();

        Assert.Contains("KOLASE", viewModel.WorkflowStructureNotice, StringComparison.Ordinal);
        Assert.Contains("mask", viewModel.WorkflowStructureNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewModel.Slots);
        Assert.False(viewModel.CanPlaceSelectedSlot);
        Assert.False(viewModel.CanExportPngCopies);
    }

    [Fact]
    public async Task PhotoCannotBeAssignedToTextCandidate()
    {
        var viewModel = new MainViewModel(new FakePsdTemplateAdapter { InspectionResult = Inspection(new PsdSlotDescriptor("name", "Name", PsdSlotType.Text, "Name")) });
        viewModel.SetMaster("/samples/master.psd");
        await viewModel.InspectSelectedMasterAsync();
        viewModel.AddSourcePhoto(new SourcePhoto("/samples/student.jpg", null));

        Assert.False(viewModel.CanAssignSelectedPhoto);
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
    public void RasterComposerDrawsPhotoThroughExactRectangleMaskWithoutChangingPixelsOutsideIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frame-studio-compose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var photoPath = Path.Combine(root, "photo.png");
        try
        {
            var background = CreateSolidPng(10, 10, new SKColor(20, 40, 180));
            File.WriteAllBytes(photoPath, CreateSolidPng(4, 4, new SKColor(230, 40, 30)));
            var slot = new FrameSlot("photo", "Photo", SlotKind.Photo, "synthetic", bounds: new FrameSlotBounds(3, 3, 4, 4), isPlacementReady: true, apertureMask: Enumerable.Repeat((byte)255, 4 * 4).ToArray())
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

    private static PsdTemplateInspection Inspection(params PsdSlotDescriptor[] slots)
    {
        var validatedSlots = slots.Select(slot =>
        {
            if (slot.Type is not (PsdSlotType.Photo or PsdSlotType.Logo))
                return slot;

            var bounds = slot.Bounds ?? new PsdSlotBounds(5, 6, 40, 60);
            var apertureMask = slot.ApertureMask ?? Enumerable.Repeat((byte)255, checked(bounds.Width * bounds.Height)).ToArray();
            return slot with { Bounds = bounds, IsPlacementReady = true, ApertureMask = apertureMask };
        }).ToArray();
        return new PsdTemplateInspection(true, "Read-only metadata with validated synthetic image bounds.", validatedSlots, PreviewPng, 120, 180);
    }

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

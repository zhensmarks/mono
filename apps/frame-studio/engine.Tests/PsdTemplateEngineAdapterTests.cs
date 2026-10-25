using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdTemplateEngineAdapterTests
{
    [Fact]
    public async Task InspectReturnsTextCandidateAndStoredCompositeWithoutExposingLayerName()
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            File.WriteAllBytes(masterPath, SyntheticPsdFixture.CreateRgbWithPhotoLayer("Student Name"));

            var result = await new PsdTemplateEngineAdapter().InspectAsync(masterPath);

            Assert.True(result.Success, result.Message);
            var slot = Assert.Single(result.Slots);
            Assert.Equal(FramePsdSlotType.Text, slot.Type);
            Assert.Equal("Text candidate 1", slot.LayerName);
            Assert.DoesNotContain("Student Name", slot.LayerName, StringComparison.Ordinal);
            Assert.Null(slot.CurrentValue);
            Assert.Equal(new FrameBounds(0, 0, 2, 2), slot.Bounds);
            Assert.False(slot.IsPlacementReady);
            Assert.Equal(2, result.DocumentWidth);
            Assert.Equal(2, result.DocumentHeight);
            Assert.NotNull(result.FlattenedPreviewPng);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, result.FlattenedPreviewPng![..8]);
            Assert.Contains("Read-only stored composite", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Student Name", result.Message, StringComparison.Ordinal);
            Assert.Equal("PsdSharp", result.ActiveReaderName);
            Assert.Equal(1, result.LayerTree!.LayerCount);
            Assert.Equal("Layer 1", Assert.Single(result.LayerTree.Roots).DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Photo XL")]
    [InlineData("School Logo")]
    public async Task ImageNameWithoutMaskOrClippingIsNotExposedAsUsableTarget(string layerName)
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            File.WriteAllBytes(masterPath, SyntheticPsdFixture.CreateRgbWithPhotoLayer(layerName));

            var result = await new PsdTemplateEngineAdapter().InspectAsync(masterPath);

            Assert.True(result.Success, result.Message);
            Assert.Empty(result.Slots);
            Assert.DoesNotContain(layerName, result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClippedPhotoLayerMetadataAloneDoesNotEnablePlacementOrExport()
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            var outputFolder = Path.Combine(root, "output");
            var originalMaster = SyntheticPsdFixture.CreateRgbWithPhotoLayer("Photo XL", clipped: true);
            File.WriteAllBytes(masterPath, originalMaster);

            var adapter = new PsdTemplateEngineAdapter();
            var inspection = await adapter.InspectAsync(masterPath);

            Assert.True(inspection.Success, inspection.Message);
            Assert.DoesNotContain(inspection.Slots, slot => slot.Type is FramePsdSlotType.Photo or FramePsdSlotType.Logo);
            Assert.True(Assert.Single(inspection.LayerTree!.Roots).HasClipping);

            var export = await adapter.ExportCopyAsync(new FramePsdExportRequest(
                masterPath,
                root,
                outputFolder,
                new Dictionary<string, string>()));

            Assert.False(export.Success);
            Assert.Contains("no photo slot", export.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputFolder));
            Assert.Equal(originalMaster, File.ReadAllBytes(masterPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("S")]
    [InlineData("XL")]
    public async Task SizeLabelAloneDoesNotCreatePhotoSlot(string layerName)
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            File.WriteAllBytes(masterPath, SyntheticPsdFixture.CreateRgbWithPhotoLayer(layerName));

            var result = await new PsdTemplateEngineAdapter().InspectAsync(masterPath);

            Assert.True(result.Success, result.Message);
            Assert.Empty(result.Slots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportRemainsDisabledWithoutValidatedPhotoSlot()
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            var outputFolder = Path.Combine(root, "output");
            var originalMaster = SyntheticPsdFixture.CreateRgbWithPhotoLayer();
            File.WriteAllBytes(masterPath, originalMaster);

            var result = await new PsdTemplateEngineAdapter().ExportCopyAsync(new FramePsdExportRequest(
                masterPath,
                root,
                outputFolder,
                new Dictionary<string, string>()));

            Assert.False(result.Success);
            Assert.Contains("no photo slot", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputFolder));
            Assert.Equal(originalMaster, File.ReadAllBytes(masterPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NonEmptyAssignmentsAreRejectedBeforeAnyOutputIsWritten()
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            var outputFolder = Path.Combine(root, "output");
            File.WriteAllBytes(masterPath, SyntheticPsdFixture.CreateRgbWithPhotoLayer());

            var result = await new PsdTemplateEngineAdapter().ExportCopyAsync(new FramePsdExportRequest(
                masterPath,
                root,
                outputFolder,
                new Dictionary<string, string> { ["photo-xl"] = "student.jpg" }));

            Assert.False(result.Success);
            Assert.Contains("assignments are not supported", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputFolder));
            Assert.Empty(Directory.GetFiles(root, "*.png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReaderFailureIsReportedWithoutClaimingTheSourceIsCorrupt()
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "truncated.psd");
            File.WriteAllBytes(masterPath, "8BPS"u8.ToArray());

            var result = await new PsdTemplateEngineAdapter().InspectAsync(masterPath);

            Assert.False(result.Success);
            Assert.NotEqual(FramePsdReadFailureKind.None, result.FailureKind);
            Assert.Contains("does not establish that the source file is corrupt", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frame-studio-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

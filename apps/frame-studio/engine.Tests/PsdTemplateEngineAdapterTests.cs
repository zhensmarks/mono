using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdTemplateEngineAdapterTests
{
    [Theory]
    [InlineData("Photo XL", FramePsdSlotType.Photo)]
    [InlineData("Student Name", FramePsdSlotType.Text)]
    [InlineData("School Logo", FramePsdSlotType.Logo)]
    public async Task InspectReturnsNameBasedCandidateAndStoredComposite(string layerName, FramePsdSlotType expectedType)
    {
        var root = CreateTempDirectory();
        try
        {
            var masterPath = Path.Combine(root, "master.psd");
            File.WriteAllBytes(masterPath, SyntheticPsdFixture.CreateRgbWithPhotoLayer(layerName));

            var result = await new PsdTemplateEngineAdapter().InspectAsync(masterPath);

            Assert.True(result.Success, result.Message);
            var slot = Assert.Single(result.Slots);
            Assert.Equal(expectedType, slot.Type);
            Assert.Equal(layerName, slot.LayerName);
            Assert.Null(slot.CurrentValue);
            Assert.Equal(new FrameBounds(0, 0, 2, 2), slot.Bounds);
            Assert.Equal(2, result.DocumentWidth);
            Assert.Equal(2, result.DocumentHeight);
            Assert.NotNull(result.FlattenedPreviewPng);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, result.FlattenedPreviewPng![..8]);
            Assert.Contains("name-based slot candidate", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyAssignmentsExportSeparateFlattenedPngWithoutChangingMaster()
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

            Assert.True(result.Success, result.Message);
            Assert.Equal(Path.Combine(outputFolder, "master-flattened.png"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, File.ReadAllBytes(result.OutputPath!)[..8]);
            Assert.Equal(originalMaster, File.ReadAllBytes(masterPath));
            Assert.Contains("Slot assignments were not applied", result.Message, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("cannot apply text/photo/logo assignments", result.Message, StringComparison.OrdinalIgnoreCase);
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

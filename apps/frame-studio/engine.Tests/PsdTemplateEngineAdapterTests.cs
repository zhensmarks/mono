using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdTemplateEngineAdapterTests
{
    [Fact]
    public void CapabilitiesDoNotClaimLayerEditingOrPsdWriting()
    {
        var capabilities = new PsdTemplateEngineAdapter().Capabilities;

        Assert.Equal("PsdSharp", capabilities.ReaderName);
        Assert.Equal("1.2.0", capabilities.ReaderVersion);
        Assert.Equal("MIT", capabilities.ReaderLicense);
        Assert.True(capabilities.CanAttemptPsdRead);
        Assert.True(capabilities.CanAttemptPsbRead);
        Assert.True(capabilities.CanInspectLayerTreeBestEffort);
        Assert.False(capabilities.CanEditPhotoLayers);
        Assert.False(capabilities.CanEditTextLayers);
        Assert.False(capabilities.CanReplaceLogoLayers);
        Assert.False(capabilities.CanWriteLayeredPsd);
    }

    [Fact]
    public async Task AssignmentsAreRejectedBeforeCreatingOutputOrReadingMaster()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var outputDirectory = Path.Combine(tempDirectory, "output");
            var request = new FramePsdExportRequest(
                Path.Combine(tempDirectory, "does-not-exist.psd"),
                tempDirectory,
                outputDirectory,
                new Dictionary<string, string> { ["photo/0"] = "student.png" });

            var result = await new PsdTemplateEngineAdapter().ExportCopyAsync(request);

            Assert.False(result.Success);
            Assert.Null(result.OutputPath);
            Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No output was written", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingSourceIsReportedAsMissingNotCorrupt()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var result = await new PsdTemplateEngineAdapter().InspectAsync(Path.Combine(tempDirectory, "missing.psd"));

            Assert.False(result.Success);
            Assert.Equal(FramePsdReadFailureKind.SourceNotFound, result.FailureKind);
            Assert.Empty(result.Slots);
            Assert.NotNull(result.EngineCapabilities);
            Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corrupt", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task UnreadableInputFailureDoesNotClaimTheFileIsCorruptOrWriteOutput()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempDirectory, "unknown.psd");
            var originalBytes = System.Text.Encoding.ASCII.GetBytes("not-a-psd");
            await File.WriteAllBytesAsync(sourcePath, originalBytes);

            var result = await new PsdTemplateEngineAdapter().InspectAsync(sourcePath);

            Assert.False(result.Success);
            Assert.NotEqual(FramePsdReadFailureKind.None, result.FailureKind);
            Assert.Empty(result.Slots);
            Assert.True(
                result.Message.Contains("not establish", StringComparison.OrdinalIgnoreCase)
                || result.Message.Contains("not evidence", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedCompositeReadDoesNotCreateOutputFolderOrChangeSource()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempDirectory, "unknown.psd");
            var outputDirectory = Path.Combine(tempDirectory, "output");
            var originalBytes = System.Text.Encoding.ASCII.GetBytes("not-a-psd");
            await File.WriteAllBytesAsync(sourcePath, originalBytes);
            var request = new FramePsdExportRequest(
                sourcePath,
                tempDirectory,
                outputDirectory,
                new Dictionary<string, string>());

            var result = await new PsdTemplateEngineAdapter().ExportCopyAsync(request);

            Assert.False(result.Success);
            Assert.NotEqual(FramePsdReadFailureKind.None, result.FailureKind);
            Assert.Null(result.OutputPath);
            Assert.False(Directory.Exists(outputDirectory));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frame-studio-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

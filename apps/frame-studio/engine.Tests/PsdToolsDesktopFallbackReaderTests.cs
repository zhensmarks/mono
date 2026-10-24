using System.Security.Cryptography;
using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdToolsDesktopFallbackReaderTests
{
    [Fact]
    public void FallbackCapabilitiesNameTheExplicitDesktopRuntimeAndKeepWritingDisabled()
    {
        var fallback = new PsdToolsDesktopFallbackReader(new PsdToolsDesktopFallbackOptions("python3"));
        var adapter = new PsdTemplateEngineAdapter(new PsdDocumentReader(fallbackReader: fallback));
        var optional = adapter.Capabilities.OptionalFallback;

        Assert.NotNull(optional);
        Assert.Equal("psd-tools desktop sidecar", optional.Name);
        Assert.Equal("MIT", optional.License);
        Assert.True(optional.IsConfigured);
        Assert.False(optional.RuntimeVerified);
        Assert.Contains("Python", optional.RuntimeRequirement, StringComparison.Ordinal);
        Assert.Contains("Desktop only", optional.PlatformScope, StringComparison.Ordinal);
        Assert.True(optional.CanInspectLayerTreeBestEffort);
        Assert.True(optional.CanExportSupportedStoredCompositePng);
        Assert.False(optional.CanWriteLayeredPsd);
        Assert.False(adapter.Capabilities.CanEditPhotoLayers);
        Assert.False(adapter.Capabilities.CanEditTextLayers);
        Assert.False(adapter.Capabilities.CanReplaceLogoLayers);
        Assert.False(adapter.Capabilities.CanWriteLayeredPsd);
    }

    [Fact]
    public void FallbackInputAndAllocationLimitsMustBeFiniteAndSensible()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", maxInputFileBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", maxPreviewBytes: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", maxAllocationBytes: 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", maxAllocationBytes: 2L * 1024 * 1024 * 1024 + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", maxAllocationBytes: 1024L * 1024 * 1024, processMemoryLimitBytes: 512L * 1024 * 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsdToolsDesktopFallbackOptions("python3", processTimeout: TimeSpan.FromMinutes(11)));
    }

    [Fact]
    public void ConfiguredFallbackReadsLocalSampleWithoutChangingItWhenEnvironmentIsProvided()
    {
        var python = Environment.GetEnvironmentVariable("FRAME_STUDIO_PSD_TOOLS_PYTHON");
        var samplePath = Environment.GetEnvironmentVariable("FRAME_STUDIO_PSD_TOOLS_SAMPLE");
        if (string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(samplePath) || !File.Exists(samplePath))
        {
            return;
        }

        using var beforeStream = File.OpenRead(samplePath);
        var beforeHash = SHA256.HashData(beforeStream);
        var fallback = new PsdToolsDesktopFallbackReader(new PsdToolsDesktopFallbackOptions(
            python,
            maxInputFileBytes: 512L * 1024 * 1024,
            maxPreviewBytes: 256L * 1024 * 1024,
            maxAllocationBytes: 1024L * 1024 * 1024,
            processMemoryLimitBytes: 3L * 1024 * 1024 * 1024,
            processTimeout: TimeSpan.FromMinutes(3)));
        var reader = new PsdDocumentReader(fallbackReader: fallback);

        var document = reader.Read(samplePath);

        Assert.Equal(2953, document.Width);
        Assert.Equal(4488, document.Height);
        Assert.Equal("Rgb", document.ColorMode);
        Assert.Equal(30, Flatten(document.Layers).Count());
        Assert.Equal(5, Flatten(document.Layers).Count(layer => layer.Kind == "group"));
        Assert.Equal(3, Flatten(document.Layers).Count(layer => layer.Kind == "type"));
        Assert.Equal(11, Flatten(document.Layers).Count(layer => layer.HasMask));
        Assert.Equal(1, Flatten(document.Layers).Count(layer => layer.HasEffects));
        Assert.NotNull(document.CompositePreview);
        Assert.Equal(53_012_256, document.CompositePreview!.RgbaPixels.Length);
        Assert.False(document.CanSaveLayeredPsd);
        Assert.True(reader.Capabilities.OptionalFallback!.RuntimeVerified);
        Assert.Contains("verified", reader.Capabilities.OptionalFallback.Version, StringComparison.OrdinalIgnoreCase);

        using var afterStream = File.OpenRead(samplePath);
        Assert.Equal(beforeHash, SHA256.HashData(afterStream));
    }

    private static IEnumerable<FramePsdLayer> Flatten(IEnumerable<FramePsdLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }
}

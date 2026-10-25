using System.Security.Cryptography;
using System.Runtime.InteropServices;
using FrameStudio.App.Services;
using FrameStudio.Engine;
using Xunit;

namespace FrameStudio.Tests;

public sealed class PsdTemplateEngineUiAdapterTests
{
    [Fact]
    public async Task ExactApertureMapEnablesMaskAwarePlacementWithoutEnablingLayeredPsdWriting()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FRAME_STUDIO_PSD_TOOLS_MASK_TESTS"), "1", StringComparison.Ordinal))
            return;
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic_masked_photo_slot_no_text.psd");
        using var beforeStream = File.OpenRead(fixture);
        var beforeHash = SHA256.HashData(beforeStream);

        var python = Environment.GetEnvironmentVariable("FRAME_STUDIO_PSD_TOOLS_PYTHON") ?? "python3";
        var fallback = new PsdToolsDesktopFallbackReader(new PsdToolsDesktopFallbackOptions(python));
        var engine = new PsdTemplateEngineAdapter(new PsdDocumentReader(fallbackReader: fallback));
        var adapter = new PsdTemplateEngineUiAdapter(engine);

        var inspection = await adapter.InspectAsync(fixture);

        Assert.True(inspection.Success, inspection.Message);
        var photo = Assert.Single(inspection.Slots, slot => slot.Type == PsdSlotType.Photo);
        Assert.NotNull(photo.ApertureMask);
        Assert.NotNull(photo.Bounds);
        Assert.Equal((long)photo.Bounds!.Width * photo.Bounds.Height, photo.ApertureMask!.LongLength);
        Assert.Contains((byte)255, photo.ApertureMask!);
        Assert.All(photo.ApertureMask!, sample => Assert.True(sample is 0 or 255));
        Assert.True(photo.IsPlacementReady);
        Assert.False(inspection.EngineCapabilities!.CanWriteLayeredPsd);

        using var afterStream = File.OpenRead(fixture);
        Assert.Equal(beforeHash, SHA256.HashData(afterStream));
    }
}

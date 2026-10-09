using System.Text.Json;
using PixelcutCompact.Services;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class ExternalEditorSettingsTests
{
    [Fact]
    public void ExternalEditorPaths_AreStoredIndependentlyAndRoundTrip()
    {
        var settings = new PreviewWindowSettings();
        settings.SetExternalEditorPath(ExternalEditorKind.Photoshop, @"C:\Apps\Photoshop.exe");
        settings.SetExternalEditorPath(ExternalEditorKind.Photocraft, @"D:\Apps\photocraft.exe");
        settings.SetExternalEditorPath(ExternalEditorKind.Custom, @"E:\Apps\other-editor.exe");

        var restored = JsonSerializer.Deserialize<PreviewWindowSettings>(JsonSerializer.Serialize(settings));

        Assert.NotNull(restored);
        Assert.Equal(@"C:\Apps\Photoshop.exe", restored!.GetExternalEditorPath(ExternalEditorKind.Photoshop));
        Assert.Equal(@"D:\Apps\photocraft.exe", restored.GetExternalEditorPath(ExternalEditorKind.Photocraft));
        Assert.Equal(@"E:\Apps\other-editor.exe", restored.GetExternalEditorPath(ExternalEditorKind.Custom));
    }

    [Fact]
    public void LegacyPath_MigratesToCurrentlySelectedEditorWithoutOverwritingPhotoshopPath()
    {
        var settings = new PreviewWindowSettings
        {
            ExternalEditorKind = nameof(ExternalEditorKind.Photocraft),
            ExternalEditorPath = @"D:\Legacy\photocraft.exe",
            PhotoshopPath = @"C:\Apps\Photoshop.exe"
        };

        Assert.True(settings.MigrateLegacyExternalEditorPath());

        Assert.Equal(@"D:\Legacy\photocraft.exe", settings.PhotocraftPath);
        Assert.Equal(@"C:\Apps\Photoshop.exe", settings.PhotoshopPath);
        Assert.Empty(settings.ExternalEditorPath);
    }

    [Fact]
    public void LegacyPhotoshopPath_DoesNotReplaceExistingPhotoshopIntegrationPath()
    {
        var settings = new PreviewWindowSettings
        {
            ExternalEditorKind = nameof(ExternalEditorKind.Photoshop),
            ExternalEditorPath = @"D:\Legacy\Photoshop.exe",
            PhotoshopPath = @"C:\Configured\Photoshop.exe"
        };

        Assert.True(settings.MigrateLegacyExternalEditorPath());

        Assert.Equal(@"C:\Configured\Photoshop.exe", settings.PhotoshopPath);
        Assert.Empty(settings.ExternalEditorPath);
    }
}

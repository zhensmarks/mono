using System.Text.Json;
using PixelcutCompact.Services;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

/// <summary>
/// Menu Window menyimpan visibilitas panel di PreviewWindowSettings.EditorPanelVisibility.
/// Test ini mengunci: (1) pilihan bertahan setelah serialize/deserialize, dan
/// (2) key yang tidak ada berarti "tampil" (default ala Photoshop) — supaya user yang
/// belum pernah toggle tidak tiba-tiba kehilangan panel.
/// </summary>
public sealed class EditorPanelVisibilitySettingsTests
{
    [Fact]
    public void DefaultSettings_HasNoExplicitVisibility_SoEveryPanelIsVisible()
    {
        var s = new PreviewWindowSettings();
        Assert.NotNull(s.EditorPanelVisibility);
        Assert.Empty(s.EditorPanelVisibility);
    }

    [Fact]
    public void HiddenPanel_SurvivesJsonRoundTrip()
    {
        var s = new PreviewWindowSettings();
        s.EditorPanelVisibility["history"] = false;
        s.EditorPanelVisibility["toolrail"] = false;

        var json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<PreviewWindowSettings>(json);

        Assert.NotNull(back);
        Assert.False(back!.EditorPanelVisibility["history"]);
        Assert.False(back.EditorPanelVisibility["toolrail"]);
        // Key yang tidak pernah di-toggle tetap "tampil".
        Assert.False(back.EditorPanelVisibility.ContainsKey("optionsbar"));
    }
}

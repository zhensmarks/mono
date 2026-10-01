using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Input;
using PixelcutCompact.Services;
using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class EditorShortcutMapTests
{
    [Fact]
    public void RemappedSingleKeyAndShiftChordResolveToTheirEditorActions()
    {
        var shortcuts = EditorShortcutMap.CreateDefaults();
        shortcuts[nameof(EditorShortcutAction.Pan)] = "K";
        shortcuts[nameof(EditorShortcutAction.PolygonLasso)] = "Shift+O";

        Assert.True(EditorShortcutMap.TryValidate(shortcuts, out var error));
        Assert.Equal(string.Empty, error);
        Assert.True(EditorShortcutMap.TryGetAction(shortcuts, Key.K, KeyModifiers.None, out var pan));
        Assert.Equal(EditorShortcutAction.Pan, pan);
        Assert.True(EditorShortcutMap.TryGetAction(shortcuts, Key.O, KeyModifiers.Shift, out var polygonLasso));
        Assert.Equal(EditorShortcutAction.PolygonLasso, polygonLasso);
        Assert.False(EditorShortcutMap.TryGetAction(shortcuts, Key.O, KeyModifiers.None, out _));
    }

    [Fact]
    public void DuplicateBindingsAreRejectedWithTheConflictingActionsNamed()
    {
        var shortcuts = EditorShortcutMap.CreateDefaults();
        shortcuts[nameof(EditorShortcutAction.Brush)] = "H";

        Assert.False(EditorShortcutMap.TryValidate(shortcuts, out var error));
        Assert.Contains("Pan tool", error);
        Assert.Contains("Brush", error);
        Assert.Contains("H", error);
    }

    [Fact]
    public void SpaceCannotBeAssignedOrResolvedAsAnEditorAction()
    {
        var shortcuts = EditorShortcutMap.CreateDefaults();
        shortcuts[nameof(EditorShortcutAction.Pan)] = "Space";

        Assert.False(EditorShortcutMap.TryValidate(shortcuts, out var error));
        Assert.Contains("reserved", error);
        Assert.False(EditorShortcutMap.TryGetAction(shortcuts, Key.Space, KeyModifiers.None, out _));
    }

    [Fact]
    public void OldSettingsWithoutAnEditorMapMergeInCurrentDefaults()
    {
        var merged = EditorShortcutMap.MergeWithDefaults(new Dictionary<string, string>
        {
            [nameof(EditorShortcutAction.Brush)] = "C"
        });

        Assert.Equal("C", EditorShortcutMap.GetShortcut(merged, EditorShortcutAction.Brush));
        Assert.Equal("H", EditorShortcutMap.GetShortcut(merged, EditorShortcutAction.Pan));
        Assert.True(EditorShortcutMap.TryValidate(merged, out _));
    }

    [Fact]
    public void EditorShortcutMapRoundTripsThroughExistingPreviewSettingsJson()
    {
        var settings = new PreviewWindowSettings();
        settings.EditorShortcuts[nameof(EditorShortcutAction.Brush)] = "C";
        settings.EditorShortcuts[nameof(EditorShortcutAction.PolygonLasso)] = "Shift+O";

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<PreviewWindowSettings>(json)!;

        Assert.Equal("C", EditorShortcutMap.GetShortcut(loaded.EditorShortcuts, EditorShortcutAction.Brush));
        Assert.Equal("Shift+O", EditorShortcutMap.GetShortcut(loaded.EditorShortcuts, EditorShortcutAction.PolygonLasso));
    }

    [Fact]
    public void ShortcutResetRestoresPreviewAndEditorDefaultsWithoutReplacingOtherSettings()
    {
        var settings = new PreviewWindowSettings
        {
            ShortcutNext = "N",
            EditorBetaMode = true
        };
        settings.EditorShortcuts[nameof(EditorShortcutAction.Brush)] = "C";

        settings.RestoreKeyboardShortcutDefaults();

        Assert.Equal(PreviewWindowSettings.DefaultShortcutNext, settings.ShortcutNext);
        Assert.Equal("B", EditorShortcutMap.GetShortcut(settings.EditorShortcuts, EditorShortcutAction.Brush));
        Assert.True(settings.EditorBetaMode);
    }
}

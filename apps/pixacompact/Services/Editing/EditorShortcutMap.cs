using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace PixelcutCompact.Services.Editing;

public enum EditorShortcutAction
{
    Pan,
    Move,
    Lasso,
    PolygonLasso,
    MagicWand,
    Pen,
    Brush,
    Eraser,
    RefineEdge,
    RectMarquee,
    EllipseMarquee,
    BrushSizeDown,
    BrushSizeUp,
    ToggleBrushMode,
    QuickMask
}

public sealed record EditorShortcutDefinition(
    EditorShortcutAction Action,
    string Label,
    string ControlName,
    string DefaultShortcut);

/// <summary>
/// The persisted edit-mode shortcut catalog. Space is deliberately excluded because
/// it is a temporary Pan override, not a selectable or remappable tool binding.
/// </summary>
public static class EditorShortcutMap
{
    private static readonly EditorShortcutDefinition[] DefinitionList =
    [
        new(EditorShortcutAction.Pan, "Pan tool", "TxtEditPanShortcut", "H"),
        new(EditorShortcutAction.Move, "Move tool", "TxtEditMoveShortcut", "V"),
        new(EditorShortcutAction.Lasso, "Lasso", "TxtEditLassoShortcut", "L"),
        new(EditorShortcutAction.PolygonLasso, "Polygon lasso", "TxtEditPolyLassoShortcut", "Shift+L"),
        new(EditorShortcutAction.MagicWand, "Magic wand", "TxtEditWandShortcut", "W"),
        new(EditorShortcutAction.Pen, "Pen", "TxtEditPenShortcut", "P"),
        new(EditorShortcutAction.Brush, "Brush", "TxtEditBrushShortcut", "B"),
        new(EditorShortcutAction.Eraser, "Eraser", "TxtEditEraserShortcut", "E"),
        new(EditorShortcutAction.RefineEdge, "Refine edge", "TxtEditRefineEdgeShortcut", "Shift+R"),
        new(EditorShortcutAction.RectMarquee, "Rectangular marquee", "TxtEditRectMarqueeShortcut", "M"),
        new(EditorShortcutAction.EllipseMarquee, "Elliptical marquee", "TxtEditEllipseMarqueeShortcut", "Shift+M"),
        new(EditorShortcutAction.BrushSizeDown, "Brush size down", "TxtEditBrushSizeDownShortcut", "["),
        new(EditorShortcutAction.BrushSizeUp, "Brush size up", "TxtEditBrushSizeUpShortcut", "]"),
        new(EditorShortcutAction.ToggleBrushMode, "Toggle erase / restore", "TxtEditBrushModeShortcut", "X"),
        new(EditorShortcutAction.QuickMask, "Quick mask", "TxtEditQuickMaskShortcut", "Q")
    ];

    private static readonly IReadOnlyDictionary<EditorShortcutAction, EditorShortcutDefinition> ByAction =
        DefinitionList.ToDictionary(definition => definition.Action);

    public static IReadOnlyList<EditorShortcutDefinition> Definitions => DefinitionList;

    public static Dictionary<string, string> CreateDefaults() => DefinitionList.ToDictionary(
        definition => definition.Action.ToString(),
        definition => definition.DefaultShortcut,
        StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> MergeWithDefaults(IReadOnlyDictionary<string, string>? shortcuts)
    {
        var merged = CreateDefaults();
        if (shortcuts == null) return merged;

        foreach (var definition in DefinitionList)
        {
            var key = definition.Action.ToString();
            if (TryGetValueIgnoreCase(shortcuts, key, out var value) && !string.IsNullOrWhiteSpace(value))
                merged[key] = value.Trim();
        }

        return merged;
    }

    public static string GetShortcut(
        IReadOnlyDictionary<string, string>? shortcuts,
        EditorShortcutAction action)
    {
        if (ByAction.TryGetValue(action, out var definition)
            && TryGetValueIgnoreCase(shortcuts, action.ToString(), out var value)
            && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        return ByAction.TryGetValue(action, out definition) ? definition.DefaultShortcut : string.Empty;
    }

    public static bool TryValidate(IReadOnlyDictionary<string, string>? shortcuts, out string error)
    {
        var normalized = MergeWithDefaults(shortcuts);
        var used = new Dictionary<(Key Key, KeyModifiers Modifiers), EditorShortcutDefinition>();

        foreach (var definition in DefinitionList)
        {
            var text = normalized[definition.Action.ToString()];
            if (!TryParse(text, out var key, out var modifiers))
            {
                error = $"{definition.Label}: use one key, optionally preceded by Shift.";
                return false;
            }

            if (IsReservedKey(key))
            {
                error = key == Key.Space
                    ? "Space is reserved for temporary Pan."
                    : $"{FormatKey(key)} is reserved by an editor command.";
                return false;
            }

            if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            {
                error = $"{definition.Label}: only an unmodified key or Shift+key is supported.";
                return false;
            }

            var chord = (key, modifiers);
            if (used.TryGetValue(chord, out var other))
            {
                error = $"{definition.Label} conflicts with {other.Label} on {FormatShortcut(key, modifiers)}.";
                return false;
            }

            used[chord] = definition;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryGetAction(
        IReadOnlyDictionary<string, string>? shortcuts,
        Key key,
        KeyModifiers modifiers,
        out EditorShortcutAction action)
    {
        action = default;
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift)
            || IsModifierKey(key) || IsReservedKey(key))
            return false;

        foreach (var definition in DefinitionList)
        {
            if (TryParse(GetShortcut(shortcuts, definition.Action), out var boundKey, out var boundModifiers)
                && boundKey == key && boundModifiers == modifiers)
            {
                action = definition.Action;
                return true;
            }
        }

        return false;
    }

    public static bool TryParse(string? shortcut, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 2) return false;
        if (parts.Length == 2)
        {
            if (!parts[0].Equals("Shift", StringComparison.OrdinalIgnoreCase)) return false;
            modifiers = KeyModifiers.Shift;
        }

        var keyText = parts[^1];
        if (keyText == "[") key = Key.OemOpenBrackets;
        else if (keyText == "]") key = Key.OemCloseBrackets;
        else if (!Enum.TryParse(keyText, ignoreCase: true, out key) || !Enum.IsDefined(key))
            return false;

        return key != Key.None && !IsModifierKey(key);
    }

    public static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        var prefix = modifiers == KeyModifiers.Shift ? "Shift+" : string.Empty;
        return prefix + FormatKey(key);
    }

    public static string FormatKey(Key key) => key switch
    {
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        _ => key.ToString()
    };

    public static bool IsReservedKey(Key key) => key is
        Key.Space or Key.Escape or Key.Enter or Key.Back or Key.Delete;

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private static bool TryGetValueIgnoreCase(
        IReadOnlyDictionary<string, string>? source,
        string key,
        out string value)
    {
        if (source != null)
        {
            foreach (var pair in source)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }
}

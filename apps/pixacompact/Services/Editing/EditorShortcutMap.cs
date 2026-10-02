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
    QuickMask,
    MaskDelete,
    MaskRestore,
    MakeSelection,
    MaskView
}

public sealed record EditorShortcutDefinition(
    EditorShortcutAction Action,
    string Label,
    string ControlName,
    string DefaultShortcut);

/// <summary>
/// The persisted edit-mode shortcut catalog. Space is deliberately excluded because
/// it is a temporary Pan override, not a selectable or remappable tool binding.
/// Supported chord format: one key, optionally preceded by Shift, Ctrl, or Ctrl+Shift
/// (e.g. "Delete", "Shift+Delete", "Ctrl+Enter").
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
        new(EditorShortcutAction.QuickMask, "Quick mask", "TxtEditQuickMaskShortcut", "Q"),
        new(EditorShortcutAction.MaskDelete, "Delete masking", "TxtEditMaskDeleteShortcut", "Delete"),
        new(EditorShortcutAction.MaskRestore, "Restore masking", "TxtEditMaskRestoreShortcut", "Shift+Delete"),
        new(EditorShortcutAction.MakeSelection, "Make selection", "TxtEditMakeSelectionShortcut", "Ctrl+Enter"),
        new(EditorShortcutAction.MaskView, "Mask view (black & white)", "TxtEditMaskViewShortcut", "\\")
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

    /// <param name="t">Opsional: pemeta kunci-resource -&gt; teks lokal (mis. key =&gt; T(key)).
    /// Bila null, pesan error dalam bahasa Inggris.</param>
    public static bool TryValidate(IReadOnlyDictionary<string, string>? shortcuts, out string error, Func<string, string>? t = null)
    {
        var normalized = MergeWithDefaults(shortcuts);
        var used = new Dictionary<(Key Key, KeyModifiers Modifiers), EditorShortcutDefinition>();
        string LabelOf(EditorShortcutDefinition d) => t?.Invoke("Sc_" + d.Action) ?? d.Label;
        string Tmpl(string key, string fallback) => t?.Invoke(key) ?? fallback;

        foreach (var definition in DefinitionList)
        {
            var text = normalized[definition.Action.ToString()];
            if (!TryParse(text, out var key, out var modifiers))
            {
                error = string.Format(Tmpl("Err_KeyFormat", "{0}: use one key, optionally with Shift/Ctrl modifier."), LabelOf(definition));
                return false;
            }

            if (IsReservedKeyForAction(definition.Action, key))
            {
                error = key == Key.Space
                    ? Tmpl("Err_SpaceReserved", "Space is reserved for temporary Pan.")
                    : string.Format(Tmpl("Err_KeyReserved", "{0} is reserved by an editor command."), FormatKey(key));
                return false;
            }

            if (!IsSupportedModifiers(modifiers))
            {
                error = string.Format(Tmpl("Err_Modifiers", "{0}: only an unmodified key, or Shift/Ctrl/Ctrl+Shift+key is supported."), LabelOf(definition));
                return false;
            }

            var chord = (key, modifiers);
            if (IsHardcodedChord(key, modifiers))
            {
                error = string.Format(Tmpl("Err_Hardcoded", "{0}: {1} is used by a built-in editor command (e.g. Ctrl+S save)."), LabelOf(definition), FormatShortcut(key, modifiers));
                return false;
            }
            if (used.TryGetValue(chord, out var other))
            {
                error = string.Format(Tmpl("Err_Conflict", "{0} conflicts with {1} on {2}."), LabelOf(definition), LabelOf(other), FormatShortcut(key, modifiers));
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
        if (!IsSupportedModifiers(modifiers) || IsModifierKey(key))
            return false;

        foreach (var definition in DefinitionList)
        {
            if (TryParse(GetShortcut(shortcuts, definition.Action), out var boundKey, out var boundModifiers)
                && KeysEquivalent(boundKey, key) && boundModifiers == modifiers
                && !IsReservedKeyForAction(definition.Action, key))
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
        if (parts.Length == 0 || parts.Length > 3) return false;
        if (parts.Length >= 2)
        {
            var mods = parts[..^1];
            foreach (var mod in mods)
            {
                if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= KeyModifiers.Shift;
                else if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || mod.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= KeyModifiers.Control;
                else
                    return false;
            }
            // Hanya kombinasi yang didukung; Alt/Win tidak dipakai untuk shortcut editor.
            if (!IsSupportedModifiers(modifiers)) return false;
        }

        var keyText = parts[^1];
        if (keyText == "[") key = Key.OemOpenBrackets;
        else if (keyText == "]") key = Key.OemCloseBrackets;
        else if (keyText == "\\") key = Key.OemBackslash;
        // Digit "0".."9" dipetakan ke Key.D0..D9 (untuk pesan konflik yang jelas;
        // chord digit sendiri hardcoded untuk opacity brush ala Photoshop).
        else if (keyText.Length == 1 && keyText[0] is >= '0' and <= '9')
        {
            var digitKey = (Key)((int)Key.D0 + (keyText[0] - '0'));
            if (!Enum.IsDefined(digitKey)) return false;
            key = digitKey;
        }
        else if (!Enum.TryParse(keyText, ignoreCase: true, out key) || !Enum.IsDefined(key))
            return false;

        return key != Key.None && !IsModifierKey(key);
    }

    public static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        // Urutan ala Photoshop: Ctrl+ lalu Shift+. (Jangan pakai or-pattern di sini;
        // `A | B` pada pattern berarti "A atau B", bukan kombinasi flag.)
        string prefix = string.Empty;
        if (modifiers.HasFlag(KeyModifiers.Control)) prefix += "Ctrl+";
        if (modifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
        return prefix + FormatKey(key);
    }

    public static string FormatKey(Key key) => key switch
    {
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        // X11 melaporkan tombol fisik `\` sebagai OemPipe, Win32 sebagai OemBackslash;
        // keduanya ditampilkan dan diperlakukan sama.
        Key.OemBackslash or Key.OemPipe => "\\",
        // Avalonia menamai tombol Enter sebagai Key.Return; tampilkan "Enter".
        Key.Return => "Enter",
        _ => key.ToString()
    };

    /// <summary>
    /// Dua nama Key untuk tombol fisik yang sama di platform berbeda dianggap setara.
    /// (X11: OemPipe; Win32: OemBackslash untuk tombol `\`.)
    /// </summary>
    public static bool KeysEquivalent(Key a, Key b) => a == b
        || (a is Key.OemBackslash or Key.OemPipe) && (b is Key.OemBackslash or Key.OemPipe);

    public static bool IsReservedKey(Key key) => key is
        Key.Space or Key.Escape or Key.Enter or Key.Back or Key.Delete;

    /// <summary>
    /// Varian context-aware dari <see cref="IsReservedKey"/>: Delete/Backspace boleh
    /// di-bind ke aksi masking (ala Photoshop), tetapi tetap reserved untuk aksi lain
    /// karena dipakai menghapus titik terakhir lasso/pen yang sedang digambar.
    /// Enter hanya boleh di-bind ke aksi Make Selection (ala Photoshop: Ctrl+Enter).
    /// </summary>
    public static bool IsReservedKeyForAction(EditorShortcutAction action, Key key)
    {
        if (key is Key.Space or Key.Escape) return true;
        if (key == Key.Enter) return action != EditorShortcutAction.MakeSelection;
        if (key is Key.Back or Key.Delete)
            return action is not (EditorShortcutAction.MaskDelete or EditorShortcutAction.MaskRestore);
        return false;
    }

    /// <summary>Modifier yang didukung untuk shortcut editor yang bisa di-remap.</summary>
    public static bool IsSupportedModifiers(KeyModifiers modifiers) => modifiers is
        KeyModifiers.None or KeyModifiers.Shift or KeyModifiers.Control
        or (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>
    /// Chord yang dipakai command bawaan editor (hardcoded di HandleEditorKey) dan
    /// tidak boleh di-bind ulang: Ctrl+Z/Y/S/J/A/D, Ctrl+Shift+Z/J/I/Enter, F12 (Revert),
    /// Shift+[/] (hardness), 1..0 (opacity), arrows (nudge), Tab (panel), Ctrl++/-/0/1 (zoom).
    /// </summary>
    private static readonly HashSet<(Key Key, KeyModifiers Modifiers)> HardcodedChords = new()
    {
        (Key.Z, KeyModifiers.Control),
        (Key.Y, KeyModifiers.Control),
        (Key.Z, KeyModifiers.Control | KeyModifiers.Shift),
        (Key.S, KeyModifiers.Control),
        (Key.J, KeyModifiers.Control),
        (Key.J, KeyModifiers.Control | KeyModifiers.Shift),
        (Key.I, KeyModifiers.Control | KeyModifiers.Shift),
        (Key.Enter, KeyModifiers.Control | KeyModifiers.Shift),
        (Key.A, KeyModifiers.Control),
        (Key.D, KeyModifiers.Control),
        (Key.F12, KeyModifiers.None),
        (Key.OemOpenBrackets, KeyModifiers.Shift),
        (Key.OemCloseBrackets, KeyModifiers.Shift),
        (Key.D1, KeyModifiers.None),
        (Key.D2, KeyModifiers.None),
        (Key.D3, KeyModifiers.None),
        (Key.D4, KeyModifiers.None),
        (Key.D5, KeyModifiers.None),
        (Key.D6, KeyModifiers.None),
        (Key.D7, KeyModifiers.None),
        (Key.D8, KeyModifiers.None),
        (Key.D9, KeyModifiers.None),
        (Key.D0, KeyModifiers.None),
        (Key.NumPad1, KeyModifiers.None),
        (Key.NumPad2, KeyModifiers.None),
        (Key.NumPad3, KeyModifiers.None),
        (Key.NumPad4, KeyModifiers.None),
        (Key.NumPad5, KeyModifiers.None),
        (Key.NumPad6, KeyModifiers.None),
        (Key.NumPad7, KeyModifiers.None),
        (Key.NumPad8, KeyModifiers.None),
        (Key.NumPad9, KeyModifiers.None),
        (Key.NumPad0, KeyModifiers.None),
        (Key.Up, KeyModifiers.None),
        (Key.Down, KeyModifiers.None),
        (Key.Left, KeyModifiers.None),
        (Key.Right, KeyModifiers.None),
        (Key.Up, KeyModifiers.Shift),
        (Key.Down, KeyModifiers.Shift),
        (Key.Left, KeyModifiers.Shift),
        (Key.Right, KeyModifiers.Shift),
        (Key.Tab, KeyModifiers.None),
        (Key.OemPlus, KeyModifiers.Control),
        (Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift),
        (Key.Add, KeyModifiers.Control),
        (Key.OemMinus, KeyModifiers.Control),
        (Key.Subtract, KeyModifiers.Control),
        (Key.D0, KeyModifiers.Control),
        (Key.NumPad0, KeyModifiers.Control),
        (Key.D1, KeyModifiers.Control),
        (Key.NumPad1, KeyModifiers.Control),
    };

    public static bool IsHardcodedChord(Key key, KeyModifiers modifiers) =>
        HardcodedChords.Contains((key, modifiers));

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

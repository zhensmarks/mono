using System;
using System.Collections.Generic;

namespace BMachine.UI.Services;

/// <summary>
/// Decides whether a window/view level shortcut must be suppressed because focus is inside a
/// TextBox. Previously ANY focused TextBox disabled EVERY explorer shortcut — so refresh (F5),
/// navigation and the address bar died whenever the rename box was open. Only the gestures that
/// genuinely belong to text editing are blocked now.
/// </summary>
public static class ExplorerShortcutGate
{
    /// <summary>Gestures a TextBox must own while it has focus (clipboard/text editing).</summary>
    private static readonly HashSet<string> TextEditingGestures = new(StringComparer.Ordinal)
    {
        "ctrl+c", "ctrl+x", "ctrl+v", "ctrl+a", "ctrl+z", "ctrl+y",
        "delete", "shift+delete", "back",
    };

    /// <summary>True when the gesture should be swallowed so the focused TextBox can handle it.</summary>
    public static bool ShouldBlock(string? gesture, bool textBoxFocused)
        => textBoxFocused && IsTextEditingGesture(gesture);

    public static bool IsTextEditingGesture(string? gesture)
        => !string.IsNullOrWhiteSpace(gesture) && TextEditingGestures.Contains(Normalize(gesture!));

    /// <summary>Lower-cases and sorts modifiers so "Shift+Ctrl+N" and "Ctrl+Shift+N" match.</summary>
    private static string Normalize(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return parts[0].ToLowerInvariant();

        var key = parts[^1].ToLowerInvariant();
        var modifiers = new List<string>();
        for (int i = 0; i < parts.Length - 1; i++) modifiers.Add(parts[i].ToLowerInvariant());
        modifiers.Sort(StringComparer.Ordinal);
        return string.Join("+", modifiers) + "+" + key;
    }
}

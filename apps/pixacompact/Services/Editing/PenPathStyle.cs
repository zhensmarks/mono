using System;
using Avalonia.Media;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Styling garis path Pen Tool: preset warna, clamp ketebalan, dan normalisasi hex.
/// Satu sumber kebenaran agar UI, render overlay, dan logic test konsisten.
/// </summary>
public static class PenPathStyle
{
    /// <summary>8 preset warna garis path ala Photoshop.</summary>
    public static readonly string[] PresetColors =
    {
        "#FFFFFF", // putih (default)
        "#000000", // hitam
        "#FF3B30", // merah
        "#34C759", // hijau
        "#0A84FF", // biru
        "#FFD60A", // kuning
        "#FF375F", // magenta
        "#32ADE6", // cyan
    };

    /// <summary>Batasi ketebalan ke rentang 1..8 px.</summary>
    public static double ClampThickness(double value) =>
        Math.Clamp(value, EditorSettings.MinPenPathThickness, EditorSettings.MaxPenPathThickness);

    /// <summary>
    /// Normalisasi string warna ke format #RRGGBB (uppercase).
    /// Input tidak valid/null/kosong → warna default.
    /// </summary>
    public static string NormalizeColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var c = Color.Parse(hex.Trim());
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch { /* jatuh ke default */ }
        }
        return EditorSettings.DefaultPenPathColor;
    }

    /// <summary>Parse warna path; tidak pernah throw (fallback ke default).</summary>
    public static Color ParseColor(string? hex)
    {
        try { return Color.Parse(NormalizeColor(hex)); }
        catch { return Color.Parse(EditorSettings.DefaultPenPathColor); }
    }
}

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

/// <summary>Palette overrides never overwrite saved Classic customizations or semantic status colors.</summary>
public static class AppearancePaletteService
{
    public const string SettingKey = "Appearance.Palette";
    private sealed class State
    {
        public AppearancePalette Palette = AppearancePalette.All[0];
        public Dictionary<string, (bool Existed, object? Value)> Originals = new();
    }
    private static readonly ConditionalWeakTable<Application, State> States = new();

    private static State GetState(Application app) => States.GetValue(app, application =>
    {
        application.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(ApplyCurrent);
        return new State();
    });

    public static AppearancePalette Current => Application.Current is { } app
        ? GetState(app).Palette : AppearancePalette.All[0];

    public static void Select(string? id)
    {
        if (Application.Current is not { } app) return;
        RemoveOverrides();
        GetState(app).Palette = AppearancePalette.Find(id);
        ApplyCurrent();
    }

    // ThemeService calls these around legacy custom-color updates so Classic stays intact.
    public static void RemoveOverrides()
    {
        if (Application.Current is not { } app) return;
        var state = GetState(app);
        foreach (var (key, original) in state.Originals)
        {
            if (original.Existed) app.Resources[key] = original.Value!;
            else app.Resources.Remove(key);
        }
        state.Originals.Clear();
    }

    public static void ApplyCurrent()
    {
        if (Application.Current is not { } app) return;
        RemoveOverrides();
        var state = GetState(app);
        var palette = state.Palette;
        if (palette.Id == "classic") return;
        var light = app.ActualThemeVariant == ThemeVariant.Light;
        var accent = Color.Parse(palette.Primary);
        var secondary = Color.Parse(palette.Secondary);
        var dark = Color.Parse(palette.Background);
        var background = light ? Mix(Colors.White, accent, .035) : dark;
        var surface = light ? Colors.White : Mix(dark, Colors.White, .045);
        var elevated = light ? Mix(Colors.White, accent, .025) : Mix(dark, Colors.White, .075);
        var text = light ? Color.Parse("#171717") : Color.Parse("#E8E8E8");
        var muted = Mix(background, text, light ? .70 : .65);
                // Garis separator/border dibuat tipis dekat kontras tema base (#E5E7EB light / #26282C dark).
                // Mix .20/.22 membuat garis terlalu terang dan menonjol (terukur 1.78:1 di Gwen Pink vs 1.16:1 tema base).
                var border = Mix(background, text, light ? .10 : .08);
        var hover = Mix(surface, accent, light ? .09 : .12);
        // In light mode a pale accent remains visible as a swatch/button; text and focus use a darker ink.
        var ink = accent;
        for (var step = 1; Contrast(ink, background) < 4.5 && step <= 20; step++)
            ink = Mix(accent, text, step / 20d);

        void Set(string key, object value)
        {
            state.Originals[key] = (app.Resources.ContainsKey(key), app.Resources.ContainsKey(key) ? app.Resources[key] : null);
            app.Resources[key] = value;
        }
        void Brushes(Color color, params string[] keys)
        {
            foreach (var key in keys) Set(key, new SolidColorBrush(color));
        }
        Set("AppBackground", background);
        Set("CardBackground", surface);
        Set("TextPrimary", text);
        Set("TextSecondary", muted);
        Set("AccentBlue", ink);
        Set("AccentColor", ink);
        Set("AccentOrange", secondary);
        Set("PopupSurfaceBorder", border);
        Brushes(background, "AppBackgroundBrush", "BackgroundDarkBrush", "TerminalBackgroundBrush", "BgInputBrush", "ComboBoxPopupBackground", "AutoCompleteBoxPopupBackground");
        Brushes(surface, "CardBackgroundBrush", "MenuSelectedBrush", "ButtonSecondaryBackgroundBrush");
        Brushes(elevated, "BackgroundBrush", "SurfaceElevatedBrush", "BgInputHoverBrush");
        Brushes(text, "TextPrimaryBrush", "TerminalTextBrush", "ButtonSecondaryForegroundBrush");
        Brushes(muted, "TextSecondaryBrush", "TextMutedBrush", "TextFaintBrush");
        Brushes(border, "CardBorderBrush", "SeparatorBrush", "BorderDefaultBrush", "BorderSubtleBrush", "ButtonSecondaryBorderBrush", "ComboBoxPopupBorderBrush", "AutoCompleteBoxPopupBorderBrush", "NavigationDividerBrush", "PanelDividerBrush");
        Brushes(Mix(border, text, .25), "BorderHoverBrush");
        Brushes(ink, "AccentBlueBrush", "AccentColorBrush", "PrimaryBrush", "BorderFocusBrush");
        Brushes(secondary, "AccentOrangeBrush");
        Brushes(Mix(background, ink, .17), "AccentSubtleBrush", "AccentLowOpacityBrush", "BgActiveBrush");
        Brushes(Mix(background, ink, .26), "AccentSubtleHoverBrush");
        Brushes(hover, "BgHoverBrush", "MenuHoverBrush", "ButtonSecondaryHoverBrush");
        Brushes(Mix(surface, accent, .20), "ButtonSecondaryPressedBrush");
        Brushes(accent, "ButtonPrimaryBackgroundBrush");
        Brushes(Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White, "ButtonPrimaryForegroundBrush", "TextOnPrimaryBrush");
        Brushes(Mix(accent, text, .10), "ButtonPrimaryHoverBrush");
        Brushes(Mix(accent, background, .15), "ButtonPrimaryPressedBrush");
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    public static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte n) { var s = n / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

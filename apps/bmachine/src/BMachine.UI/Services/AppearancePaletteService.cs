using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

/// <summary>
/// Layering resource: Base (theme axaml) &lt; Layer (palette template) &lt; Manual (warna custom user).
/// Manual TIDAK pernah dihapus oleh Select/ApplyCurrent/theme-switch — hanya oleh ClearManual()
/// (tombol "Reset ke bawaan template").
/// </summary>
public static class AppearancePaletteService
{
    public const string SettingKey = "Appearance.Palette";
    private sealed class State
    {
        public AppearancePalette Palette = AppearancePalette.All[0];
        // Nilai Base (theme) per key yang Layer tulis — untuk menghapus layer.
        public Dictionary<string, (bool Existed, object? Value)> Base = new();
        // Manual override user + nilai layer saat di-capture (untuk restore saat layer recompute).
        public Dictionary<string, object> Manual = new();
        public Dictionary<string, object> ManualLayerValue = new();
    }
    private static readonly ConditionalWeakTable<Application, State> States = new();

    private static State GetState(Application app) => States.GetValue(app, application =>
    {
        application.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(ApplyCurrent);
        return new State();
    });

    public static AppearancePalette Current => Application.Current is { } app
        ? GetState(app).Palette : AppearancePalette.All[0];

    /// <summary>Ganti palette template; manual override user tetap bertahan di atasnya.</summary>
    public static void Select(string? id)
    {
        if (Application.Current is not { } app) return;
        var state = GetState(app);
        state.Palette = AppearancePalette.Find(id);
        ApplyLayer(app, state);
    }

    /// <summary>Recompute layer (theme variant light/dark switch); manual tetap di atas.</summary>
    public static void ApplyCurrent()
    {
        if (Application.Current is not { } app) return;
        ApplyLayer(app, GetState(app));
    }

    /// <summary>Catat manual override (widget color/accent/border/background) di atas layer.</summary>
    public static void SetManual(string key, object value)
    {
        if (Application.Current is not { } app) return;
        SetManual(app, GetState(app), key, value);
    }

    /// <summary>Update nilai manual override HANYA jika sudah pernah di-set (pindah variant light/dark).</summary>
    public static void UpdateManual(string key, object value)
    {
        if (Application.Current is not { } app) return;
        var state = GetState(app);
        if (!state.Manual.ContainsKey(key)) return;
        state.Manual[key] = value;
        app.Resources[key] = value;
    }

    /// <summary>Hapus manual override (reset ke bawaan template): resource kembali ke nilai layer.</summary>
    public static void ClearManual()
    {
        if (Application.Current is not { } app) return;
        var state = GetState(app);
        if (state.Manual.Count == 0) return;
        foreach (var key in state.Manual.Keys)
            Restore(app, key, state.ManualLayerValue.TryGetValue(key, out var v) ? v : null);
        state.Manual.Clear();
        state.ManualLayerValue.Clear();
    }

    private static void SetManual(Application app, State state, string key, object value)
    {
        if (!state.Manual.ContainsKey(key))
            state.ManualLayerValue[key] = Capture(app, key);
        state.Manual[key] = value;
        app.Resources[key] = value;
    }

    /// <summary>Rebuild layer dari Base, terapkan palette aktif, re-apply manual di atasnya.</summary>
    private static void ApplyLayer(Application app, State state)
    {
        // 1. Simpan nilai manual, kembalikan resource ke nilai layer (bukan Base)
        var manual = new Dictionary<string, object>(state.Manual);
        if (state.Manual.Count > 0)
        {
            foreach (var key in state.Manual.Keys)
                Restore(app, key, state.ManualLayerValue.TryGetValue(key, out var v) ? v : null);
            state.Manual.Clear();
            state.ManualLayerValue.Clear();
        }
        // 2. Hapus layer lama (kembalikan Base theme values)
        foreach (var (key, original) in state.Base)
        {
            if (original.Existed) app.Resources[key] = original.Value!;
            else app.Resources.Remove(key);
        }
        state.Base.Clear();
        // 3. Terapkan layer palette (classic = tanpa layer)
        if (state.Palette.Id != "classic")
            WriteLayer(app, state, state.Palette);
        // 4. Re-apply manual (capture nilai layer baru sebagai ManualLayerValue)
        foreach (var (key, value) in manual)
            SetManual(app, state, key, value);
    }

    private static void WriteLayer(Application app, State state, AppearancePalette palette)
    {
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
            state.Base[key] = (app.Resources.ContainsKey(key), Capture(app, key));
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
        Brushes(surface, "CardBackgroundBrush", "MenuSelectedBrush");
        // Tombol sekunder HARUS terbaca sebagai kotak terisi, bukan cuma garis border.
        // Mengikatnya ke `surface` membuat isinya identik dengan latar (terukur 255,255,255 di tema
        // terang) sehingga tombol tampak transparan dan hanya border 1 px yang terlihat — user
        // melaporkannya sebagai "background transparan jadi kelihatan ada garisnya".
        // Nilainya sedikit digeser dari permukaan: lebih gelap di tema terang, lebih terang di gelap.
        Brushes(light ? Mix(surface, text, .05) : Mix(surface, Colors.White, .06), "ButtonSecondaryBackgroundBrush");
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

    private static object? Capture(Application app, string key)
        => app.Resources.TryGetValue(key, out var value) ? value : null;

    private static void Restore(Application app, string key, object? value)
    {
        if (value is null) app.Resources.Remove(key);
        else app.Resources[key] = value;
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

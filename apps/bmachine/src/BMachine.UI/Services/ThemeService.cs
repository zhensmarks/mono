using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using BMachine.SDK;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;

namespace BMachine.UI.Services;

public class ThemeService : IThemeService, IRecipient<ThemeSettingsChangedMessage>
{
    private readonly IDatabase _database;
    private string _lightBgColor = "#F5F5F5";
    private string _darkBgColor = "#1C1C1C";

    public ThemeService(IDatabase database)
    {
        _database = database;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public async Task InitializeAsync()
    {
        // Load Appearance Background Colors
        var darkBg = await _database.GetAsync<string>("Appearance.Background.Dark");
        var hasDarkBg = !string.IsNullOrEmpty(darkBg);
        if (hasDarkBg) _darkBgColor = darkBg;

        var lightBg = await _database.GetAsync<string>("Appearance.Background.Light");
        var hasLightBg = !string.IsNullOrEmpty(lightBg);
        if (hasLightBg) _lightBgColor = lightBg;

        // Load settings from DB
        var themeStr = await _database.GetAsync<string>("Settings.Theme") ?? "Dark";
        var accentStr = await _database.GetAsync<string>("Settings.Accent") ?? "#3b82f6"; // Default Blue
        var fontStr = await _database.GetAsync<string>("Settings.Font") ?? "Inter";
        
        // Load Border Colors
                // Normalisasi: nilai border tersimpan pernah ber-alpha 00 (AARRGGBB) -> transparan,
                // sehingga di tema Classic garis separator/border TIDAK terlihat sama sekali.
                // Alpha 00 dipaksa FF (border wajib opaque); nilai lama ditulis ulang biar membaik sendiri.
        var savedBorderLight = await _database.GetAsync<string>("Settings.BorderLight");
        var hasBorderLight = !string.IsNullOrEmpty(savedBorderLight);
        var borderLight = savedBorderLight ?? "#E5E7EB"; // Default Light Gray
        var savedBorderDark = await _database.GetAsync<string>("Settings.BorderDark");
        var hasBorderDark = !string.IsNullOrEmpty(savedBorderDark);
        var borderDark = savedBorderDark ?? "#333333"; // Default Dark Gray
                var healedLight = OpaqueIfInvisible(borderLight);
                var healedDark = OpaqueIfInvisible(borderDark);
                if (healedLight != borderLight) { borderLight = healedLight; _ = _database.SetAsync("Settings.BorderLight", borderLight); }
                if (healedDark != borderDark) { borderDark = healedDark; _ = _database.SetAsync("Settings.BorderDark", borderDark); }
        
        // Load Widget Colors
        var editColor = await _database.GetAsync<string>("Settings.Color.Editing") ?? "#3b82f6";
        var revColor = await _database.GetAsync<string>("Settings.Color.Revision") ?? "#f97316";
        var lateColor = await _database.GetAsync<string>("Settings.Color.Late") ?? "#ef4444";
        var pointsColor = await _database.GetAsync<string>("Settings.Color.Points") ?? "#10b981";

        // Store internally for switching
        _lightBorderColor = borderLight;
        _darkBorderColor = borderDark;

        // Apply
        SetWidgetColor("Editing", editColor);
        SetWidgetColor("Revision", revColor);
        SetWidgetColor("Late", lateColor);
        SetWidgetColor("Points", pointsColor);
        
        // Initialize Log Colors based on initial Theme
        var isInitialLight = themeStr == "Light";
        UpdateLogColors(isInitialLight);

        // Apply Theme (border/bg hanya di-pin ke manual jika DB punya nilai custom user;
        // jika tidak, template layer yang menentukan tampilan).
        if (Enum.TryParse<ThemeVariantType>(themeStr, out var theme))
        {
            SetTheme(theme);
        }
        else
        {
            AppearancePaletteService.ApplyCurrent();
        }

        var activeIsLight = isInitialLight || themeStr == "Light";
        if (hasDarkBg || hasLightBg)
        {
            var bgHex = activeIsLight ? _lightBgColor : _darkBgColor;
            if (Color.TryParse(bgHex, out var bgc))
            {
                var bgBrush = new SolidColorBrush(bgc);
                AppearancePaletteService.SetManual("AppBackgroundBrush", bgBrush);
                AppearancePaletteService.SetManual("TerminalBackgroundBrush", bgBrush);
                AppearancePaletteService.SetManual("BackgroundDarkBrush", bgBrush);
            }
        }
        if (hasBorderLight || hasBorderDark)
        {
            var borderHex = activeIsLight ? _lightBorderColor : _darkBorderColor;
            AppearancePaletteService.SetManual("CardBorderBrush", SolidColorBrush.Parse(borderHex));
            AppearancePaletteService.SetManual("SeparatorBrush", SolidColorBrush.Parse(borderHex));
        }

        SetAccentColor(accentStr);
        SetFontFamily(fontStr);
        AppearancePaletteService.Select(await _database.GetAsync<string>(AppearancePaletteService.SettingKey));
    }
    
    // ... Existing Fields ...

    public void SetWidgetColor(string type, string hexColor)
    {
        if (Application.Current == null) return;
        if (hexColor == "RANDOM") hexColor = "#FFFFFF"; // Fallback

        if (Color.TryParse(hexColor, out var color))
        {
            if (type == "Accent")
            {
                AppearancePaletteService.SetManual("AccentBlueBrush", new SolidColorBrush(color));
                AppearancePaletteService.SetManual("AccentBlue", color);
                AppearancePaletteService.SetManual("AccentColorBrush", new SolidColorBrush(color));
                AppearancePaletteService.SetManual("AccentLowOpacityBrush", new SolidColorBrush(color) { Opacity = 0.15 });
                AppearancePaletteService.SetManual("AccentColor", color);
            }
            else
            {
                AppearancePaletteService.SetManual($"Accent{type}Brush", new SolidColorBrush(color));
                AppearancePaletteService.SetManual($"Accent{type}LowOpacityBrush", new SolidColorBrush(color) { Opacity = 0.35 });
                AppearancePaletteService.SetManual($"Accent{type}Color", color);
            }
        }
    }


    private string _lightBorderColor = "#E5E7EB";
    private string _darkBorderColor = "#333333";

    public void SetTheme(ThemeVariantType theme)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            ThemeVariantType.Light => ThemeVariant.Light,
            ThemeVariantType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        // Resolve actual theme to apply correct manual colors (Border, CardBg)
        bool isLight = false;
        if (theme == ThemeVariantType.Light) isLight = true;
        else if (theme == ThemeVariantType.Dark) isLight = false;
        else 
        {
            // System
            isLight = Application.Current.ActualThemeVariant == ThemeVariant.Light;
        }

        // Border + bg: update manual override HANYA jika user sudah custom (UpdateManual = no-op jika belum).
        var borderColor = isLight ? _lightBorderColor : _darkBorderColor;
        AppearancePaletteService.UpdateManual("CardBorderBrush", SolidColorBrush.Parse(borderColor));
        AppearancePaletteService.UpdateManual("SeparatorBrush", SolidColorBrush.Parse(borderColor));
        UpdateBackgroundBrush(isLight);
        
        // Update Log Colors
        UpdateLogColors(isLight);

        // Recompute layer (light/dark aware), re-apply manual di atasnya
        AppearancePaletteService.ApplyCurrent();
        _database.SetAsync("Settings.Theme", theme.ToString());
    }

    public void Receive(ThemeSettingsChangedMessage message)
    {
        if (!string.IsNullOrEmpty(message.DarkBackgroundColor)) _darkBgColor = message.DarkBackgroundColor;
        if (!string.IsNullOrEmpty(message.LightBackgroundColor)) _lightBgColor = message.LightBackgroundColor;
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        var hex = isLight ? _lightBgColor : _darkBgColor;
        if (Color.TryParse(hex, out var color))
        {
            // Aksi user (picker berubah) -> pin manual override di atas layer template.
            var brush = new SolidColorBrush(color);
            AppearancePaletteService.SetManual("AppBackgroundBrush", brush);
            AppearancePaletteService.SetManual("TerminalBackgroundBrush", brush);
            AppearancePaletteService.SetManual("BackgroundDarkBrush", brush);
        }
    }

    private void UpdateBackgroundBrush(bool isLight)
        {
            if (Application.Current == null) return;
            var hex = isLight ? _lightBgColor : _darkBgColor;
            if (Color.TryParse(hex, out var color))
            {
                var brush = new SolidColorBrush(color);
                AppearancePaletteService.UpdateManual("AppBackgroundBrush", brush);
                AppearancePaletteService.UpdateManual("TerminalBackgroundBrush", brush);
                AppearancePaletteService.UpdateManual("BackgroundDarkBrush", brush);
            }
        }

        /// <summary>
        /// Border/separator wajib opaque. Hex 8 digit AARRGGBB dengan alpha 00 (= transparan)
        /// menyebabkan tema Classic kehilangan semua garis separator/border.
        /// Nilai itu berasal dari Color.ToString() picker dengan A=0 (mis. "#00333333").
        /// </summary>
        private static string OpaqueIfInvisible(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return hex ?? "";
            if (!Color.TryParse(hex, out var c)) return hex!;
            return c.A == 0 ? Color.FromRgb(c.R, c.G, c.B).ToString() : hex;
        }

    private void UpdateLogColors(bool isLight)
    {
        if (Application.Current == null) return;

        // Info (Blue)
        Application.Current.Resources["LogInfoBrush"] = isLight ? SolidColorBrush.Parse("#0284c7") : SolidColorBrush.Parse("#60a5fa"); // Dark Blue vs Light Blue
        
        // System (Cyan)
        Application.Current.Resources["LogSystemBrush"] = isLight ? SolidColorBrush.Parse("#0891b2") : SolidColorBrush.Parse("#22d3ee"); // Dark Cyan vs Light Cyan
        
        // Success (Green)
        Application.Current.Resources["LogSuccessBrush"] = isLight ? SolidColorBrush.Parse("#16a34a") : SolidColorBrush.Parse("#4ade80"); // Dark Green vs Light Green
        
        // Warning (Yellow/Orange)
        Application.Current.Resources["LogWarningBrush"] = isLight ? SolidColorBrush.Parse("#d97706") : SolidColorBrush.Parse("#facc15"); // Dark Amber vs Light Yellow
        
        // Error (Red)
        Application.Current.Resources["LogErrorBrush"] = isLight ? SolidColorBrush.Parse("#dc2626") : SolidColorBrush.Parse("#f87171"); // Dark Red vs Light Red
        
        // Debug (Gray)
        Application.Current.Resources["LogDebugBrush"] = isLight ? SolidColorBrush.Parse("#4b5563") : SolidColorBrush.Parse("#9ca3af"); // Dark Gray vs Light Gray
    }

    public void SetBorderColor(string hexColor, bool isDark, bool saveToDb = true)
    {
        if (Application.Current == null) return;

        // Paksa opaque: border alpha 00 = tak terlihat (dulu bikin tema Classic tanpa garis sama sekali).
        hexColor = OpaqueIfInvisible(hexColor);
        if (isDark)
        {
            _darkBorderColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.BorderDark", hexColor);
        }
        else
        {
            _lightBorderColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.BorderLight", hexColor);
        }
        // Tulis hanya ke variant yang aktif; variant lain ditangani saat SetTheme.
        bool isActive = isDark
            ? Application.Current.RequestedThemeVariant == ThemeVariant.Dark
            : Application.Current.RequestedThemeVariant == ThemeVariant.Light;
        if (isActive)
        {
            AppearancePaletteService.SetManual("CardBorderBrush", SolidColorBrush.Parse(hexColor));
            AppearancePaletteService.SetManual("SeparatorBrush", SolidColorBrush.Parse(hexColor));
        }
    }



    public void SetAccentColor(string hexColor)
    {
        if (Application.Current == null) return;
        if (Color.TryParse(hexColor, out var color))
        {
            AppearancePaletteService.SetManual("AccentColor", color);
            AppearancePaletteService.SetManual("AccentColorBrush", new SolidColorBrush(color));
            AppearancePaletteService.SetManual("AccentLowOpacityBrush", new SolidColorBrush(color) { Opacity = 0.15 });
            AppearancePaletteService.SetManual("AccentBlue", color);
            AppearancePaletteService.SetManual("AccentBlueBrush", new SolidColorBrush(color));
            AppearancePaletteService.SetManual("AccentOrange", color);
            AppearancePaletteService.SetManual("AccentOrangeBrush", new SolidColorBrush(color));
            _database.SetAsync("Settings.Accent", hexColor);
        }
    }

    public void SetFontFamily(string fontFamily)
    {
        if (Application.Current == null) return;
        _database.SetAsync("Settings.Font", fontFamily);
    }
}

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using BMachine.SDK;

namespace BMachine.UI.Services;

public class ThemeService : IThemeService
{
    private readonly IDatabase _database;

    public ThemeService(IDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        // Load settings from DB
        var themeStr = await _database.GetAsync<string>("Settings.Theme") ?? "Dark";
        var accentStr = await _database.GetAsync<string>("Settings.Accent") ?? "#3b82f6"; // Default Blue
        var fontStr = await _database.GetAsync<string>("Settings.Font") ?? "Inter";
        
        // Load Border Colors
        var borderLight = await _database.GetAsync<string>("Settings.BorderLight") ?? "#E5E7EB"; // Default Light Gray
        var borderDark = await _database.GetAsync<string>("Settings.BorderDark") ?? "#333333"; // Default Dark Gray
        
        // Load Card Background Colors
        var cardBgLight = await _database.GetAsync<string>("Settings.CardBgLight") ?? "#FFFFFF"; 
        var cardBgDark = await _database.GetAsync<string>("Settings.CardBgDark") ?? "#1A1C20";
        
        // Load Widget Colors
        var editColor = await _database.GetAsync<string>("Settings.Color.Editing") ?? "#3b82f6";
        var revColor = await _database.GetAsync<string>("Settings.Color.Revision") ?? "#f97316";
        var lateColor = await _database.GetAsync<string>("Settings.Color.Late") ?? "#ef4444";
        var pointsColor = await _database.GetAsync<string>("Settings.Color.Points") ?? "#10b981";

        // Store internally for switching
        _lightBorderColor = borderLight;
        _darkBorderColor = borderDark;
        _lightCardBgColor = cardBgLight;
        _darkCardBgColor = cardBgDark;

        // Load Terminal Background Colors
        var termBgLight = await _database.GetAsync<string>("Settings.TermBgLight") ?? "#F8F9FA"; 
        var termBgDark = await _database.GetAsync<string>("Settings.TermBgDark") ?? "#1E1E1E";
        
        _lightTerminalBgColor = termBgLight;
        _darkTerminalBgColor = termBgDark;

        // Apply
        SetWidgetColor("Editing", editColor);
        SetWidgetColor("Revision", revColor);
        SetWidgetColor("Late", lateColor);
        SetWidgetColor("Points", pointsColor);
        
        // Initialize Log Colors based on initial Theme
        var isInitialLight = themeStr == "Light";
        UpdateLogColors(isInitialLight);

        // Apply Theme (This also applies the initial border)
        if (Enum.TryParse<ThemeVariantType>(themeStr, out var theme))
        {
            SetTheme(theme);
        }
        
        SetAccentColor(accentStr);
        SetFontFamily(fontStr);
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
                Application.Current.Resources["AccentBlueBrush"] = new SolidColorBrush(color);
                Application.Current.Resources["AccentBlue"] = color;
                
                 // Update color resource for consistency
                Application.Current.Resources["AccentColorBrush"] = new SolidColorBrush(color);
                Application.Current.Resources["AccentLowOpacityBrush"] = new SolidColorBrush(color) { Opacity = 0.15 };
                Application.Current.Resources["AccentColor"] = color;
            }
            else
            {
                Application.Current.Resources[$"Accent{type}Brush"] = new SolidColorBrush(color);
                Application.Current.Resources[$"Accent{type}LowOpacityBrush"] = new SolidColorBrush(color) { Opacity = 0.35 };
                Application.Current.Resources[$"Accent{type}Color"] = color;
            }
        }
    }


    private string _lightBorderColor = "#E5E7EB";
    private string _darkBorderColor = "#333333";
    private string _lightCardBgColor = "#FFFFFF";
    private string _darkCardBgColor = "#1A1C20";

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
        // If System/Default, we check ActualThemeVariant.
        // Note: This only sets the initial colors. Fully dynamic system switching 
        // would require listening to ActualThemeVariantChanged event.
        bool isLight = false;
        if (theme == ThemeVariantType.Light) isLight = true;
        else if (theme == ThemeVariantType.Dark) isLight = false;
        else 
        {
            // System
            isLight = Application.Current.ActualThemeVariant == ThemeVariant.Light;
        }

        // Apply Border Color for the new theme
        var borderColor = isLight ? _lightBorderColor : _darkBorderColor;
        var cardBgColor = isLight ? _lightCardBgColor : _darkCardBgColor;
        
        // Update the Dynamic Resource
        Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(borderColor);
        Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(cardBgColor);
        Application.Current.Resources["BackgroundDarkBrush"] = SolidColorBrush.Parse(cardBgColor); // Sync BackgroundDarkBrush
        
        // Terminal Background
        var termBgColor = isLight ? _lightTerminalBgColor : _darkTerminalBgColor;
        Application.Current.Resources["TerminalBackgroundBrush"] = SolidColorBrush.Parse(termBgColor);
        
        // Update Log Colors
        UpdateLogColors(isLight);

        // Fire and forget save
        _database.SetAsync("Settings.Theme", theme.ToString());
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
        
        if (isDark)
        {
            _darkBorderColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.BorderDark", hexColor);
            
            // If currently dark, apply immediately
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Dark)
            {
                Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
        else
        {
            _lightBorderColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.BorderLight", hexColor);
            
             // If currently light, apply immediately
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Light)
            {
                Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
    }

    public void SetCardBackgroundColor(string hexColor, bool isDark, bool saveToDb = true)
    {
        if (Application.Current == null) return;
        
        if (isDark)
        {
            _darkCardBgColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.CardBgDark", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Dark)
            {
                Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
                Application.Current.Resources["BackgroundDarkBrush"] = SolidColorBrush.Parse(hexColor); // Sync
            }
        }
        else
        {
            _lightCardBgColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.CardBgLight", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Light)
            {
                Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
                Application.Current.Resources["BackgroundDarkBrush"] = SolidColorBrush.Parse(hexColor); // Sync
            }
        }
    }

    private string _lightTerminalBgColor = "#F8F9FA";
    private string _darkTerminalBgColor = "#1E1E1E";

    public void SetTerminalBackgroundColor(string hexColor, bool isDark, bool saveToDb = true)
    {
        if (Application.Current == null) return;
        
        if (isDark)
        {
            _darkTerminalBgColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.TermBgDark", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Dark)
            {
                Application.Current.Resources["TerminalBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
        else
        {
            _lightTerminalBgColor = hexColor;
            if (saveToDb) _database.SetAsync("Settings.TermBgLight", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Light)
            {
                Application.Current.Resources["TerminalBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
    }

    public void SetAccentColor(string hexColor)
    {
        if (Application.Current == null) return;
        
        if (Color.TryParse(hexColor, out var color))
        {
            // Update Core Accent Resources
            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["AccentColorBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["AccentLowOpacityBrush"] = new SolidColorBrush(color) { Opacity = 0.15 };

            // Update Specific Variants (Blue/Orange) to match if we want unified theme
            Application.Current.Resources["AccentBlue"] = color;
            Application.Current.Resources["AccentBlueBrush"] = new SolidColorBrush(color);
            
            // Simplified: Keeping others as fallback or also unified?
            // User requested "Accent Color" generally, so let's unify.
            Application.Current.Resources["AccentOrange"] = color; 
            Application.Current.Resources["AccentOrangeBrush"] = new SolidColorBrush(color);
            
             _database.SetAsync("Settings.Accent", hexColor);
        }
    }

    public void SetFontFamily(string fontFamily)
    {
        if (Application.Current == null) return;
        _database.SetAsync("Settings.Font", fontFamily);
    }
}

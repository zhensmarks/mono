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

        // Store internally for switching
        _lightBorderColor = borderLight;
        _darkBorderColor = borderDark;
        _lightCardBgColor = cardBgLight;
        _darkCardBgColor = cardBgDark;

        // Apply Theme (This also applies the initial border)
        if (Enum.TryParse<ThemeVariantType>(themeStr, out var theme))
        {
            SetTheme(theme);
        }
        
        SetAccentColor(accentStr);
        SetFontFamily(fontStr);
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

        // Apply Border Color for the new theme
        var borderColor = theme == ThemeVariantType.Light ? _lightBorderColor : _darkBorderColor;
        var cardBgColor = theme == ThemeVariantType.Light ? _lightCardBgColor : _darkCardBgColor;
        
        // Update the Dynamic Resource
        Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(borderColor);
        Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(cardBgColor);
        
        // Fire and forget save
        _database.SetAsync("Settings.Theme", theme.ToString());
    }

    public void SetBorderColor(string hexColor, bool isDark)
    {
        if (Application.Current == null) return;
        
        if (isDark)
        {
            _darkBorderColor = hexColor;
            _database.SetAsync("Settings.BorderDark", hexColor);
            
            // If currently dark, apply immediately
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Dark)
            {
                Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
        else
        {
            _lightBorderColor = hexColor;
            _database.SetAsync("Settings.BorderLight", hexColor);
            
             // If currently light, apply immediately
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Light)
            {
                Application.Current.Resources["CardBorderBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
    }

    public void SetCardBackgroundColor(string hexColor, bool isDark)
    {
        if (Application.Current == null) return;
        
        if (isDark)
        {
            _darkCardBgColor = hexColor;
            _database.SetAsync("Settings.CardBgDark", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Dark)
            {
                Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
        else
        {
            _lightCardBgColor = hexColor;
            _database.SetAsync("Settings.CardBgLight", hexColor);
            
            if (Application.Current.RequestedThemeVariant == ThemeVariant.Light)
            {
                Application.Current.Resources["CardBackgroundBrush"] = SolidColorBrush.Parse(hexColor);
            }
        }
    }

    public void SetAccentColor(string hexColor)
    {
        if (Application.Current == null) return;
        
        if (Color.TryParse(hexColor, out var color))
        {
            // Update resource
            Application.Current.Resources["AccentBlue"] = color;
            Application.Current.Resources["AccentBlueBrush"] = new SolidColorBrush(color);
            
            // Also update other accents if we want a unified color
            Application.Current.Resources["AccentOrange"] = color; // Simplified for now
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

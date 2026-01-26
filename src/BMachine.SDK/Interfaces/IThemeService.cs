using System.Threading.Tasks;

namespace BMachine.SDK;

public enum ThemeVariantType
{
    Dark,
    Light,
    System
}

public interface IThemeService
{
    Task InitializeAsync();
    void SetTheme(ThemeVariantType theme);
    void SetAccentColor(string hexColor);
    void SetFontFamily(string fontFamily);
    void SetBorderColor(string hexColor, bool isDark, bool saveToDb = true);
    void SetCardBackgroundColor(string hexColor, bool isDark, bool saveToDb = true);
    void SetWidgetColor(string type, string hexColor);
    void SetTerminalBackgroundColor(string hexColor, bool isDark, bool saveToDb = true);
    // void SetFontSize(double scale); // Reserved for future
}

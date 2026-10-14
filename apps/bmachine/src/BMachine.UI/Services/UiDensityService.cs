using Avalonia;
using Avalonia.Media;

namespace BMachine.UI.Services;

public enum UiDensity
{
    Compact,
    Normal,
    Comfortable
}

public static class UiDensityService
{
    public const string SettingKey = "Settings.UiDensity";

    public static UiDensity Parse(string? value) => value switch
    {
        "Compact" => UiDensity.Compact,
        "Comfortable" => UiDensity.Comfortable,
        _ => UiDensity.Normal
    };

    public static void Apply(UiDensity density)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        var (btnHeight, btnFont, padX, padY, iconBtn, icon, stroke, textBody, textCaption) = density switch
        {
            UiDensity.Compact => (24.0, 11.0, 8.0, 2.0, 22.0, 12.0, 1.5, 12.0, 10.0),
            UiDensity.Comfortable => (40.0, 13.5, 20.0, 10.0, 34.0, 18.0, 2.0, 14.0, 12.0),
            _ => (36.0, 12.5, 16.0, 8.0, 28.0, 16.0, 1.8, 13.0, 11.0)
        };

        resources["DensityBtnHeight"] = btnHeight;
        resources["DensityBtnFont"] = btnFont;
        resources["DensityBtnPadding"] = new Thickness(padX, padY);
        resources["DensityIconBtn"] = iconBtn;
        resources["DensityIcon"] = icon;
        resources["DensityIconStroke"] = stroke;
        resources["DensityTextBody"] = textBody;
        resources["DensityTextCaption"] = textCaption;
    }
}
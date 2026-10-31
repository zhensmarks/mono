using Avalonia.Media;

namespace BMachine.UI.Models;

public sealed record AppearancePalette(string Id, string Name, string Primary, string Secondary, string Background, string ColorLabels, string LightBackground = "#F5F5F5")
{
    public IBrush PrimaryBrush => SolidColorBrush.Parse(Primary);
    public IBrush SecondaryBrush => SolidColorBrush.Parse(Secondary);
    public IBrush BackgroundBrush => SolidColorBrush.Parse(Background);
    public IBrush LightBackgroundBrush => SolidColorBrush.Parse(LightBackground);

    public static IReadOnlyList<AppearancePalette> All { get; } = new[]
    {
        new AppearancePalette("classic", "Classic (Default)", "#3B82F6", "#F97316", "#1C1C1C", "Tampilan awal dengan pengaturan warna pribadi Anda."),
        new AppearancePalette("spider", "Spider Red", "#D9272E", "#1557A5", "#171717", "Spider Red · Hero Blue · Web Black", "#FCF1F1"),
        new AppearancePalette("gwen", "Gwen Pink", "#F2A2C0", "#65D6D6", "#24242E", "Gwen Pink · Spider Cyan · Shadow Black", "#FCF5F8"),
        new AppearancePalette("armor", "Armor Red", "#A71930", "#D9A52E", "#252525", "Armor Red · Stark Gold · Charcoal Black", "#F8F2F0"),
        new AppearancePalette("midnight", "Midnight Black", "#8B5CF6", "#D62828", "#111111", "Midnight Black · Crimson Red · Electric Purple", "#F4F2FB"),
        new AppearancePalette("symbiote", "Symbiote Black", "#E8E8E8", "#34343B", "#080808", "Symbiote Black · Graphite Gray · Web White", "#F5F5F5")
    };

    public static AppearancePalette Find(string? id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
}

/// <summary>Nilai default Appearance yang dipakai tombol "Reset ke default". Preset Classic memakai persis nilai ini.</summary>
public static class ThemeDefaults
{
    public const string DarkBackground = "#1C1C1C";
    public const string LightBackground = "#F5F5F5";
    public const string BorderDark = "#333333";
    public const string BorderLight = "#E5E7EB";
    public const string Accent = "#3b82f6";
    public const string WidgetEditing = "#3b82f6";
    public const string WidgetRevision = "#f97316";
    public const string WidgetLate = "#ef4444";
    public const string WidgetPoints = "#10b981";
}

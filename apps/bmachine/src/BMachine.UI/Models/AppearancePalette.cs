using Avalonia.Media;

namespace BMachine.UI.Models;

public sealed record AppearancePalette(string Id, string Name, string Primary, string Secondary, string Background, string ColorLabels)
{
    public IBrush PrimaryBrush => SolidColorBrush.Parse(Primary);
    public IBrush SecondaryBrush => SolidColorBrush.Parse(Secondary);
    public IBrush BackgroundBrush => SolidColorBrush.Parse(Background);

    public static IReadOnlyList<AppearancePalette> All { get; } = new[]
    {
        new AppearancePalette("classic", "Classic (Default)", "#3B82F6", "#F97316", "#1C1C1C", "Tampilan awal dengan pengaturan warna pribadi Anda."),
        new AppearancePalette("spider", "Spider Red", "#D9272E", "#1557A5", "#171717", "Spider Red · Hero Blue · Web Black"),
        new AppearancePalette("gwen", "Gwen Pink", "#F2A2C0", "#65D6D6", "#24242E", "Gwen Pink · Spider Cyan · Shadow Black"),
        new AppearancePalette("armor", "Armor Red", "#A71930", "#D9A52E", "#252525", "Armor Red · Stark Gold · Charcoal Black"),
        new AppearancePalette("midnight", "Midnight Black", "#8B5CF6", "#D62828", "#111111", "Midnight Black · Crimson Red · Electric Purple"),
        new AppearancePalette("symbiote", "Symbiote Black", "#E8E8E8", "#34343B", "#080808", "Symbiote Black · Graphite Gray · Web White")
    };

    public static AppearancePalette Find(string? id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
}

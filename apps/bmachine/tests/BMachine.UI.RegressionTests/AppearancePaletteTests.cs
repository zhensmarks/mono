using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using BMachine.SDK;
using BMachine.UI.Messages;
using BMachine.UI.Models;
using BMachine.UI.Services;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class AppearancePaletteTests : IDisposable
{
    private static Application App => Application.Current!;
    private readonly Dictionary<object, object?> _resources;
    private readonly ThemeVariant _variant;

    public AppearancePaletteTests()
    {
        AppearancePaletteService.Select("classic");
        _resources = App.Resources.ToDictionary(x => x.Key, x => x.Value);
        _variant = App.RequestedThemeVariant!;
        App.RequestedThemeVariant = ThemeVariant.Dark;
    }

    public void Dispose()
    {
        AppearancePaletteService.Select("classic");
        App.RequestedThemeVariant = _variant;
        App.Resources.Clear();
        foreach (var (key, value) in _resources) App.Resources[key] = value!;
    }

    private static Color ResourceColor(string key)
    {
        var value = App.Resources[key];
        return value switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            _ => throw new Xunit.Sdk.XunitException($"Resource '{key}' was not a color or SolidColorBrush.")
        };
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void SetLocalResource(string key, object value) => App.Resources[key] = value;

    [AvaloniaFact]
    public void AllPalettesExposeExactIdsHexValuesAndLabels()
    {
        var expected = new[]
        {
            ("classic", "Classic (Default)", "#3B82F6", "#F97316", "#1C1C1C", "Tampilan awal dengan pengaturan warna pribadi Anda."),
            ("spider", "Spider Red", "#D9272E", "#1557A5", "#171717", "Spider Red · Hero Blue · Web Black"),
            ("gwen", "Gwen Pink", "#F2A2C0", "#65D6D6", "#24242E", "Gwen Pink · Spider Cyan · Shadow Black"),
            ("armor", "Armor Red", "#A71930", "#D9A52E", "#252525", "Armor Red · Stark Gold · Charcoal Black"),
            ("midnight", "Midnight Black", "#8B5CF6", "#D62828", "#111111", "Midnight Black · Crimson Red · Electric Purple"),
            ("symbiote", "Symbiote Black", "#E8E8E8", "#34343B", "#080808", "Symbiote Black · Graphite Gray · Web White")
        };
        Assert.Equal(expected.Length, AppearancePalette.All.Count);
        foreach (var (actual, wanted) in AppearancePalette.All.Zip(expected))
            Assert.Equal(wanted, (actual.Id, actual.Name, actual.Primary, actual.Secondary, actual.Background, actual.ColorLabels));
    }

    [AvaloniaFact]
    public void SelectingClassicRestoresRootValuesAndDoesNotLeakAbsentResources()
    {
        AppearancePaletteService.Select("classic");
        App.Resources.Remove("AppBackground");
        App.Resources.Remove("ButtonPrimaryBackgroundBrush");

        AppearancePaletteService.Select("spider");
        Assert.True(App.Resources.ContainsKey("AppBackground"));
        Assert.True(App.Resources.ContainsKey("ButtonPrimaryBackgroundBrush"));

        AppearancePaletteService.Select("classic");
        Assert.False(App.Resources.ContainsKey("AppBackground"));
        Assert.False(App.Resources.ContainsKey("ButtonPrimaryBackgroundBrush"));
    }

    [AvaloniaFact]
    public void SwitchingDarkAndLightKeepsTheSelectedPaletteApplied()
    {
        var service = new ThemeService(new FakeDatabase());
        service.SetTheme(ThemeVariantType.Dark);
        AppearancePaletteService.Select("armor");
        var darkAccent = ResourceColor("AccentColor");
        var darkBackground = ResourceColor("AppBackgroundBrush");

        service.SetTheme(ThemeVariantType.Light);
        Assert.Equal("#A71930", Hex(ResourceColor("ButtonPrimaryBackgroundBrush")));
        Assert.NotEqual(Hex(darkBackground), Hex(ResourceColor("AppBackgroundBrush")));
        Assert.True(AppearancePaletteService.Contrast(darkAccent, darkBackground) >= 4.5);

        service.SetTheme(ThemeVariantType.Dark);
        Assert.Equal("#A71930", Hex(ResourceColor("ButtonPrimaryBackgroundBrush")));
        Assert.Equal(Hex(darkBackground), Hex(ResourceColor("AppBackgroundBrush")));
    }

    [AvaloniaFact]
    public async Task InitializeLoadsSavedPaletteIdAndUnknownIdFallsBackToClassic()
    {
        var db = new FakeDatabase();
        await db.SetAsync(AppearancePaletteService.SettingKey, "midnight");
        await db.SetAsync("Settings.Theme", "Dark");
        var service = new ThemeService(db);
        await service.InitializeAsync();
        Assert.Equal("midnight", AppearancePaletteService.Current.Id);
        Assert.Equal("#8B5CF6", Hex(ResourceColor("ButtonPrimaryBackgroundBrush")));

        var unknown = new FakeDatabase();
        await unknown.SetAsync(AppearancePaletteService.SettingKey, "not-a-palette");
        var fallback = new ThemeService(unknown);
        await fallback.InitializeAsync();
        Assert.Equal("classic", AppearancePaletteService.Current.Id);
    }

    [AvaloniaFact]
    public void CustomAccentAndBackgroundAreRestoredAfterPaletteAndCanBeUpdatedWhileActive()
    {
        var db = new FakeDatabase();
        db.SetAsync("Appearance.Background.Dark", "#123456").Wait();
        db.SetAsync("Appearance.Background.Light", "#FEDCBA").Wait();
        db.SetAsync("Settings.Accent", "#ABCDEF").Wait();
        db.SetAsync("Settings.Theme", "Dark").Wait();
        var service = new ThemeService(db);
        service.InitializeAsync().GetAwaiter().GetResult();

        Assert.Equal("#ABCDEF", Hex(ResourceColor("AccentColor")));
        Assert.Equal("#123456", Hex(ResourceColor("AppBackgroundBrush")));
        AppearancePaletteService.Select("spider");
        Assert.Equal("#D9272E", Hex(ResourceColor("ButtonPrimaryBackgroundBrush")));

        service.SetAccentColor("#112233");
        service.Receive(new ThemeSettingsChangedMessage("#445566", "#DDEEFF"));
        Assert.Equal("#D9272E", Hex(ResourceColor("ButtonPrimaryBackgroundBrush")));

        AppearancePaletteService.Select("classic");
        Assert.Equal("#112233", Hex(ResourceColor("AccentColor")));
        Assert.Equal("#445566", Hex(ResourceColor("AppBackgroundBrush")));
    }

    [AvaloniaTheory]
    [InlineData("spider", false)]
    [InlineData("spider", true)]
    [InlineData("gwen", false)]
    [InlineData("gwen", true)]
    [InlineData("armor", false)]
    [InlineData("armor", true)]
    [InlineData("midnight", false)]
    [InlineData("midnight", true)]
    [InlineData("symbiote", false)]
    [InlineData("symbiote", true)]
    public void EveryPaletteHasReadableForegroundBackgroundAndPrimaryButtonContrast(string id, bool light)
    {
        new ThemeService(new FakeDatabase()).SetTheme(light ? ThemeVariantType.Light : ThemeVariantType.Dark);
        AppearancePaletteService.Select(id);
        var background = ResourceColor("AppBackgroundBrush");
        var foreground = ResourceColor("TextPrimary");
        var primaryBackground = ResourceColor("ButtonPrimaryBackgroundBrush");
        var primaryForeground = ResourceColor("ButtonPrimaryForegroundBrush");
        Assert.True(AppearancePaletteService.Contrast(ResourceColor("AccentColor"), background) >= 4.5);
        Assert.True(AppearancePaletteService.Contrast(ResourceColor("TextSecondary"), background) >= 4.5);

        Assert.True(AppearancePaletteService.Contrast(foreground, background) >= 4.5,
            $"{id} text/background contrast was below 4.5.");
        Assert.True(AppearancePaletteService.Contrast(primaryForeground, primaryBackground) >= 4.5,
            $"{id} primary button contrast was below 4.5.");
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(BMachine.UI.RegressionTests.HeadlessTestApp))]

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Headless Avalonia app used by regression tests with the BMachine resources needed
/// to instantiate and render real UI views without opening a visible screen.
/// </summary>
public class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://BMachine.UI/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });
        Styles.Add(new StyleInclude(new Uri("avares://BMachine.UI/"))
        {
            Source = new Uri("avares://BMachine.UI/Styles/BMachineDesignSystem.axaml")
        });

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://BMachine.UI/"))
        {
            Source = new Uri("avares://BMachine.UI/Themes/DarkTheme.axaml")
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://BMachine.UI/"))
        {
            Source = new Uri("avares://BMachine.UI/Styles/TablerIcons.axaml")
        });

        // Headless Linux does not resolve the app's bundled avares font collection.
        // Use installed system fonts for test rendering only; production styles remain unchanged.
        Resources["FontPrimary"] = new FontFamily("Noto Sans");
        Resources["FontMono"] = new FontFamily("Noto Sans Mono");
    }
}

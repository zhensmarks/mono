using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(BMachine.UI.RegressionTests.HeadlessTestApp))]

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Headless Avalonia app for unit tests. No window ever appears on a real screen;
/// everything runs in a hidden headless backend on a background session.
/// </summary>
public class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

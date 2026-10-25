using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;

namespace FrameStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            desktop.MainWindow = args.Contains("--frame-tool-prototype", StringComparer.Ordinal)
                ? new FrameToolPrototypeWindow()
                : new MainWindow(GetStartupPsdPath(args));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? GetStartupPsdPath(IReadOnlyList<string> args)
    {
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (string.Equals(args[index], "--open-psd", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[index + 1]))
                return args[index + 1];
        }

        return null;
    }
}

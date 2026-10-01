using Avalonia;
using System;
using System.Reflection;
using Abeng.BugReporter;

namespace PixelcutCompact;

class Program
{
    // Pemakai: jangan sentuh logic/tampilan; ini hanya melempar laporan ke ProjectBot.
    private static readonly BugReporter _bug = new BugReporter("PixelcutCompact");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Global handler: exception yang lepas dari task async (mis. automation browser).
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception
                    ?? new Exception($"Unhandled exception object: {e.ExceptionObject?.ToString() ?? "unknown"}");
                System.IO.File.WriteAllText("crash.log", ex.ToString());
                _bug.ReportCrash(ex, appVersion: ThisVersion(), context: "unhandled");
            }
            catch { }
        };

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash.log", ex.ToString());
            _bug.ReportCrash(ex, appVersion: ThisVersion(), context: "main-loop");
        }
    }

    // Baca <Version> dari csproj (AssemblyInformationalVersion).
    private static string? ThisVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v.Split('+')[0];
        }
        catch { return null; }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

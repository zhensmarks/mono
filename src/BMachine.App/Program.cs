using Avalonia;
using System;

namespace BMachine.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    private static readonly string LogFolder = ".logs";
    private static readonly string DebugLog = System.IO.Path.Combine(LogFolder, "debug.log");
    
    [STAThread]
    public static void Main(string[] args)
    {
        try 
        {
            // Ensure .logs folder exists
            System.IO.Directory.CreateDirectory(LogFolder);
            System.IO.File.WriteAllText(DebugLog, $"App Starting... [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n");
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            System.IO.Directory.CreateDirectory(LogFolder);
            System.IO.File.AppendAllText(DebugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CRASH: {ex}\n");
            throw; // Re-throw to ensure process exit code is error
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

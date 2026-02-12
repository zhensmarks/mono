using Avalonia;
using System;

namespace BMachine.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    // Use PlatformServiceFactory to get persistent path, but fallback to BaseDirectory if it fails/logs before init
    private static readonly string LogFolder = System.IO.Path.Combine(
        BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory(), 
        ".logs");
    private static readonly string DebugLog = System.IO.Path.Combine(LogFolder, "debug.log");
    
    [STAThread]
    public static void Main(string[] args)
    {
        // Optimization: Reduce process priority to minimize system lag
        try 
        { 
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; 
        } 
        catch { /* Ignore permission errors */ }

        // Global Exception Handler
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
                var logPath = System.IO.Path.Combine(appData, "crash_report.txt");
                var ex = e.ExceptionObject as Exception;
                System.IO.File.AppendAllText(logPath, 
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL UNHANDLED EXCEPTION:\n{ex}\n\n");
            }
            catch { }
        };

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
            try
            {
                var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
                var logPath = System.IO.Path.Combine(appData, "crash_report.txt");
                System.IO.File.AppendAllText(logPath, 
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MAIN LOOP CRASH:\n{ex}\n\n");
            }
            catch { }
            
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

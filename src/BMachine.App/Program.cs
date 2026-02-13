using Avalonia;
using System;

namespace BMachine.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Optimization: Reduce process priority
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
                var ex = e.ExceptionObject as Exception;
                // Try AppData path
                try 
                {
                    var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
                    var logPath = System.IO.Path.Combine(appData, "crash_report.txt");
                    string crashMsg = $"[{DateTime.Now}] FATAL UNHANDLED EXCEPTION:\n{ex}\n\n";
                    System.IO.File.AppendAllText(logPath, crashMsg);
                }
                catch { }
            }
            catch { }
        };

        try 
        {
            var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
            string logFolder = System.IO.Path.Combine(appData, ".logs");
            string debugLog = System.IO.Path.Combine(logFolder, "debug.log");

            // Ensure .logs folder exists
            System.IO.Directory.CreateDirectory(logFolder);
            System.IO.File.WriteAllText(debugLog, $"App Starting... [{DateTime.Now}]\n");
            
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
                var logPath = System.IO.Path.Combine(appData, "crash_report.txt");
                string crashMsg = $"[{DateTime.Now}] MAIN LOOP CRASH:\n{ex}\n\n";
                System.IO.File.AppendAllText(logPath, crashMsg);
            }
            catch { }
            
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

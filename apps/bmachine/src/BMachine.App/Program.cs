using Avalonia;
using System;
using System.Threading.Tasks;

namespace BMachine.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Removed: Reduce process priority to Normal (default) to ensure UI responsiveness 
        // when a heavy application like Photoshop is focused.

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

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            try
            {
                var ex = e.Exception;
                try
                {
                    var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
                    var logPath = System.IO.Path.Combine(appData, "crash_report.txt");
                    string crashMsg = $"[{DateTime.Now}] UNOBSERVED TASK EXCEPTION:\n{ex}\n\n";
                    System.IO.File.AppendAllText(logPath, crashMsg);
                }
                catch { }
            }
            catch { }
            e.SetObserved();
        };

        try 
        {
            var appData = BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory();
            string logFolder = System.IO.Path.Combine(appData, ".logs");
            string debugLog = System.IO.Path.Combine(logFolder, "debug.log");

            // Ensure .logs folder exists
            System.IO.Directory.CreateDirectory(logFolder);
            try
            {
                using (var fs = new System.IO.FileStream(debugLog, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var sw = new System.IO.StreamWriter(fs))
                {
                    sw.WriteLine($"App Starting... [{DateTime.Now}]");
                }
#if DEBUG
                global::System.Diagnostics.Trace.Listeners.Add(new global::System.Diagnostics.TextWriterTraceListener(debugLog));
                global::System.Diagnostics.Trace.AutoFlush = true;
#endif
            }
            catch { }
            
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

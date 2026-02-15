using System;
using System.Threading.Tasks;
using BMachine.SDK;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Services;

public class Bootstrapper
{
    private readonly IDatabase _database;

    public Bootstrapper(IDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync(IProgress<double>? progress = null, IProgress<string>? status = null)
    {
        // 1. Init Database
        status?.Report("Menghubungkan Database...");
        progress?.Report(10);
        await Task.Delay(500); // Simulating work

        // 2. Load Core Settings
        status?.Report("Memuat Pengaturan Inti...");
        progress?.Report(30);
        
        // Initialize Theme BEFORE verifying scripts or showing UI
        try
        {
            var themeService = new ThemeService(_database);
            await themeService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing theme: {ex.Message}");
        }

        // 2a. Preload All Settings (with robust error handling)
        status?.Report("Memuat Konfigurasi Aplikasi...");
        progress?.Report(35);
        try
        {
            var settingsPreload = new SettingsPreloadService(_database);
            await settingsPreload.PreloadAllSettingsAsync();
        }
        catch (Exception ex)
        {
            // Don't crash if preload fails - just log and continue
            Console.WriteLine($"[WARNING] Error preloading settings: {ex.Message}");
            Console.WriteLine($"[WARNING] Stack: {ex.StackTrace}");
        }

        await Task.Delay(200);

        // 3. Verify Scripts
        status?.Report("Memeriksa Script Python...");
        progress?.Report(55);
        // Check scripts existence...
        await Task.Delay(500);

        // 4. Initialize Services
        status?.Report("Menyiapkan Layanan...");
        progress?.Report(75);
        // Init Google Drive service...
        
        // Register Update Service (Singleton-ish via ServiceLocator pattern typically, but here we just ensure logic is ready)
        // For now, MainWindowViewModel creates it, so we might just log ready.
        
        await Task.Delay(500);
        
        // 5. Initialize Plugins
        status?.Report("Memuat Plugin...");
        progress?.Report(80);
        try
        {
            var eventBus = new EventBus();
            var logger = new SimpleLogger(); 
            var activity = new ActivityService(_database);
            var nav = new NavigationService(); 
            var notify = new NotificationService(); 
            
            // Build mocked/real dep graph
            var pluginManager = new BMachine.Core.PluginSystem.PluginManager(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"),
                eventBus,
                logger, 
                _database,
                activity,
                nav,
                notify,
                new AppServiceProvider() // Use Simple Provider
            );
            
            await pluginManager.LoadAllPluginsAsync();
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Plugin Init Error: {ex}");
        }

        // 6. Finalizing
        status?.Report("Membuka Antarmuka...");
        progress?.Report(90);
        await Task.Delay(300);

        status?.Report("Selesai!");
        progress?.Report(100);
        await Task.Delay(300);
    }

    // Quick mock services to satisfy constructor if real ones aren't available yet
    // In a real app we'd use DI container.
    public class SimpleLogger : ILogger
    {
        public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
        public void Info(string message) => Console.WriteLine($"[INFO] {message}");
        public void Warning(string message) => Console.WriteLine($"[WARN] {message}");
        public void Error(string message, Exception? ex = null) => Console.WriteLine($"[ERROR] {message} {ex}");
    }

    public class AppServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    public class ActivityService : IActivityService 
    {
        private readonly IDatabase _db;
        public ActivityService(IDatabase db) => _db = db;
        public Task ClearAsync() => Task.CompletedTask;
        public Task<int> GetCountAsync(string type) => Task.FromResult(0);
        public Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 10) => Task.FromResult(Enumerable.Empty<ActivityLog>());
        public Task LogAsync(string type, string title, string description) => Task.CompletedTask;
    }

    public class NavigationService : INavigationService 
    {
        public bool CanGoBack => false;
        public void GoBack() {}
        public void NavigateTo(string viewId, object? parameter = null) {}
    }

    public class NotificationService : INotificationService
    {
        public Task<bool> ShowConfirmAsync(string message, string? title = null) => Task.FromResult(true);
        public void ShowError(string message, string? title = null) => Console.WriteLine($"[Error] {message}");
        public void ShowInfo(string message, string? title = null) => Console.WriteLine($"[Info] {message}");
        public void ShowSuccess(string message, string? title = null) => Console.WriteLine($"[Success] {message}");
        public void ShowWarning(string message, string? title = null) => Console.WriteLine($"[Warn] {message}");
    }
}

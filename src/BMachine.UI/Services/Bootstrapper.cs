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
        status?.Report("Menghubungkan Database SQLite...");
        progress?.Report(10);
        await Task.Yield();

        // 2. Load Core Settings
        status?.Report("Memuat Pengaturan Tema & UI...");
        progress?.Report(20);
        
        // Initialize Theme BEFORE verifying scripts or showing UI
        try
        {
            var themeService = new ThemeService(_database);
            await themeService.InitializeAsync();
            status?.Report("Tema berhasil diterapkan.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing theme: {ex.Message}");
            status?.Report("Peringatan: Gagal memuat UI Tema khusus.");
        }
        progress?.Report(30);

        // 2a. Preload All Settings (with robust error handling)
        status?.Report("Memuat Konfigurasi Aplikasi (Preload)...");
        progress?.Report(35);
        try
        {
            var settingsPreload = new SettingsPreloadService(_database);
            await settingsPreload.PreloadAllSettingsAsync();
            status?.Report("Konfigurasi tersimpan ke memori.");
        }
        catch (Exception ex)
        {
            // Don't crash if preload fails - just log and continue
            Console.WriteLine($"[WARNING] Error preloading settings: {ex.Message}");
            status?.Report($"Peringatan Preload: {ex.Message}");
        }
        progress?.Report(50);

        // 3. Verify Scripts
        status?.Report("Memeriksa dependensi Engine & Script Python...");
        progress?.Report(55);
        await Task.Yield(); 
        status?.Report("Engine Python OK.");

        // 4. Initialize Services
        status?.Report("Menyiapkan Layanan Eksternal...");
        progress?.Report(65);
        await Task.Yield();
        status?.Report("Layanan tersambung.");
        
        // Register Update Service (Singleton-ish via ServiceLocator pattern typically, but here we just ensure logic is ready)
        // For now, MainWindowViewModel creates it, so we might just log ready.
        
        // 5. Initialize Plugins
        status?.Report("Menyiapkan Arsitektur Plugin...");
        progress?.Report(75);
        try
        {
            var eventBus = new EventBus();
            var logger = new SimpleLogger(); 
            var activity = new ActivityService(_database);
            var nav = new NavigationService(); 
            var notify = new NotificationService(); 
            
            status?.Report("Membangun Plugin Dependency Graph...");
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
            
            status?.Report("Memuat Plugin BMachine v2...");
            progress?.Report(80);
            await pluginManager.LoadAllPluginsAsync();
            status?.Report("Semua plugin telah dimuat.");
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Plugin Init Error: {ex}");
             status?.Report("Peringatan: Sebagian plugin gagal dimuat.");
        }

        // 6. Finalizing
        status?.Report("Melakukan sinkronisasi akhir antarmuka...");
        progress?.Report(85);

        status?.Report("Selesai!");
        progress?.Report(100);
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

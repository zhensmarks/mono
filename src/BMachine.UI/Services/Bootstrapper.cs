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

        await Task.Delay(200);

        // 3. Verify Scripts
        status?.Report("Memeriksa Script Python...");
        progress?.Report(50);
        // Check scripts existence...
        await Task.Delay(500);

        // 4. Initialize Services
        status?.Report("Menyiapkan Layanan...");
        progress?.Report(70);
        // Init Google Drive service...
        await Task.Delay(500);
        
        // 5. Finalizing
        status?.Report("Membuka Antarmuka...");
        progress?.Report(90);
        await Task.Delay(300);

        status?.Report("Selesai!");
        progress?.Report(100);
    }
}

using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using BMachine.SDK;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Services;

public partial class LanguageService : ObservableObject, ILanguageService
{
    private readonly IDatabase _database;
    private Dictionary<string, string> _currentStrings = new();
    
    [ObservableProperty]
    private CultureInfo _currentLanguage;

    public LanguageService(IDatabase database)
    {
        _database = database;
        _currentLanguage = new CultureInfo("en-US"); // Default
    }

    public IReadOnlyList<CultureInfo> AvailableLanguages { get; } = new[]
    {
        new CultureInfo("en-US"),
        new CultureInfo("id-ID")
    };

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (_currentStrings.TryGetValue(key, out var value))
        {
            return value;
        }
        return $"[{key}]"; // Fallback debug
    }

    public async Task InitializeAsync()
    {
        var langCode = await _database.GetAsync<string>("Settings.Language") ?? "en-US";
        await LoadLanguageAsync(langCode);
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        await LoadLanguageAsync(languageCode);
    }

    private async Task LoadLanguageAsync(string languageCode)
    {
        try 
        {
            // In a real app, these would be separate files (Assets/i18n/en.json).
            // For this embedded prototype, I'll simulate loading internal dictionaries.
            // Or better: Load from file if exists, else fallback.
            
            // Simulating content for now to avoid multiple file creation complexity initially,
            // later we can move to real files.
            
            Dictionary<string, string> dictionary = new();
            
            if (languageCode.StartsWith("id"))
            {
                dictionary = GetIndonesianDictionary();
                CurrentLanguage = new CultureInfo("id-ID");
            }
            else
            {
                dictionary = GetEnglishDictionary();
                CurrentLanguage = new CultureInfo("en-US");
            }

            _currentStrings = dictionary;
            
            // Save preference
            await _database.SetAsync("Settings.Language", languageCode);
            
            // Notify all properties changed to refresh UI bindings
            // "Item[]" is the special property name for the indexer
            OnPropertyChanged("Item[]"); 
            // Also notify generic change
            Dispatcher.UIThread.Post(() => OnPropertyChanged(string.Empty));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading language: {ex.Message}");
        }
    }

    private Dictionary<string, string> GetEnglishDictionary()
    {
        return new Dictionary<string, string>
        {
            ["App.Title"] = "Control Center",
            ["Nav.General"] = "General",
            ["Nav.Appearance"] = "Appearance",
            ["Nav.Account"] = "Account",
            ["Nav.Extensions"] = "Extensions",
            
            ["Extensions.Title"] = "Extensions Manager",
            ["Extensions.Add"] = "Add Extension",
            ["Extensions.NoExtensions"] = "No extensions installed.",
            ["Extensions.Version"] = "Version",
            ["Extensions.Author"] = "Author",
            ["Extensions.Delete"] = "Delete",
            
            ["General.Language"] = "Language",
            ["General.LanguageDesc"] = "Application display language.",
            ["General.Storage"] = "Storage",
            ["General.StorageDesc"] = "Offline data storage location.",
            ["General.System"] = "System",
            ["General.SystemDesc"] = "Allow full system access.",
            ["General.Profile"] = "User Profile",
            ["General.ProfileDesc"] = "Change your dashboard display name.",
            ["General.Save"] = "Save Profile",
            
            ["Appearance.Title"] = "Appearance",
            ["Appearance.Mode"] = "App Mode",
            ["Appearance.ModeDesc"] = "Choose light or dark appearance.",
            ["Appearance.Accent"] = "Accent Color",
            ["Appearance.AccentDesc"] = "Main color for buttons and icons.",
            ["Appearance.Dark"] = "Dark",
            ["Appearance.Light"] = "Light",
            
            ["Dashboard.Greeting"] = "Hello, {0}",
            ["Dashboard.GoodMorning"] = "Good Morning",
            ["Dashboard.GoodAfternoon"] = "Good Afternoon",
            ["Dashboard.GoodEvening"] = "Good Evening",
            
            ["Action.Renaming"] = "BATCH RENAMING",
            ["Action.RenamingDesc"] = "Batch file renaming",
            ["Action.RemoveBg"] = "REMOVE BG",
            ["Action.RemoveBgDesc"] = "Isolate subjects",
            ["Action.Upload"] = "UPLOAD GDRIVE",
            ["Action.UploadDesc"] = "Sync files",
            
            ["Stats.Editing"] = "EDITING",
            ["Stats.Revision"] = "REVISION",
            ["Stats.Late"] = "LATE",
            ["Stats.Points"] = "POINTS"
        };
    }

    private Dictionary<string, string> GetIndonesianDictionary()
    {
        return new Dictionary<string, string>
        {
            ["App.Title"] = "Pusat Kendali",
            ["Nav.General"] = "Umum",
            ["Nav.Appearance"] = "Tampilan",
            ["Nav.Account"] = "Akun",
            ["Nav.Extensions"] = "Ekstensi",
            
            ["Extensions.Title"] = "Manajer Ekstensi",
            ["Extensions.Add"] = "Tambah Ekstensi",
            ["Extensions.NoExtensions"] = "Belum ada ekstensi terpasang.",
            ["Extensions.Version"] = "Versi",
            ["Extensions.Author"] = "Pembuat",
            ["Extensions.Delete"] = "Hapus",
            
            ["General.Language"] = "Bahasa",
            ["General.LanguageDesc"] = "Bahasa tampilan aplikasi.",
            ["General.Storage"] = "Penyimpanan",
            ["General.StorageDesc"] = "Lokasi penyimpanan data offline.",
            ["General.System"] = "Sistem",
            ["General.SystemDesc"] = "Izinkan akses sistem penuh.",
            ["General.Profile"] = "Profil Pengguna",
            ["General.ProfileDesc"] = "Ubah nama yang tampil di dashboard.",
            ["General.Save"] = "Simpan Profil",
            
            ["Appearance.Title"] = "Tampilan",
            ["Appearance.Mode"] = "Mode Aplikasi",
            ["Appearance.ModeDesc"] = "Pilih tampilan terang atau gelap.",
            ["Appearance.Accent"] = "Warna Aksen",
            ["Appearance.AccentDesc"] = "Warna utama untuk tombol dan ikon.",
            ["Appearance.Dark"] = "Gelap",
            ["Appearance.Light"] = "Terang",
            
            ["Dashboard.Greeting"] = "Halo, {0}",
            ["Dashboard.GoodMorning"] = "Selamat Pagi",
            ["Dashboard.GoodAfternoon"] = "Selamat Siang",
            ["Dashboard.GoodEvening"] = "Selamat Malam",
            
            ["Action.Renaming"] = "PERULARAN",
            ["Action.RenamingDesc"] = "Ganti nama file massal",
            ["Action.RemoveBg"] = "HAPUS BG",
            ["Action.RemoveBgDesc"] = "Hapus latar belakang",
            ["Action.Upload"] = "UPLOAD GDRIVE",
            ["Action.UploadDesc"] = "Sinkronisasi file",
            
            ["Stats.Editing"] = "EDITING",
            ["Stats.Revision"] = "REVISI",
            ["Stats.Late"] = "SUSULAN",
            ["Stats.Points"] = "POIN"
        };
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.SDK;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using BMachine.Core.Platform;


namespace BMachine.UI.ViewModels;

public partial class PathSettingsViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly IPlatformService _platformService;

    public PathSettingsViewModel(IDatabase database, INotificationService notificationService)
    {
        _database = database;
        _notificationService = notificationService;
        _platformService = PlatformServiceFactory.Get();
        LoadPaths();
    }

    // Design-time constructor
    public PathSettingsViewModel()
    {
        _database = null!;
        _notificationService = null!;
    }

    [ObservableProperty] private string _pathProfesi = "";
    [ObservableProperty] private string _pathSporty = "";
    [ObservableProperty] private string _pathManasik10RP = "";
    [ObservableProperty] private string _pathManasik8R = "";
    [ObservableProperty] private string _pathPasFoto = "";
    [ObservableProperty] private string _pathWisuda10RP = "";
    [ObservableProperty] private string _pathWisuda8R = "";
    [ObservableProperty] private string _pathOkeBase = "";
    [ObservableProperty] private string _pathPhotoshop = "";
    [ObservableProperty] private string _pathLocalOutput = "";
    [ObservableProperty] private string _offlineStoragePath = "";

    // Auto-save when user manually types into a TextBox (TwoWay binding)
    partial void OnPathLocalOutputChanged(string value)   => _ = PersistPathAsync("Configs.Master.LocalOutput", value);
    partial void OnPathOkeBaseChanged(string value)       => _ = PersistPathAsync("Configs.Master.OkeBase", value);
    partial void OnPathPhotoshopChanged(string value)     => _ = PersistPathAsync("Configs.Master.PhotoshopPath", value);
    partial void OnPathProfesiChanged(string value)       => _ = PersistPathAsync("Configs.Master.Profesi", value);
    partial void OnPathSportyChanged(string value)        => _ = PersistPathAsync("Configs.Master.Sporty", value);
    partial void OnPathManasik10RPChanged(string value)   => _ = PersistPathAsync("Configs.Master.Manasik10RP", value);
    partial void OnPathManasik8RChanged(string value)     => _ = PersistPathAsync("Configs.Master.Manasik8R", value);
    partial void OnPathPasFotoChanged(string value)       => _ = PersistPathAsync("Configs.Master.PasFoto", value);
    partial void OnPathWisuda10RPChanged(string value)    => _ = PersistPathAsync("Configs.Master.Wisuda10RP", value);
    partial void OnPathWisuda8RChanged(string value)      => _ = PersistPathAsync("Configs.Master.Wisuda8R", value);
    partial void OnOfflineStoragePathChanged(string value)=> _ = PersistPathAsync("Configs.Storage.OfflinePath", value);

    private async Task PersistPathAsync(string key, string value)
    {
        if (_database == null) return;
        await _database.SetAsync(key, value ?? "");
        WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
    }
    
    [ObservableProperty]
    private ObservableCollection<string> _additionalMasterPaths = new();

    [ObservableProperty]
    private ObservableCollection<string> _additionalPhotoshopPaths = new();

    private async void LoadPaths()
    {
        if (_database == null) return;
        PathProfesi = await _database.GetAsync<string>("Configs.Master.Profesi") ?? "";
        PathSporty = await _database.GetAsync<string>("Configs.Master.Sporty") ?? "";
        PathManasik10RP = await _database.GetAsync<string>("Configs.Master.Manasik10RP") ?? "";
        PathManasik8R = await _database.GetAsync<string>("Configs.Master.Manasik8R") ?? "";
        PathPasFoto = await _database.GetAsync<string>("Configs.Master.PasFoto") ?? "";
        PathWisuda10RP = await _database.GetAsync<string>("Configs.Master.Wisuda10RP") ?? "";
        PathWisuda8R = await _database.GetAsync<string>("Configs.Master.Wisuda8R") ?? "";
        PathOkeBase = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? "";
        PathLocalOutput = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";

        PathPhotoshop = await _database.GetAsync<string>("Configs.Master.PhotoshopPath") ?? "";

        // Default to Downloads/BMachine_Attachments if empty
        var defaultStorage = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "BMachine_Attachments");
        OfflineStoragePath = await _database.GetAsync<string>("Configs.Storage.OfflinePath") ?? defaultStorage;

        // Load Additional Paths
        var jsonPaths = await _database.GetAsync<string>("Configs.Master.AdditionalPaths");
        if (!string.IsNullOrEmpty(jsonPaths))
        {
            try 
            {
                var paths = JsonSerializer.Deserialize<string[]>(jsonPaths);
                if (paths != null)
                {
                    AdditionalMasterPaths = new ObservableCollection<string>(paths);
                }
            }
            catch { }
        }

        // Load Additional Photoshop Paths
        var jsonPsPaths = await _database.GetAsync<string>("Configs.Master.PhotoshopPaths");
        if (!string.IsNullOrEmpty(jsonPsPaths))
        {
            try 
            {
                var paths = JsonSerializer.Deserialize<string[]>(jsonPsPaths);
                if (paths != null)
                {
                    AdditionalPhotoshopPaths = new ObservableCollection<string>(paths);
                }
            }
            catch { }
        }
        
        // Notify any listeners
        WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
    }
    
    [RelayCommand]
    private async Task AddMasterPath()
    {
         var storageProvider = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
         if (storageProvider == null) return;
         
         var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
         {
             Title = "Select Additional Master Folder",
             AllowMultiple = false
         });
         
         if (result != null && result.Count > 0)
         {
             var path = result[0].Path.LocalPath;
             if (!AdditionalMasterPaths.Contains(path))
             {
                 AdditionalMasterPaths.Add(path);
                 await SaveAdditionalPaths();
             }
         }
    }

    [RelayCommand]
    private async Task RemoveMasterPath(string path)
    {
        if (AdditionalMasterPaths.Contains(path))
        {
            AdditionalMasterPaths.Remove(path);
            await SaveAdditionalPaths();
        }
    }

    private async Task SaveAdditionalPaths()
    {
        if (_database == null) return;
        var json = JsonSerializer.Serialize(AdditionalMasterPaths);
        await _database.SetAsync("Configs.Master.AdditionalPaths", json);
        
        // Notify listeners (Dashboard/BatchVM) to reload
        WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
    }

    [RelayCommand]
    private async Task AddPhotoshopPath()
    {
         var storageProvider = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
         if (storageProvider == null) return;
         
         var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
         {
             Title = "Select Additional Photoshop Folder",
             AllowMultiple = false
         });
         
         if (result != null && result.Count > 0)
         {
             var path = result[0].Path.LocalPath;
             if (!AdditionalPhotoshopPaths.Contains(path))
             {
                 AdditionalPhotoshopPaths.Add(path);
                 await SaveAdditionalPhotoshopPaths();
             }
         }
    }

    [RelayCommand]
    private async Task RemovePhotoshopPath(string path)
    {
        if (AdditionalPhotoshopPaths.Contains(path))
        {
            AdditionalPhotoshopPaths.Remove(path);
            await SaveAdditionalPhotoshopPaths();
        }
    }

    private async Task SaveAdditionalPhotoshopPaths()
    {
        if (_database == null) return;
        var json = JsonSerializer.Serialize(AdditionalPhotoshopPaths);
        await _database.SetAsync("Configs.Master.PhotoshopPaths", json);
        
        WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
    }
    
    [RelayCommand]
    private async Task BrowseFile(string type)
    {
         var storageProvider = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
         if (storageProvider == null) return;
         string selectedPath = null;

         if (OperatingSystem.IsMacOS() && type == "Photoshop")
         {
             var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
             {
                 Title = $"Select Executable (.app) for {type}",
                 AllowMultiple = false
             });
             
             if (result != null && result.Count > 0)
             {
                 selectedPath = result[0].Path.LocalPath;
                 
                 // Auto-correct if user selected the parent folder instead of the .app bundle
                 if (!selectedPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                 {
                     var appBundles = System.IO.Directory.GetDirectories(selectedPath, "*.app");
                     if (appBundles.Length > 0)
                     {
                         // Pick the first .app bundle found inside the selected folder
                         // Ideally "Adobe Photoshop 202x.app"
                         selectedPath = appBundles.FirstOrDefault(p => p.Contains("Photoshop")) ?? appBundles[0];
                     }
                 }
             }
         }
         else
         {
             var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
             {
                 Title = $"Select Executable for {type}",
                 AllowMultiple = false,
                 FileTypeFilter = new[] 
                 { 
                     new FilePickerFileType("Executables") 
                     { 
                         Patterns = new[] { "*.exe", "*.app", "*.sh", "*.py" },
                         AppleUniformTypeIdentifiers = new[] { "com.apple.application-bundle", "public.executable" }
                     },
                     new FilePickerFileType("All Files") { Patterns = new[] { "*" } } 
                 }
             });
             
             if (result != null && result.Count > 0)
             {
                 selectedPath = result[0].Path.LocalPath;
             }
         }
         
         if (!string.IsNullOrEmpty(selectedPath))
         {
             if (type == "Photoshop")
             {
                 PathPhotoshop = selectedPath;
                 await _database.SetAsync("Configs.Master.PhotoshopPath", selectedPath);
             }
             _notificationService?.ShowSuccess($"{type} Path Updated");
         }
    }

    [RelayCommand]
    private void FixPhotoshopWarning()
    {
        var result = _platformService.SilencePhotoshopWarnings(PathPhotoshop);
        _notificationService?.ShowSuccess(result);
    }

    [RelayCommand]
    private async Task BrowsePath(string key)
    {
        var storageProvider = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
        if (storageProvider == null) return;

        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            AllowMultiple = false
        });

        if (result == null || result.Count == 0) return;
        var buffer = result[0].Path.LocalPath;
        if (string.IsNullOrEmpty(buffer)) return;

        if (key == "Manasik10RP")
        {
            PathManasik10RP = buffer;
            await _database.SetAsync("Configs.Master.Manasik10RP", buffer);
        }
        else if (key == "Manasik8R")
        {
            PathManasik8R = buffer;
            await _database.SetAsync("Configs.Master.Manasik8R", buffer);
        }
        else if (key == "Wisuda10RP")
        {
            PathWisuda10RP = buffer;
            await _database.SetAsync("Configs.Master.Wisuda10RP", buffer);
        }
        else if (key == "Wisuda8R")
        {
            PathWisuda8R = buffer;
            await _database.SetAsync("Configs.Master.Wisuda8R", buffer);
        }
        else if (key == "Profesi")
        {
            PathProfesi = buffer;
            await _database.SetAsync("Configs.Master.Profesi", buffer);
        }
        else if (key == "Sporty")
        {
            PathSporty = buffer;
            await _database.SetAsync("Configs.Master.Sporty", buffer);
        }
        else if (key == "PasFoto")
        {
            PathPasFoto = buffer;
            await _database.SetAsync("Configs.Master.PasFoto", buffer);
        }
        else if (key == "OkeBase")
        {
            PathOkeBase = buffer;
            await _database.SetAsync("Configs.Master.OkeBase", buffer);
        }
        else if (key == "LocalOutput")
        {
            PathLocalOutput = buffer;
            await _database.SetAsync("Configs.Master.LocalOutput", buffer);
        }
        else if (key == "OfflineStorage")
        {
            OfflineStoragePath = buffer;
            await _database.SetAsync("Configs.Storage.OfflinePath", buffer);
        }

        // Notify listeners to reload paths
        WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
    }

    // Called by TextBox when user manually edits a path field
    [RelayCommand]
    private async Task SaveManualPath(string key)
    {
        string buffer = key switch
        {
            "Manasik10RP"    => PathManasik10RP,
            "Manasik8R"      => PathManasik8R,
            "Wisuda10RP"     => PathWisuda10RP,
            "Wisuda8R"       => PathWisuda8R,
            "Profesi"        => PathProfesi,
            "Sporty"         => PathSporty,
            "PasFoto"        => PathPasFoto,
            "OkeBase"        => PathOkeBase,
            "LocalOutput"    => PathLocalOutput,
            "OfflineStorage" => OfflineStoragePath,
            _                => ""
        };

        string dbKey = key switch
        {
            "Manasik10RP"    => "Configs.Master.Manasik10RP",
            "Manasik8R"      => "Configs.Master.Manasik8R",
            "Wisuda10RP"     => "Configs.Master.Wisuda10RP",
            "Wisuda8R"       => "Configs.Master.Wisuda8R",
            "Profesi"        => "Configs.Master.Profesi",
            "Sporty"         => "Configs.Master.Sporty",
            "PasFoto"        => "Configs.Master.PasFoto",
            "OkeBase"        => "Configs.Master.OkeBase",
            "LocalOutput"    => "Configs.Master.LocalOutput",
            "OfflineStorage" => "Configs.Storage.OfflinePath",
            _                => ""
        };

        if (!string.IsNullOrEmpty(dbKey))
        {
            await _database.SetAsync(dbKey, buffer);
            WeakReferenceMessenger.Default.Send(new MasterPathsChangedMessage());
        }
    }
}

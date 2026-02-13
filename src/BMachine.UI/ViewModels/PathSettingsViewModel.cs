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


namespace BMachine.UI.ViewModels;

public partial class PathSettingsViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;

    public PathSettingsViewModel(IDatabase database, INotificationService notificationService)
    {
        _database = database;
        _notificationService = notificationService;
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
         var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
         if (topLevel == null) return;
         
         var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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
         var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
         if (topLevel == null) return;
         
         var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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
         var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
         if (topLevel == null) return;
         
         var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
         {
             Title = $"Select Executable for {type}",
             AllowMultiple = false,
             FileTypeFilter = new[] 
             { 
                 new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*.app", "*.sh", "*.py" } },
                 new FilePickerFileType("All Files") { Patterns = new[] { "*" } } 
             }
         });
         
         if (result != null && result.Count > 0)
         {
             var path = result[0].Path.LocalPath;
             if (type == "Photoshop")
             {
                 PathPhotoshop = path;
                 await _database.SetAsync("Configs.Master.PhotoshopPath", path);
             }
             _notificationService?.ShowSuccess($"{type} Path Updated");
         }
    }

    [RelayCommand]
    private async Task BrowsePath(string type)
    {
        var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Select Folder for {type}",
            AllowMultiple = false
        });

        if (result != null && result.Count > 0)
        {
            var path = result[0].Path.LocalPath;
            switch (type)
            {
                case "Profesi":
                    PathProfesi = path;
                    await _database.SetAsync("Configs.Master.Profesi", path);
                    break;
                case "Sporty":
                    PathSporty = path;
                    await _database.SetAsync("Configs.Master.Sporty", path);
                    break;
                case "Manasik10RP":
                    PathManasik10RP = path;
                    await _database.SetAsync("Configs.Master.Manasik10RP", path);
                    break;
                case "Manasik8R":
                    PathManasik8R = path;
                    await _database.SetAsync("Configs.Master.Manasik8R", path);
                    break;
                case "PasFoto":
                    PathPasFoto = path;
                    await _database.SetAsync("Configs.Master.PasFoto", path);
                    break;
                case "Wisuda10RP":
                    PathWisuda10RP = path;
                    await _database.SetAsync("Configs.Master.Wisuda10RP", path);
                    break;
                case "Wisuda8R":
                    PathWisuda8R = path;
                    await _database.SetAsync("Configs.Master.Wisuda8R", path);
                    break;
                case "OkeBase":
                    PathOkeBase = path;
                    await _database.SetAsync("Configs.Master.OkeBase", path);
                    break;
                case "LocalOutput":
                    PathLocalOutput = path;
                    await _database.SetAsync("Configs.Master.LocalOutput", path);
                    break;
                case "OfflineStorage":
                    OfflineStoragePath = path;
                    await _database.SetAsync("Configs.Storage.OfflinePath", path);
                    break;
            }
            _notificationService?.ShowSuccess($"{type} Path Updated");
        }
    }
}

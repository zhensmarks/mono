using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.Core.Database;
using System.Threading.Tasks;
using BMachine.SDK;

namespace BMachine.UI.ViewModels;

public partial class ExplorerSettingsViewModel : ObservableObject
{
    private readonly IDatabase _database;

    [ObservableProperty]
    private string _pathLocalOutput = "";

    [ObservableProperty]
    private string _shortcutNewFolder = "Ctrl+Shift+N"; // Placeholder

    [ObservableProperty]
    private string _shortcutNewFile = "Ctrl+Shift+T"; // Placeholder
    
    [ObservableProperty]
    private string _shortcutFocusSearch = "Ctrl+F"; // Placeholder

    public ExplorerSettingsViewModel(IDatabase database)
    {
        _database = database;
        _ = LoadSettings();
    }

    private async Task LoadSettings()
    {
        PathLocalOutput = await _database.GetAsync<string>("Configs.Path.LocalOutput") ?? "";
    }

    [RelayCommand]
    private async Task BrowsePath(string key)
    {
        var buffer = "";
        
        var dialog = new Avalonia.Controls.OpenFolderDialog
        {
            Title = "Select Folder"
        };

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var result = await dialog.ShowAsync(desktop.MainWindow);
            if (!string.IsNullOrWhiteSpace(result))
            {
                 buffer = result;
            }
        }

        if (!string.IsNullOrEmpty(buffer))
        {
            if (key == "LocalOutput")
            {
                PathLocalOutput = buffer;
                await _database.SetAsync("Configs.Path.LocalOutput", buffer);
            }
        }
    }
}

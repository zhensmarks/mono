using System;
using System.Linq;
using System.Threading.Tasks;
using BMachine.SDK;

namespace BMachine.UI.Services;

/// <summary>
/// Service to preload all application settings during splashscreen (Bootstrapper)
/// This eliminates lazy loading delays and RAM spikes when navigating to Settings
/// </summary>
public class SettingsPreloadService
{
    private readonly IDatabase _database;

    public SettingsPreloadService(IDatabase database)
    {
        _database = database;
    }

    public async Task PreloadAllSettingsAsync()
    {
        // All settings keys that are loaded by SettingsViewModel and DashboardViewModel
        var settingsKeys = new[]
        {
            // User Profile
            "User.Name",
            "User.Avatar",
            
            // Trello Integration
            "Trello.ApiKey",
            "Trello.Token",
            "Trello.IsConnected",
            "Trello.EditingBoardId",
            "Trello.EditingListId",
            "Trello.RevisionBoardId",
            "Trello.RevisionListId",
            "Trello.LateBoardId",
            "Trello.LateListId",
            "Trello.QcBoardId",
            
            // Google Integration
            "Google.CredsPath",
            "Google.SheetId",
            "Google.SheetName",
            "Google.SheetColumn",
            "Google.SheetRow",
            
            // Leaderboard
            "Leaderboard.Range",
            
            // Theme & Appearance (already loaded by ThemeService, but included for completeness)
            "Settings.Theme",
            "Settings.Accent",
            "Settings.Font",
            "Settings.BorderLight",
            "Settings.BorderDark",
            "Settings.CardBgLight",
            "Settings.CardBgDark",
            "Settings.TermBgLight",
            "Settings.TermBgDark",
            "Settings.Color.Editing",
            "Settings.Color.Revision",
            "Settings.Color.Late",
            "Settings.Color.Points",
            
            // Dashboard Navigation
            "Dashboard.Nav.Width",
            "Dashboard.Nav.Height",
            "Dashboard.Nav.Radius",
            "Dashboard.Nav.FontSize",
            "Dashboard.Nav.Style",
            "Dashboard.Nav.Text",
            "Dashboard.Nav.Text.Driver",
            "Dashboard.Nav.Text.Pixelcut",
            "Dashboard.Nav.Text.Batch",
            "Dashboard.Nav.Text.Locker",
            
            // Refresh Intervals
            "Settings.Interval.Editing",
            "Settings.Interval.Revision",
            "Settings.Interval.Late",
            "Settings.Interval.Points",
            
            // Dashboard Visibility
            "Settings.Dash.Gdrive",
            "Settings.Dash.Pixelcut",
            "Settings.Dash.Batch",
            "Settings.Dash.Lock",
            "Settings.Dash.Point",
            
            // Visual Settings
            "Settings.StartupAnim",
            "Settings.StatSpeed",
            "Settings.FloatingWidget",
            "Dashboard.IsFloatingWidgetVisible",
            
            // Background Colors
            "Appearance.Background.Dark",
            "Appearance.Background.Light",
            
            // Window State
            "Configs.Window.Width",
            "Configs.Window.Height",
            "Configs.Window.State",
            "Configs.Window.X",
            "Configs.Window.Y",
            
            // Shortcut Config
            "ShortcutConfig"
        };

        // Load all settings in parallel for maximum speed
        var loadTasks = settingsKeys.Select(key => _database.GetAsync<string>(key));
        await Task.WhenAll(loadTasks);
        
        Console.WriteLine($"[SettingsPreloadService] Preloaded {settingsKeys.Length} settings keys");
    }
}

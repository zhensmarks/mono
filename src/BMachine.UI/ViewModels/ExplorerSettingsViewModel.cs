using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.Core.Database;
using BMachine.UI.Messages;
using System.Threading.Tasks;
using BMachine.SDK;

namespace BMachine.UI.ViewModels;

public partial class ExplorerSettingsViewModel : ObservableObject
{
    private readonly IDatabase _database;

    [ObservableProperty]
    private string _pathLocalOutput = "";

    // --- Shortcuts (all editable via Record) ---
    [ObservableProperty] private string _shortcutNewFolder = "Ctrl+Shift+N";
    [ObservableProperty] private string _shortcutNewFile = "Ctrl+Shift+T";
    [ObservableProperty] private string _shortcutFocusSearch = "Ctrl+L";
    [ObservableProperty] private string _shortcutDelete = "Ctrl+D";
    [ObservableProperty] private string _shortcutNewWindow = "Ctrl+N";
    [ObservableProperty] private string _shortcutNewTab = "Ctrl+T";
    [ObservableProperty] private string _shortcutCloseTab = "Ctrl+W";
    [ObservableProperty] private string _shortcutNavigateUp = "Alt+Up";
    [ObservableProperty] private string _shortcutBack = "Alt+Left";
    [ObservableProperty] private string _shortcutForward = "Alt+Right";
    [ObservableProperty] private string _shortcutRename = "F2";
    [ObservableProperty] private string _shortcutPermanentDelete = "Shift+Delete";
    [ObservableProperty] private string _shortcutFocusSearchBox = "Ctrl+F";
    [ObservableProperty] private string _shortcutAddressBar = "Alt+D";
    [ObservableProperty] private string _shortcutAddressBar2 = "Ctrl+L";
    [ObservableProperty] private string _shortcutSwitchTab = "Ctrl+Tab";

    [ObservableProperty] private bool _isRecordingShortcut;
    [ObservableProperty] private string _recordingForKey = ""; // e.g. "ShortcutNewFolder"

    public ExplorerSettingsViewModel(IDatabase database)
    {
        _database = database;
        _ = LoadSettings();
    }

    private async Task LoadSettings()
    {
        PathLocalOutput = await _database.GetAsync<string>("Configs.Path.LocalOutput") ?? "";
        ShortcutNewFolder = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewFolder") ?? "Ctrl+Shift+N";
        ShortcutNewFile = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewFile") ?? "Ctrl+Shift+T";
        ShortcutFocusSearch = await _database.GetAsync<string>("Configs.Explorer.ShortcutFocusSearch") ?? "Ctrl+L";
        ShortcutDelete = await _database.GetAsync<string>("Configs.Explorer.ShortcutDelete") ?? "Ctrl+D";
        ShortcutNewWindow = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewWindow") ?? "Ctrl+N";
        ShortcutNewTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutNewTab") ?? "Ctrl+T";
        ShortcutCloseTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutCloseTab") ?? "Ctrl+W";
        ShortcutNavigateUp = await _database.GetAsync<string>("Configs.Explorer.ShortcutNavigateUp") ?? "Alt+Up";
        ShortcutBack = await _database.GetAsync<string>("Configs.Explorer.ShortcutBack") ?? "Alt+Left";
        ShortcutForward = await _database.GetAsync<string>("Configs.Explorer.ShortcutForward") ?? "Alt+Right";
        ShortcutRename = await _database.GetAsync<string>("Configs.Explorer.ShortcutRename") ?? "F2";
        ShortcutPermanentDelete = await _database.GetAsync<string>("Configs.Explorer.ShortcutPermanentDelete") ?? "Shift+Delete";
        ShortcutFocusSearchBox = await _database.GetAsync<string>("Configs.Explorer.ShortcutFocusSearchBox") ?? "Ctrl+F";
        ShortcutAddressBar = await _database.GetAsync<string>("Configs.Explorer.ShortcutAddressBar") ?? "Alt+D";
        ShortcutAddressBar2 = await _database.GetAsync<string>("Configs.Explorer.ShortcutAddressBar2") ?? "Ctrl+L";
        ShortcutSwitchTab = await _database.GetAsync<string>("Configs.Explorer.ShortcutSwitchTab") ?? "Ctrl+Tab";
    }

    [RelayCommand]
    private void StartRecording(string? shortcutKey)
    {
        if (string.IsNullOrEmpty(shortcutKey)) return;
        RecordingForKey = shortcutKey;
        IsRecordingShortcut = true;
        WeakReferenceMessenger.Default.Send(new ExplorerSettingsFocusMessage());
    }

    [RelayCommand]
    private void CancelRecording()
    {
        IsRecordingShortcut = false;
        RecordingForKey = "";
    }

    /// <summary>Called from view when a key is captured during recording. gestureString e.g. "Ctrl+Shift+N".</summary>
    public void ApplyRecordedShortcut(string gestureString)
    {
        if (string.IsNullOrEmpty(RecordingForKey)) return;
        switch (RecordingForKey)
        {
            case "ShortcutNewFolder": ShortcutNewFolder = gestureString; break;
            case "ShortcutNewFile": ShortcutNewFile = gestureString; break;
            case "ShortcutFocusSearch": ShortcutFocusSearch = gestureString; break;
            case "ShortcutDelete": ShortcutDelete = gestureString; break;
            case "ShortcutNewWindow": ShortcutNewWindow = gestureString; break;
            case "ShortcutNewTab": ShortcutNewTab = gestureString; break;
            case "ShortcutCloseTab": ShortcutCloseTab = gestureString; break;
            case "ShortcutNavigateUp": ShortcutNavigateUp = gestureString; break;
            case "ShortcutBack": ShortcutBack = gestureString; break;
            case "ShortcutForward": ShortcutForward = gestureString; break;
            case "ShortcutRename": ShortcutRename = gestureString; break;
            case "ShortcutPermanentDelete": ShortcutPermanentDelete = gestureString; break;
            case "ShortcutFocusSearchBox": ShortcutFocusSearchBox = gestureString; break;
            case "ShortcutAddressBar": ShortcutAddressBar = gestureString; break;
            case "ShortcutAddressBar2": ShortcutAddressBar2 = gestureString; break;
            case "ShortcutSwitchTab": ShortcutSwitchTab = gestureString; break;
        }
        _ = SaveShortcutAsync($"Configs.Explorer.{RecordingForKey}", gestureString);
        IsRecordingShortcut = false;
        RecordingForKey = "";
    }

    partial void OnShortcutNewFolderChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutNewFolder", value);
    partial void OnShortcutNewFileChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutNewFile", value);
    partial void OnShortcutFocusSearchChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutFocusSearch", value);
    partial void OnShortcutDeleteChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutDelete", value);
    partial void OnShortcutNewWindowChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutNewWindow", value);
    partial void OnShortcutNewTabChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutNewTab", value);
    partial void OnShortcutCloseTabChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutCloseTab", value);
    partial void OnShortcutNavigateUpChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutNavigateUp", value);
    partial void OnShortcutBackChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutBack", value);
    partial void OnShortcutForwardChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutForward", value);
    partial void OnShortcutRenameChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutRename", value);
    partial void OnShortcutPermanentDeleteChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutPermanentDelete", value);
    partial void OnShortcutFocusSearchBoxChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutFocusSearchBox", value);
    partial void OnShortcutAddressBarChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutAddressBar", value);
    partial void OnShortcutAddressBar2Changed(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutAddressBar2", value);
    partial void OnShortcutSwitchTabChanged(string value) => _ = SaveShortcutAsync("Configs.Explorer.ShortcutSwitchTab", value);

    private async Task SaveShortcutAsync(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        await _database.SetAsync(key, value ?? "");
        WeakReferenceMessenger.Default.Send(new ExplorerShortcutsChangedMessage());
    }

    [RelayCommand]
    private async Task BrowsePath(string key)
    {
        var buffer = "";
        var dialog = new Avalonia.Controls.OpenFolderDialog { Title = "Select Folder" };
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var result = await dialog.ShowAsync(desktop.MainWindow);
            if (!string.IsNullOrWhiteSpace(result)) buffer = result;
        }
        if (!string.IsNullOrEmpty(buffer) && key == "LocalOutput")
        {
            PathLocalOutput = buffer;
            await _database.SetAsync("Configs.Path.LocalOutput", buffer);
        }
    }
}

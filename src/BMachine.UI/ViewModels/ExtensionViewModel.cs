using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.Core.PluginSystem;

namespace BMachine.UI.ViewModels;

public partial class ExtensionViewModel : ObservableObject
{
    private readonly LoadedPlugin _plugin;
    private readonly Action<ExtensionViewModel> _toggleAction;
    private readonly Action<ExtensionViewModel> _deleteAction;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _version;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _author;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isNull;

    public ExtensionViewModel(LoadedPlugin plugin, Action<ExtensionViewModel> toggleAction, Action<ExtensionViewModel> deleteAction)
    {
        _plugin = plugin;
        _toggleAction = toggleAction;
        _deleteAction = deleteAction;

        _id = plugin.Plugin.Id;
        _name = plugin.Plugin.Name;
        _version = plugin.Plugin.Version;
        _description = plugin.Manifest?.Description ?? "";
        _author = plugin.Manifest?.Author ?? "";
        _isEnabled = true; // Default to enabled for now
    }

    [RelayCommand]
    private void Toggle()
    {
        _toggleAction?.Invoke(this);
    }

    [RelayCommand]
    private void Delete()
    {
        _deleteAction?.Invoke(this);
    }
}

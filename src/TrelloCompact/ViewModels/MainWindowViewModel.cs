using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TrelloCompact.Services;
using TrelloCompact.Models;
using CommunityToolkit.Mvvm.Input;

namespace TrelloCompact.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SettingsService _settings = new();
    private readonly TrelloApiService _api;

    [ObservableProperty]
    private bool _isSetupMode;

    [ObservableProperty]
    private SetupViewModel? _setupVm;

    [ObservableProperty]
    private AddTabViewModel? _addTabVm;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isAddTabDialogOpen;

    // Edit tab state
    [ObservableProperty] private bool _isEditTabDialogOpen;
    [ObservableProperty] private string _editTabName = "";
    [ObservableProperty] private string _editTabColor = "";
    private TabViewModel? _editingTab;

    // Settings dialog
    [ObservableProperty] private bool _isSettingsDialogOpen;
    [ObservableProperty] private string _settingsDisplayName = "";
    [ObservableProperty] private string _settingsDefaultMoveInfo = "";

    public MainWindowViewModel()
    {
        _api = new TrelloApiService(_settings);
        AddTabVm = new AddTabViewModel(_api, _settings, this);
        
        var cfg = _settings.Load();
        if (string.IsNullOrEmpty(cfg.TrelloApiKey) || string.IsNullOrEmpty(cfg.TrelloToken))
        {
            IsSetupMode = true;
            SetupVm = new SetupViewModel(_api, _settings, this);
        }
        else
        {
            IsSetupMode = false;
            LoadTabsFromSettings();
        }
    }

    public void FinishSetupAndStart()
    {
        IsSetupMode = false;
        SetupVm = null;
        LoadTabsFromSettings();
        if (Tabs.Count == 0) IsAddTabDialogOpen = true;
    }

    private void LoadTabsFromSettings()
    {
        Tabs.Clear();
        var cfg = _settings.Load();
        foreach (var t in cfg.Tabs.OrderBy(x => x.Order))
            Tabs.Add(new TabViewModel(t, _api, this));

        SelectedTab = Tabs.Any() ? Tabs.First() : null;
    }

    [RelayCommand]
    private void OpenAddTabDialog() => IsAddTabDialogOpen = true;

    [RelayCommand]
    private void SelectTab(TabViewModel tab) => SelectedTab = tab;

    // --- Edit Tab ---
    [RelayCommand]
    private void EditTab(TabViewModel tab)
    {
        if (tab == null) return;
        _editingTab = tab;
        EditTabName = tab.TabName;
        EditTabColor = tab.AccentColor;
        IsEditTabDialogOpen = true;
    }

    [RelayCommand]
    private void SaveEditTab()
    {
        if (_editingTab == null || string.IsNullOrWhiteSpace(EditTabName)) return;
        _editingTab.TabName = EditTabName;
        _editingTab.AccentColor = EditTabColor;

        var cfg = _settings.Load();
        var t = cfg.Tabs.FirstOrDefault(x => x.Id == _editingTab.TabConfigId);
        if (t != null)
        {
            t.Name = EditTabName;
            t.AccentColor = EditTabColor;
            _settings.Save(cfg);
        }
        IsEditTabDialogOpen = false;
        _editingTab = null;
    }

    [RelayCommand]
    private void CancelEditTab()
    {
        IsEditTabDialogOpen = false;
        _editingTab = null;
    }

    // --- Close Tab ---
    [RelayCommand]
    private void CloseTab(TabViewModel tab)
    {
        if (tab == null) return;
        var cfg = _settings.Load();
        cfg.Tabs.RemoveAll(x => x.Id == tab.TabConfigId);
        _settings.Save(cfg);

        var wasSelected = SelectedTab == tab;
        Tabs.Remove(tab);
        if (wasSelected) SelectedTab = Tabs.Any() ? Tabs.First() : null;
    }

    // --- Settings ---
    [RelayCommand]
    private void OpenSettings()
    {
        var cfg = _settings.Load();
        SettingsDisplayName = cfg.DisplayName ?? "";
        
        if (!string.IsNullOrEmpty(cfg.DefaultMoveBoardName) && !string.IsNullOrEmpty(cfg.DefaultMoveListName))
            SettingsDefaultMoveInfo = $"{cfg.DefaultMoveBoardName} → {cfg.DefaultMoveListName}";
        else
            SettingsDefaultMoveInfo = "Not set";

        IsSettingsDialogOpen = true;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var cfg = _settings.Load();
        cfg.DisplayName = SettingsDisplayName;
        _settings.Save(cfg);
        IsSettingsDialogOpen = false;
    }

    [RelayCommand]
    private void ClearDefaultMove()
    {
        var cfg = _settings.Load();
        cfg.DefaultMoveBoardId = null;
        cfg.DefaultMoveBoardName = null;
        cfg.DefaultMoveListId = null;
        cfg.DefaultMoveListName = null;
        _settings.Save(cfg);
        SettingsDefaultMoveInfo = "Not set";
    }

    [RelayCommand]
    private void CancelSettings() => IsSettingsDialogOpen = false;

    partial void OnIsAddTabDialogOpenChanged(bool value)
    {
        if (value && AddTabVm != null)
        {
            try { _ = AddTabVm.InitializeAsync(); } catch { }
        }
    }
}

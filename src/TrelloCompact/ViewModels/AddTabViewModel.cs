using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrelloCompact.Models;
using TrelloCompact.Services;

namespace TrelloCompact.ViewModels;

public partial class AddTabViewModel : ViewModelBase
{
    private readonly TrelloApiService _api;
    private readonly SettingsService _settings;
    private readonly MainWindowViewModel _mainVm;

    [ObservableProperty]
    private string _tabName = "New Tab";

    [ObservableProperty]
    private string _accentColor = "#3b82f6";

    public ObservableCollection<TrelloBoard> Boards { get; } = new();
    
    [ObservableProperty]
    private TrelloBoard? _selectedBoard;

    public ObservableCollection<TrelloList> Lists { get; } = new();

    [ObservableProperty]
    private TrelloList? _selectedList;
    
    [ObservableProperty]
    private bool _isLoading;

    public string[] PredefinedColors { get; } = new[] {
        "#ef4444", "#f97316", "#eab308", "#22c55e",
        "#0ea5e9", "#3b82f6", "#6366f1", "#a855f7", "#ec4899"
    };

    public AddTabViewModel(TrelloApiService api, SettingsService settings, MainWindowViewModel mainVm)
    {
        _api = api;
        _settings = settings;
        _mainVm = mainVm;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        Boards.Clear();
        var boards = await _api.GetBoardsAsync();
        foreach (var b in boards) Boards.Add(b);
        IsLoading = false;
    }

    partial void OnSelectedBoardChanged(TrelloBoard? value)
    {
        if (value != null) _ = LoadListsAsync(value.Id);
    }

    private async Task LoadListsAsync(string boardId)
    {
        IsLoading = true;
        Lists.Clear();
        var lists = await _api.GetListsAsync(boardId);
        foreach (var l in lists) Lists.Add(l);
        IsLoading = false;
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedBoard == null || SelectedList == null || string.IsNullOrWhiteSpace(TabName)) return;

        var cfg = _settings.Load();
        var tab = new CustomTab
        {
            Name = TabName,
            BoardId = SelectedBoard.Id,
            BoardName = SelectedBoard.Name,
            ListId = SelectedList.Id,
            ListName = SelectedList.Name,
            AccentColor = AccentColor,
            Order = cfg.Tabs.Count
        };
        cfg.Tabs.Add(tab);
        _settings.Save(cfg);

        _mainVm.IsAddTabDialogOpen = false;
        _mainVm.FinishSetupAndStart(); // Reload tabs
    }

    [RelayCommand]
    private void Cancel()
    {
        _mainVm.IsAddTabDialogOpen = false;
    }

    [RelayCommand]
    private void SetAccentColor(string color)
    {
        if (!string.IsNullOrEmpty(color))
            AccentColor = color;
    }
}

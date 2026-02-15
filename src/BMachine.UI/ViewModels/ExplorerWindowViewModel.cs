using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.Core.Platform;
using BMachine.SDK;
using BMachine.UI.Services;

namespace BMachine.UI.ViewModels;

public partial class ExplorerWindowViewModel : ObservableObject
{
    private readonly IDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly FileOperationManager _fileManager;
    private readonly IPlatformService _platformService;

    [ObservableProperty]
    private ObservableCollection<ExplorerTabItemViewModel> _tabs = new();

    [ObservableProperty]
    private ExplorerTabItemViewModel? _selectedTab;

    partial void OnSelectedTabChanged(ExplorerTabItemViewModel? value)
    {
        foreach (var tab in Tabs)
            tab.IsSelected = tab == value;
    }

    /// <summary>True when more than one tab (show tab bar).</summary>
    public bool ShowTabBar => Tabs.Count > 1;

    /// <summary>True when single tab (show folder name in title bar left, no tab bar).</summary>
    public bool ShowSingleTabTitle => Tabs.Count == 1;

    /// <summary>True when single tab (show content without tab strip).</summary>
    public bool ShowSingleTabContent => Tabs.Count == 1;

    public ExplorerWindowViewModel(
        IDatabase database,
        INotificationService notificationService,
        FileOperationManager fileManager,
        IPlatformService platformService,
        OutputExplorerViewModel? initialExplorer = null)
    {
        _database = database;
        _notificationService = notificationService;
        _fileManager = fileManager;
        _platformService = platformService;

        if (initialExplorer != null)
            Tabs.Add(new ExplorerTabItemViewModel(initialExplorer));
        else
            Tabs.Add(new ExplorerTabItemViewModel(new OutputExplorerViewModel(database, notificationService, fileManager, platformService)));

        SelectedTab = Tabs[0];
        Tabs.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(ShowTabBar)); OnPropertyChanged(nameof(ShowSingleTabTitle)); OnPropertyChanged(nameof(ShowSingleTabContent)); };
    }

    [RelayCommand]
    private void AddTab()
    {
        var vm = new OutputExplorerViewModel(_database, _notificationService, _fileManager, _platformService);
        Tabs.Add(new ExplorerTabItemViewModel(vm));
        SelectedTab = Tabs[^1];
    }

    public void CloseTab(ExplorerTabItemViewModel tab)
    {
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.RemoveAt(idx);
        OnPropertyChanged(nameof(ShowTabBar));
        OnPropertyChanged(nameof(ShowSingleTabTitle));
        OnPropertyChanged(nameof(ShowSingleTabContent));
        if (Tabs.Count == 0) return;
        if (SelectedTab == tab)
            SelectedTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
    }
}

public partial class ExplorerTabItemViewModel : ObservableObject
{
    public OutputExplorerViewModel ExplorerViewModel { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Title => string.IsNullOrEmpty(ExplorerViewModel.CurrentPath)
        ? "Output"
        : System.IO.Path.GetFileName(ExplorerViewModel.CurrentPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    /// <summary>Sub-text showing selection count, e.g. "3 items selected".</summary>
    public string SelectionInfo
    {
        get
        {
            var count = ExplorerViewModel.SelectedItems?.Count ?? 0;
            return count > 1 ? $"{count} items selected" : "";
        }
    }

    /// <summary>True when SelectionInfo should be shown (multiple selection).</summary>
    public bool HasSelectionInfo => (ExplorerViewModel.SelectedItems?.Count ?? 0) > 1;

    public ExplorerTabItemViewModel(OutputExplorerViewModel explorerViewModel)
    {
        ExplorerViewModel = explorerViewModel;
        ExplorerViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutputExplorerViewModel.CurrentPath))
                OnPropertyChanged(nameof(Title));
        };
        ExplorerViewModel.SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectionInfo));
            OnPropertyChanged(nameof(HasSelectionInfo));
        };
    }
}

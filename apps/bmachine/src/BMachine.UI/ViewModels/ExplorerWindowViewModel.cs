using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.Core.Platform;
using BMachine.SDK;
using BMachine.UI.Services;

namespace BMachine.UI.ViewModels;

public class PinnedTabsState
{
    public System.Collections.Generic.List<string> PinnedPaths { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, string> CustomLabels { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, string> CustomColors { get; set; } = new();
}

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
    public bool ShowTabBar => Tabs.Count > 1 || Tabs.Any(t => t.IsPinned);

    /// <summary>True when single unpinned tab (show folder name in title bar).</summary>
    public bool ShowSingleTabTitle => Tabs.Count == 1 && !Tabs.Any(t => t.IsPinned);

    /// <summary>True when single tab (show content without tab strip).</summary>
    public bool ShowSingleTabContent => Tabs.Count == 1 && !Tabs.Any(t => t.IsPinned);

    /// <summary>True when there are pinned tabs (show separator).</summary>
    public bool HasPinnedTabs => Tabs.Any(t => t.IsPinned) && Tabs.Any(t => !t.IsPinned);

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
        {
            initialExplorer.RequestOpenNewTab += OpenTabWithPath;
            Tabs.Add(new ExplorerTabItemViewModel(initialExplorer, this));
        }
        else
        {
            var vm = new OutputExplorerViewModel(database, notificationService, fileManager, platformService);
            vm.RequestOpenNewTab += OpenTabWithPath;
            Tabs.Add(new ExplorerTabItemViewModel(vm, this));
        }
        _ = LoadPinnedTabsAsync();

        SelectedTab = Tabs[0];
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowTabBar));
            OnPropertyChanged(nameof(ShowSingleTabTitle));
            OnPropertyChanged(nameof(ShowSingleTabContent));
            OnPropertyChanged(nameof(HasPinnedTabs));
        };
    }

    private async Task LoadPinnedTabsAsync()
    {
        var state = await _database.GetAsync<PinnedTabsState>("Explorer_PinnedTabs");
        if (state?.PinnedPaths != null && state.PinnedPaths.Count > 0)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Remove the default tab if it hasn't been navigated and we are restoring pinned tabs
                if (Tabs.Count == 1 && string.IsNullOrEmpty(Tabs[0].ExplorerViewModel.CurrentPath))
                {
                    Tabs.Clear();
                }

                // Insert pinned tabs at the front
                int insertAt = 0;
                foreach (var path in state.PinnedPaths)
                {
                    var vm = new OutputExplorerViewModel(_database, _notificationService, _fileManager, _platformService);
                    vm.NavigateTo(path);
                    var tabVm = new ExplorerTabItemViewModel(vm, this) { IsPinned = true };
                    // Restore custom label if any
                    if (state.CustomLabels != null && state.CustomLabels.TryGetValue(path, out var label) && !string.IsNullOrEmpty(label))
                        tabVm.PinnedTabLabel = label;
                    // Restore custom color if any
                    if (state.CustomColors != null && state.CustomColors.TryGetValue(path, out var color) && !string.IsNullOrEmpty(color))
                        tabVm.PinnedTabColor = color;
                    Tabs.Insert(insertAt++, tabVm);
                }

                if (Tabs.Count > 0 && SelectedTab == null)
                    SelectedTab = Tabs[0];
                    
                UpdateLastPinnedState();
                OnPropertyChanged(nameof(HasPinnedTabs));
            });
        }
    }

    private void UpdateLastPinnedState()
    {
        var lastPinned = Tabs.LastOrDefault(t => t.IsPinned);
        foreach (var tab in Tabs)
        {
            tab.IsLastPinned = (tab == lastPinned);
        }
    }

    private async Task SavePinnedTabsAsync() => await SavePinnedTabsPublicAsync();

    public async Task SavePinnedTabsPublicAsync()
    {
        var pinnedTabs = Tabs.Where(t => t.IsPinned).ToList();
        var pinnedPaths = pinnedTabs
            .Select(t => t.ExplorerViewModel.CurrentPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        var customLabels = pinnedTabs
            .Where(t => !string.IsNullOrEmpty(t.PinnedTabLabel) && !string.IsNullOrEmpty(t.ExplorerViewModel.CurrentPath))
            .ToDictionary(t => t.ExplorerViewModel.CurrentPath!, t => t.PinnedTabLabel!);
        var customColors = pinnedTabs
            .Where(t => !string.IsNullOrEmpty(t.ExplorerViewModel.CurrentPath))
            .ToDictionary(t => t.ExplorerViewModel.CurrentPath!, t => t.PinnedTabColor);
        await _database.SetAsync("Explorer_PinnedTabs", new PinnedTabsState { PinnedPaths = pinnedPaths, CustomLabels = customLabels, CustomColors = customColors });
    }

    [RelayCommand]
    private async Task TogglePinTabAsync(ExplorerTabItemViewModel tab)
    {
        if (tab == null || !Tabs.Contains(tab)) return;

        tab.IsPinned = !tab.IsPinned;

        // Re-sort tabs: newest pinned goes to front of pinned group
        var pinned = Tabs.Where(t => t.IsPinned).ToList();
        var unpinned = Tabs.Where(t => !t.IsPinned).ToList();

        Tabs.Clear();
        foreach (var p in pinned) Tabs.Add(p);
        foreach (var u in unpinned) Tabs.Add(u);

        SelectedTab = tab;

        UpdateLastPinnedState();
        OnPropertyChanged(nameof(HasPinnedTabs));
        await SavePinnedTabsAsync();
    }

    [RelayCommand]
    private void AddTab()
    {
        var vm = new OutputExplorerViewModel(_database, _notificationService, _fileManager, _platformService);
        vm.RequestOpenNewTab += OpenTabWithPath;
        Tabs.Add(new ExplorerTabItemViewModel(vm, this));
        SelectedTab = Tabs[^1];
    }

    private void OpenTabWithPath(string path)
    {
        var vm = new OutputExplorerViewModel(_database, _notificationService, _fileManager, _platformService);
        vm.RequestOpenNewTab += OpenTabWithPath;
        vm.NavigateTo(path);
        var tab = new ExplorerTabItemViewModel(vm, this);
        
        // Insert after current tab if possible, else at end
        int insertIndex = Tabs.Count;
        if (SelectedTab != null)
        {
            insertIndex = Tabs.IndexOf(SelectedTab) + 1;
        }
        Tabs.Insert(insertIndex, tab);
        SelectedTab = tab;
    }

    public async void CloseTab(ExplorerTabItemViewModel tab)
    {
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.RemoveAt(idx);
        OnPropertyChanged(nameof(ShowTabBar));
        OnPropertyChanged(nameof(ShowSingleTabTitle));
        OnPropertyChanged(nameof(ShowSingleTabContent));
        
        if (tab.IsPinned)
        {
            await SavePinnedTabsAsync();
        }

        if (Tabs.Count == 0) return;
        if (SelectedTab == tab)
            SelectedTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
            
        UpdateLastPinnedState();
        OnPropertyChanged(nameof(HasPinnedTabs));
    }
}

public partial class ExplorerTabItemViewModel : ObservableObject
{
    public OutputExplorerViewModel ExplorerViewModel { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinMenuText))]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isLastPinned;

    /// <summary>Custom short label for the pinned tab. Empty = use TitleInitials.</summary>
    [ObservableProperty]
    private string _pinnedTabLabel = string.Empty;

    partial void OnPinnedTabLabelChanged(string value) => OnPropertyChanged(nameof(DisplayInitials));

    // Palette of tasteful accent colors for pinned tab badges
    private static readonly string[] PinColorPalette =
    [
        "#F97316", // orange (default)
        "#8B5CF6", // violet
        "#10B981", // emerald
        "#3B82F6", // blue
        "#F43F5E", // rose
        "#FBBF24", // amber
        "#06B6D4", // cyan
        "#EC4899", // pink
    ];

    /// <summary>Hex color string for the pinned badge background.</summary>
    [ObservableProperty]
    private string _pinnedTabColor = "#F97316"; // default orange

    partial void OnPinnedTabColorChanged(string value) => OnPropertyChanged(nameof(BadgeBrush));

    public string PinMenuText => IsPinned ? "Unpin Tab" : "Pin Tab";

    public string Title => string.IsNullOrEmpty(ExplorerViewModel.CurrentPath)
        ? "Output"
        : System.IO.Path.GetFileName(ExplorerViewModel.CurrentPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public string TitleInitials
    {
        get
        {
            var title = Title;
            if (string.IsNullOrEmpty(title)) return "?";
            return title.Substring(0, 1).ToUpper();
        }
    }

    /// <summary>What to show inside the pinned badge - custom label or auto initials.</summary>
    public string DisplayInitials => !string.IsNullOrEmpty(PinnedTabLabel) ? PinnedTabLabel.Substring(0, Math.Min(2, PinnedTabLabel.Length)).ToUpper() : TitleInitials;

    public Avalonia.Media.IBrush BadgeBrush
    {
        get
        {
            try { return Avalonia.Media.SolidColorBrush.Parse(PinnedTabColor); }
            catch { return Avalonia.Media.Brushes.DarkOrange; }
        }
    }

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

    public ExplorerWindowViewModel Parent { get; }

    [RelayCommand]
    private void TogglePin()
    {
        Parent.TogglePinTabCommand.Execute(this);
    }

    [RelayCommand]
    private async Task CyclePinnedColor()
    {
        var idx = System.Array.IndexOf(PinColorPalette, PinnedTabColor);
        PinnedTabColor = PinColorPalette[(idx + 1) % PinColorPalette.Length];
        OnPropertyChanged(nameof(BadgeBrush));
        await Parent.SavePinnedTabsPublicAsync();
    }

    [RelayCommand]
    private async Task RenamePinned()
    {
        var dialog = new Avalonia.Controls.Window
        {
            Title = "Rename Tab",
            SizeToContent = Avalonia.Controls.SizeToContent.WidthAndHeight,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = Avalonia.Controls.SystemDecorations.Full,
        };

        var textBox = new Avalonia.Controls.TextBox
        {
            Text = PinnedTabLabel,
            Watermark = "e.g. DK, HM ...",
            Width = 180,
            MaxLength = 2,
            Margin = new Avalonia.Thickness(16, 0, 16, 0),
        };

        var okBtn = new Avalonia.Controls.Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8),
            MinWidth = 60,
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(4, 8, 8, 8),
            MinWidth = 60,
        };

        var btnRow = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(0, 16, 0, 0) };
        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Short label for pinned tab (max 2 chars):",
            Margin = new Avalonia.Thickness(16, 0, 16, 0),
            FontSize = 12,
        });
        panel.Children.Add(textBox);
        panel.Children.Add(btnRow);
        dialog.Content = panel;

        bool confirmed = false;
        okBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        textBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Return) { confirmed = true; dialog.Close(); } };

        // Find the ExplorerWindow (not the main window)
        var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.OfType<BMachine.UI.Views.ExplorerWindow>().LastOrDefault()
                ?? desktop.Windows.FirstOrDefault()
            : null;

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        if (confirmed && !string.IsNullOrEmpty(textBox.Text))
        {
            PinnedTabLabel = textBox.Text.Trim().ToUpper();
            await Parent.SavePinnedTabsPublicAsync();
        }
    }

    public ExplorerTabItemViewModel(OutputExplorerViewModel explorerViewModel, ExplorerWindowViewModel parent)
    {
        ExplorerViewModel = explorerViewModel;
        Parent = parent;
        ExplorerViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutputExplorerViewModel.CurrentPath))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(TitleInitials));
            }
        };
        ExplorerViewModel.SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectionInfo));
            OnPropertyChanged(nameof(HasSelectionInfo));
        };
    }
}

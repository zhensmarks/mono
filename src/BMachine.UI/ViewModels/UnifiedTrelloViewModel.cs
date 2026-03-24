using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class UnifiedTrelloViewModel : ObservableObject
{
    private readonly BMachine.SDK.IDatabase _database;
    private bool _editingAutoRefreshStarted;
    private bool _revisionAutoRefreshStarted;
    private bool _lateAutoRefreshStarted;

    [ObservableProperty]
    private EditingCardListViewModel _editingVM;
    
    [ObservableProperty]
    private RevisionCardListViewModel _revisionVM;
    
    [ObservableProperty]
    private LateCardListViewModel _lateVM;

    // 0 = Editing, 1 = Revisi, 2 = Susulan
    [ObservableProperty]
    private int _selectedTab = 0;

    // When true, status bar appears at bottom (dashboard embedded mode)
    [ObservableProperty]
    private bool _isEmbedded;

    /// <summary>Set by view when width &lt; threshold. Enables single-view stack navigation.</summary>
    [ObservableProperty]
    private bool _isCompactMode;

    public bool IsNotEmbedded => !IsEmbedded;

    /// <summary>True when panel (detail/comment/etc) should be shown as full-screen in compact mode.</summary>
    public bool ShouldShowPanelScreen =>
        ShouldShowRightPanel ||
        (ActiveViewModel?.IsBatchMoveMode == true);

    /// <summary>Compact mode: show list (no panel). Desktop: always true.</summary>
    public bool IsListScreenVisible => !IsCompactMode || !ShouldShowPanelScreen;

    /// <summary>Compact mode: show panel. Desktop: always true.</summary>
    public bool IsPanelScreenVisible => !IsCompactMode || ShouldShowPanelScreen;

    partial void OnIsEmbeddedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotEmbedded));
    }

    partial void OnIsCompactModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListScreenVisible));
        OnPropertyChanged(nameof(IsPanelScreenVisible));
    }

    public UnifiedTrelloViewModel(
        BMachine.SDK.IDatabase database, 
        EditingCardListViewModel editingVM, 
        RevisionCardListViewModel revisionVM, 
        LateCardListViewModel lateVM)
    {
        _database = database;
        EditingVM = editingVM;
        RevisionVM = revisionVM;
        LateVM = lateVM;
        editingVM.IsPanelHostedExternally = true;
        revisionVM.IsPanelHostedExternally = true;
        lateVM.IsPanelHostedExternally = true;
        void RaiseShouldShowRightPanel(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BaseTrelloListViewModel.IsAnyPanelOpen) or nameof(EditingCardListViewModel.IsAddManualPanelOpen))
            {
                OnPropertyChanged(nameof(ShouldShowRightPanel));
                OnPropertyChanged(nameof(ShouldShowPanelScreen));
                OnPropertyChanged(nameof(IsListScreenVisible));
                OnPropertyChanged(nameof(IsPanelScreenVisible));
            }
        }
        editingVM.PropertyChanged += RaiseShouldShowRightPanel;
        revisionVM.PropertyChanged += RaiseShouldShowRightPanel;
        lateVM.PropertyChanged += RaiseShouldShowRightPanel;

        void RaiseShouldShowPanelScreen(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BaseTrelloListViewModel.IsBatchMoveMode))
            {
                OnPropertyChanged(nameof(ShouldShowPanelScreen));
                OnPropertyChanged(nameof(IsListScreenVisible));
                OnPropertyChanged(nameof(IsPanelScreenVisible));
            }
        }
        editingVM.PropertyChanged += RaiseShouldShowPanelScreen;
        revisionVM.PropertyChanged += RaiseShouldShowPanelScreen;
        lateVM.PropertyChanged += RaiseShouldShowPanelScreen;
    }

    public BaseTrelloListViewModel ActiveViewModel
    {
        get
        {
            return SelectedTab switch
            {
                0 => EditingVM,
                1 => RevisionVM,
                2 => LateVM,
                _ => EditingVM
            };
        }
    }

    /// <summary>True when the right-column panel should be visible (card detail, comment, etc., or Add Manual for Editing).</summary>
    public bool ShouldShowRightPanel =>
        ActiveViewModel?.IsAnyPanelOpen == true ||
        (ActiveViewModel is EditingCardListViewModel e && e.IsAddManualPanelOpen);

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ActiveViewModel));
        OnPropertyChanged(nameof(IsEditingSelected));
        OnPropertyChanged(nameof(IsRevisionSelected));
        OnPropertyChanged(nameof(IsLateSelected));
        OnPropertyChanged(nameof(ShouldShowRightPanel));
        OnPropertyChanged(nameof(ShouldShowPanelScreen));
        OnPropertyChanged(nameof(IsListScreenVisible));
        OnPropertyChanged(nameof(IsPanelScreenVisible));
        OnPropertyChanged(nameof(ActiveViewModelTitle));
        EnsureActiveTabAutoRefreshStarted();
    }

    public bool IsEditingSelected => SelectedTab == 0;
    public bool IsRevisionSelected => SelectedTab == 1;
    public bool IsLateSelected => SelectedTab == 2;
    
    public string ActiveViewModelTitle => ActiveViewModel?.Title ?? "Trello";

    [RelayCommand]
    private void SwitchToEditing() => SelectedTab = 0;

    [RelayCommand]
    private void SwitchToRevision() => SelectedTab = 1;

    [RelayCommand]
    private void SwitchToLate() => SelectedTab = 2;

    public void EnsureActiveTabAutoRefreshStarted()
    {
        switch (SelectedTab)
        {
            case 0:
                if (!_editingAutoRefreshStarted)
                {
                    EditingVM.StartAutoRefresh();
                    _editingAutoRefreshStarted = true;
                }
                break;
            case 1:
                if (!_revisionAutoRefreshStarted)
                {
                    RevisionVM.StartAutoRefresh();
                    _revisionAutoRefreshStarted = true;
                }
                break;
            case 2:
                if (!_lateAutoRefreshStarted)
                {
                    LateVM.StartAutoRefresh();
                    _lateAutoRefreshStarted = true;
                }
                break;
        }
    }

    [RelayCommand]
    private void AddManualCard()
    {
        if (ActiveViewModel == EditingVM)
        {
            EditingVM.OpenAddManualPanelCommand?.Execute(null);
        }
    }

    [RelayCommand]
    private void RefreshActiveList()
    {
        if (ActiveViewModel == null || ActiveViewModel.IsRefreshing) return;

        if (ActiveViewModel == EditingVM)
        {
            EditingVM.RefreshCommand.Execute(null);
        }
        else if (ActiveViewModel == RevisionVM)
        {
            RevisionVM.RefreshCommand.Execute(null);
        }
        else if (ActiveViewModel == LateVM)
        {
            LateVM.RefreshCommand.Execute(null);
        }
    }

    /// <summary>Compact mode: navigate back through the view stack (Comment → Detail → List). No lag: only toggles VM state.</summary>
    [RelayCommand]
    private void CompactBack()
    {
        var vm = ActiveViewModel;
        if (vm == null) return;

        // LIFO: close topmost panel first
        if (vm.IsCommentPanelOpen) { vm.IsCommentPanelOpen = false; return; }
        if (vm.IsChecklistPanelOpen) { vm.IsChecklistPanelOpen = false; return; }
        if (vm.IsMovePanelOpen) { vm.CloseMovePanelCommand.Execute(null); return; }
        if (vm.IsAttachmentPanelOpen) { vm.IsAttachmentPanelOpen = false; return; }
        if (vm.IsDetailPanelOpen) { vm.CloseDetailPanelCommand.Execute(null); return; }
        if (vm.IsBatchMoveMode) { vm.CloseMovePanelCommand.Execute(null); return; }
        if (vm is EditingCardListViewModel evm && evm.IsAddManualPanelOpen)
        {
            evm.CloseAddManualPanelCommand?.Execute(null);
        }
    }
}

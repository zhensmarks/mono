using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class UnifiedTrelloViewModel : ObservableObject
{
    private readonly BMachine.SDK.IDatabase _database;

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

    public bool IsNotEmbedded => !IsEmbedded;

    partial void OnIsEmbeddedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotEmbedded));
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
                OnPropertyChanged(nameof(ShouldShowRightPanel));
        }
        editingVM.PropertyChanged += RaiseShouldShowRightPanel;
        revisionVM.PropertyChanged += RaiseShouldShowRightPanel;
        lateVM.PropertyChanged += RaiseShouldShowRightPanel;
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
        OnPropertyChanged(nameof(ActiveViewModelTitle));
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
}

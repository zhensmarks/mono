using Avalonia.Controls;
using Avalonia.Layout;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class UnifiedTrelloView : UserControl
{
    private const double CompactThreshold = 800;
    private bool _lastCompact;

    public UnifiedTrelloView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.AddHandler(
            Avalonia.Controls.Control.RequestBringIntoViewEvent,
            OnRequestBringIntoView,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            true);
        var rightContainer = this.FindControl<Grid>("Part_RightColumnContainer");
        if (rightContainer != null)
            rightContainer.SizeChanged += OnRightContainerSizeChanged;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDesktopColumnState(), Avalonia.Threading.DispatcherPriority.Loaded);

        var bBoard = this.FindControl<AutoCompleteBox>("Part_BatchMoveBoardBox");
        if (bBoard != null) BMachine.UI.Controls.AutoCompleteBoxHelper.Hook(bBoard);

        var bList = this.FindControl<AutoCompleteBox>("Part_BatchMoveListBox");
        if (bList != null) BMachine.UI.Controls.AutoCompleteBoxHelper.Hook(bList);
    }

    private void ApplyDesktopColumnState()
    {
        if (_lastCompact) return;
        var root = this.FindControl<Grid>("Part_RootGrid");
        var splitter = this.FindControl<GridSplitter>("Part_DetailSplitter");
        if (root == null || DataContext is not UnifiedTrelloViewModel uvm) return;
        bool show = uvm.ShouldShowPanelScreen;
        if (root.ColumnDefinitions.Count >= 3)
        {
            bool isCurrentlyOpen = root.ColumnDefinitions[2].Width.IsAbsolute && root.ColumnDefinitions[2].Width.Value > 0;
            if (show)
            {
                root.ColumnDefinitions[1].Width = new GridLength(6);
                // always apply current persisted width when visible so resize survives tab switch / reopen even if isCurrentlyOpen
                root.ColumnDefinitions[2].Width = new GridLength(uvm.DetailPanelWidth, GridUnitType.Pixel);
            }
            else
            {
                root.ColumnDefinitions[1].Width = new GridLength(0);
                root.ColumnDefinitions[2].Width = new GridLength(0);
            }
        }
        if (splitter != null) splitter.IsVisible = show;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        this.RemoveHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        var rightContainer = this.FindControl<Grid>("Part_RightColumnContainer");
        if (rightContainer != null)
            rightContainer.SizeChanged -= OnRightContainerSizeChanged;
        if (DataContext is UnifiedTrelloViewModel vm)
            vm.PropertyChanged -= UnifiedVM_PropertyChanged;
        if (_subscribedActiveVM != null)
            _subscribedActiveVM.PropertyChanged -= ActiveVM_PropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void OnRightContainerSizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (_lastCompact) return;
        if (DataContext is UnifiedTrelloViewModel uvm && uvm.ShouldShowPanelScreen)
        {
            double w = e.NewSize.Width;
            // clamp same as XAML MinWidth/MaxWidth
            if (w >= 320 && w <= 650 && Math.Abs(w - uvm.DetailPanelWidth) > 1)
            {
                uvm.DetailPanelWidth = w;
            }
        }
    }

    private void OnRootGridSizeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e is not Avalonia.Controls.SizeChangedEventArgs sizeEventArgs || sender is not Grid grid) return;

        bool isCompact = sizeEventArgs.NewSize.Width < CompactThreshold;
        if (isCompact == _lastCompact) return;
        _lastCompact = isCompact;

        var rightContainer = this.FindControl<Grid>("Part_RightColumnContainer");
        var mainPanelBorder = this.FindControl<Border>("Part_MainPanelBorder");
        var movePanel = this.FindControl<Border>("Part_MovePanel");
        var splitter = this.FindControl<GridSplitter>("Part_DetailSplitter");

        if (DataContext is UnifiedTrelloViewModel vm)
            vm.IsCompactMode = isCompact;

        if (isCompact)
        {
            if (rightContainer != null) Grid.SetColumn(rightContainer, 0);
            if (rightContainer != null) Grid.SetColumnSpan(rightContainer, 1);
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            if (mainPanelBorder != null)
            {
                mainPanelBorder.Width = double.NaN;
                mainPanelBorder.MinWidth = 0;
                mainPanelBorder.MaxWidth = double.PositiveInfinity;
                mainPanelBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            if (movePanel != null)
            {
                movePanel.Width = double.NaN;
                movePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            if (splitter != null) splitter.IsVisible = false;
        }
        else
        {
            bool showPanel = false;
            double panelWidth = 400;
            if (DataContext is UnifiedTrelloViewModel vm2)
            {
                showPanel = vm2.ShouldShowPanelScreen;
                panelWidth = vm2.DetailPanelWidth;
            }
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(showPanel ? new GridLength(6) : new GridLength(0)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(showPanel ? new GridLength(panelWidth, GridUnitType.Pixel) : new GridLength(0)));
            if (rightContainer != null)
            {
                Grid.SetColumn(rightContainer, 2);
                Grid.SetColumnSpan(rightContainer, 1);
            }
            if (mainPanelBorder != null)
            {
                mainPanelBorder.Width = double.NaN;
                mainPanelBorder.MinWidth = showPanel ? 320 : 0;
                mainPanelBorder.MaxWidth = showPanel ? 650 : double.PositiveInfinity;
            }
            if (movePanel != null)
            {
                movePanel.Width = double.NaN;
                movePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            if (splitter != null) splitter.IsVisible = showPanel;
        }
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is UnifiedTrelloViewModel vm)
        {
            vm.PropertyChanged -= UnifiedVM_PropertyChanged;
            vm.PropertyChanged += UnifiedVM_PropertyChanged;
            SubscribeToActiveViewModel(vm);
        }
    }

    private BaseTrelloListViewModel? _subscribedActiveVM;

    private void UnifiedVM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not UnifiedTrelloViewModel vm) return;
        if (e.PropertyName == nameof(UnifiedTrelloViewModel.ActiveViewModel))
        {
            SubscribeToActiveViewModel(vm);
            if (!_lastCompact) Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDesktopColumnState(), Avalonia.Threading.DispatcherPriority.Loaded);
        }
        if (e.PropertyName is nameof(UnifiedTrelloViewModel.ShouldShowPanelScreen) or nameof(UnifiedTrelloViewModel.ShouldShowRightPanel) or nameof(UnifiedTrelloViewModel.DetailPanelWidth))
        {
            if (!_lastCompact) ApplyDesktopColumnState();
        }
    }

    private void SubscribeToActiveViewModel(UnifiedTrelloViewModel vm)
    {
        if (_subscribedActiveVM != null)
            _subscribedActiveVM.PropertyChanged -= ActiveVM_PropertyChanged;

        _subscribedActiveVM = vm.ActiveViewModel;

        if (_subscribedActiveVM != null)
            _subscribedActiveVM.PropertyChanged += ActiveVM_PropertyChanged;
    }

    private void ActiveVM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not BaseTrelloListViewModel vm) return;

        // Reset teks AutoCompleteBox saat SelectedItem berubah ke null
        if (e.PropertyName == nameof(BaseTrelloListViewModel.SelectedMoveBoard) && vm.SelectedMoveBoard == null)
        {
            var boardBox = this.FindControl<AutoCompleteBox>("Part_BatchMoveBoardBox");
            if (boardBox != null) boardBox.Text = "";
        }

        if (e.PropertyName == nameof(BaseTrelloListViewModel.SelectedMoveList) && vm.SelectedMoveList == null)
        {
            var listBox = this.FindControl<AutoCompleteBox>("Part_BatchMoveListBox");
            if (listBox != null) listBox.Text = "";
        }

        // Reset semua saat panel ditutup
        if (e.PropertyName == nameof(BaseTrelloListViewModel.IsMovePanelOpen) && !vm.IsMovePanelOpen)
        {
            var boardBox = this.FindControl<AutoCompleteBox>("Part_BatchMoveBoardBox");
            var listBox = this.FindControl<AutoCompleteBox>("Part_BatchMoveListBox");
            if (boardBox != null) boardBox.Text = "";
            if (listBox != null) listBox.Text = "";
        }

        // When any panel open state changes, update desktop columns without waiting for resize
        if (!_lastCompact && e.PropertyName is nameof(BaseTrelloListViewModel.IsAnyPanelOpen) or nameof(BaseTrelloListViewModel.IsBatchMoveMode))
        {
            ApplyDesktopColumnState();
        }
    }
}

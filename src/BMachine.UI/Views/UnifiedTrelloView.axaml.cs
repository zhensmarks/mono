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
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        this.RemoveHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
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

        if (DataContext is UnifiedTrelloViewModel vm)
            vm.IsCompactMode = isCompact;

        if (isCompact)
        {
            // Mode kecil: 1 kolom, List dan Panel bergantian (visibility toggle, no lag)
            if (rightContainer != null) Grid.SetColumn(rightContainer, 0);
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
        }
        else
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            if (rightContainer != null) Grid.SetColumn(rightContainer, 1);
            if (mainPanelBorder != null)
            {
                mainPanelBorder.Width = 400;
                mainPanelBorder.MinWidth = 350;
                mainPanelBorder.MaxWidth = 500;
            }
            if (movePanel != null)
            {
                movePanel.Width = 400;
                movePanel.HorizontalAlignment = HorizontalAlignment.Right;
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BMachine.UI.Controls;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class PathSettingsView : UserControl
{
    private EditablePathItem? _pressedMasterPath;
    private Point? _masterPathPressPoint;
    private Control? _masterPathDropTarget;
    private bool _masterPathDropBefore;
    private bool _masterPathDragActive;
    private DispatcherTimer? _masterPathTapTimer;

    public PathSettingsView()
    {
        InitializeComponent();
        var scrollViewer = this.FindControl<ScrollViewer>("PathScrollViewer");
        if (scrollViewer != null) PointerScrollGuard.Attach(this, scrollViewer);
        AddHandler(PointerPressedEvent, OnViewPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnViewPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PathSettingsViewModel vm || IsInsideTextBox(e.Source)) return;

        foreach (var item in vm.AdditionalMasterPaths.Where(x => x.IsEditingName))
            item.IsEditingName = false;
        Focus();
    }

    private static bool IsInsideTextBox(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().Any(x => x is TextBox);

    public void OnMasterPathPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not EditablePathItem item ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || IsInteractiveSource(e.Source, control))
            return;

        _pressedMasterPath = item;
        _masterPathPressPoint = e.GetPosition(this);
    }

    public async void OnMasterPathPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_masterPathPressPoint is not Point start || _pressedMasterPath is null)
            return;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - start.X) < 6 && Math.Abs(position.Y - start.Y) < 6)
            return;

        var draggedItem = _pressedMasterPath;
        _pressedMasterPath = null;
        _masterPathPressPoint = null;

        var data = new DataObject();
        data.Set("AdditionalMasterPath", draggedItem);
        _masterPathDragActive = true;
        var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        _masterPathDragActive = false;

        if (result == DragDropEffects.None && _masterPathDropTarget?.DataContext is EditablePathItem target &&
            DataContext is PathSettingsViewModel vm)
            await MoveToDropTarget(vm, draggedItem, target, _masterPathDropBefore);

        ClearDropIndicator();
    }

    public void OnMasterPathPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedMasterPath = null;
        _masterPathPressPoint = null;
    }

    public void OnMasterPathTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not EditablePathItem item ||
            IsInteractiveSource(e.Source, control))
            return;

        _masterPathTapTimer?.Stop();
        _masterPathTapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _masterPathTapTimer.Tick += (_, _) =>
        {
            _masterPathTapTimer?.Stop();
            item.IsAppearanceOpen = !item.IsAppearanceOpen;
        };
        _masterPathTapTimer.Start();
    }

    public void OnMasterPathDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not EditablePathItem item ||
            IsInteractiveSource(e.Source, control))
            return;

        _masterPathTapTimer?.Stop();
        e.Handled = true;
        item.IsEditingName = true;

        var scrollViewer = this.FindControl<ScrollViewer>("PathScrollViewer");
        var offset = scrollViewer?.Offset ?? default;
        Dispatcher.UIThread.Post(() =>
        {
            control.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.IsVisible)?.Focus();
            if (scrollViewer != null) scrollViewer.Offset = offset;
        }, DispatcherPriority.Input);
    }

    public void OnMasterNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape && sender is Control { DataContext: EditablePathItem item })
        {
            item.IsEditingName = false;
            e.Handled = true;
        }
    }

    public void OnMasterNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: EditablePathItem item })
            item.IsEditingName = false;
    }

    public void OnMasterPathDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (sender is not Control target || target.DataContext is not EditablePathItem targetItem ||
            DataContext is not PathSettingsViewModel vm || !e.Data.Contains("AdditionalMasterPath") ||
            e.Data.Get("AdditionalMasterPath") is not EditablePathItem draggedItem || draggedItem == targetItem ||
            !vm.AdditionalMasterPaths.Contains(draggedItem))
        {
            ClearDropIndicator();
            return;
        }

        _masterPathDropBefore = vm.AdditionalMasterPaths.IndexOf(draggedItem) > vm.AdditionalMasterPaths.IndexOf(targetItem);
        ShowDropIndicator(target, _masterPathDropBefore);
        e.DragEffects = DragDropEffects.Move;
    }

    public void OnMasterPathDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control target && target == _masterPathDropTarget)
            ClearDropIndicator();
    }

    public async void OnMasterPathDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control target && target.DataContext is EditablePathItem targetItem &&
            DataContext is PathSettingsViewModel vm && e.Data.Get("AdditionalMasterPath") is EditablePathItem draggedItem)
            await MoveToDropTarget(vm, draggedItem, targetItem, _masterPathDropBefore);

        ClearDropIndicator();
    }

    private static bool IsInteractiveSource(object? source, Control container) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().TakeWhile(x => x != container)
            .Any(x => x is Button or TextBox or ColorPicker);

    private static async Task MoveToDropTarget(PathSettingsViewModel vm, EditablePathItem dragged, EditablePathItem target, bool before)
    {
        var oldIndex = vm.AdditionalMasterPaths.IndexOf(dragged);
        var targetIndex = vm.AdditionalMasterPaths.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0) return;

        var newIndex = before ? targetIndex : targetIndex + 1;
        if (oldIndex < newIndex) newIndex--;
        await vm.MoveAdditionalMasterPathAsync(dragged, newIndex);
    }

    private void ShowDropIndicator(Control target, bool before)
    {
        if (_masterPathDropTarget != target)
            ClearDropIndicator();

        var indicator = target.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(x => x.Name == "MasterPathDropIndicator");
        if (indicator == null) return;

        _masterPathDropTarget = target;
        indicator.VerticalAlignment = before ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
        indicator.RenderTransform = new TranslateTransform(0, before ? -10 : 10);
        indicator.IsVisible = true;
    }

    private void ClearDropIndicator()
    {
        if (_masterPathDropTarget != null)
        {
            var indicator = _masterPathDropTarget.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(x => x.Name == "MasterPathDropIndicator");
            if (indicator != null) indicator.IsVisible = false;
        }

        _masterPathDropTarget = null;
    }
}
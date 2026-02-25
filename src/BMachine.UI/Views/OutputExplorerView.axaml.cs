using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using Avalonia.Input;
using System.Linq;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using BMachine.UI.Models;
using Avalonia.Controls.Primitives;
using System.Collections.Generic;

namespace BMachine.UI.Views;

public partial class OutputExplorerView : UserControl
{
    private readonly System.Collections.Generic.List<KeyBinding> _explorerKeyBindings = new();
    private readonly System.Windows.Input.ICommand _requestCloseTabOrWindowCommand;
    private readonly System.Windows.Input.ICommand _requestNewTabCommand;

    // Drag-out state tracking
    private Point? _dragStartPoint;
    private bool _isDragging;
    private const double DragThreshold = 5;

    // Rubber-band state
    private bool _isSelecting;
    private bool _isDragSelectPending; // waiting for threshold
    private Point _selectionStartPoint; // in FileAreaGrid viewport coords
    private Vector _startScrollOffset; // scroll offset when drag started
    private Avalonia.Controls.Shapes.Rectangle? _selectionRect;
    private Canvas? _rubberBandCanvas;
    private Control? _selectionCaptureControl;
    private List<object> _initialSelection = new();

    public OutputExplorerView()
    {
        InitializeComponent();
        _requestCloseTabOrWindowCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            var w = this.GetVisualRoot();
            WeakReferenceMessenger.Default.Send(new RequestCloseExplorerWindowMessage(w));
        });
        _requestNewTabCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            WeakReferenceMessenger.Default.Send(new AddExplorerTabMessage(this.GetVisualRoot()));
        });
        // NOTE: OnFileAreaPointerPressed/Moved/Released are wired in XAML on FileAreaGrid
        this.KeyDown += OnRootKeyDown;
        Loaded += OnViewLoaded;
        WeakReferenceMessenger.Default.Register<FocusExplorerPathBarMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.Focus());
        });
        WeakReferenceMessenger.Default.Register<RequestCloseExplorerWindowMessage>(this, (_, m) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Only handle if this view's window was the target (message value is our window)
                var root = this.GetVisualRoot();
                if (root != m.Value) return;
                if (root is ExplorerWindow w)
                    w.HandleCloseTabOrWindow();
            });
        });
        WeakReferenceMessenger.Default.Register<ExplorerShortcutsReadyMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ReapplyExplorerShortcuts);
        });
        WeakReferenceMessenger.Default.Register<FocusSearchBoxMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var sb = this.FindControl<TextBox>("SearchBox");
                sb?.Focus();
            });
        });
        PointerWheelChanged += OnRootPointerWheelChanged;

        // Register DragDrop handlers
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Wire up dynamic Batch items injection for all ContextMenus
        AddHandler(Control.ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// When any ContextMenu opens, find the BatchMarker separator and inject
    /// batch script buttons in a single row, matching the BatchView style.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Find the context menu from the source control
        if (e.Source is not Control sourceControl) return;
        
        // Walk up to find the control with a context menu
        var ctrl = sourceControl;
        ContextMenu? menu = null;
        while (ctrl != null)
        {
            if (ctrl.ContextMenu != null)
            {
                menu = ctrl.ContextMenu;
                break;
            }
            ctrl = ctrl.Parent as Control;
        }
        if (menu == null) return;

        var vm = DataContext as OutputExplorerViewModel;
        if (vm == null || vm.ScriptOptions.Count == 0) return;

        // Find the BatchMarker separator
        int markerIndex = -1;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is Separator sep && sep.Tag is string tag && tag == "BatchMarker")
            {
                markerIndex = i;
                break;
            }
        }
        if (markerIndex < 0) return;

        // Remove any previously injected batch row and separator
        for (int i = menu.Items.Count - 1; i > markerIndex; i--)
        {
            if (menu.Items[i] is MenuItem mi && mi.Tag is string t && t == "BatchButtonRow")
                menu.Items.RemoveAt(i);
            else if (menu.Items[i] is Separator s && s.Tag is string stag && stag == "BatchSeparator")
                menu.Items.RemoveAt(i);
        }

        // Try to get the fallback icon geometry from resources
        Avalonia.Media.StreamGeometry? fallbackIcon = null;
        if (this.TryFindResource("IconScript", out var res) && res is Avalonia.Media.StreamGeometry sg)
            fallbackIcon = sg;

        // Create buttons in a centered horizontal StackPanel
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(4, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        foreach (var script in vm.ScriptOptions)
        {
            var iconData = script.IconGeometry ?? fallbackIcon;

            var icon = new Avalonia.Controls.PathIcon
            {
                Data = iconData,
                Width = 16,
                Height = 16,
                Foreground = Avalonia.Media.Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            var btn = new Button
            {
                Content = icon,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = Avalonia.Media.SolidColorBrush.Parse("#2A2A2A"),
                BorderThickness = new Thickness(0),
                Command = vm.BatchScriptCommand,
                CommandParameter = script,
            };
            ToolTip.SetTip(btn, script.Name);

            panel.Children.Add(btn);
        }

        // Wrap in a custom-templated MenuItem so it sits in the context menu
        var rowItem = new MenuItem
        {
            Header = null,
            Tag = "BatchButtonRow",
        };
        rowItem.Template = new Avalonia.Controls.Templates.FuncControlTemplate<MenuItem>((_, _) => panel);

        menu.Items.Insert(markerIndex + 1, rowItem);

        // Add a separator after the batch buttons to separate from items below
        // Check if one already exists (tagged "BatchSeparator")
        int afterRow = markerIndex + 2;
        if (afterRow >= menu.Items.Count || !(menu.Items[afterRow] is Separator sepAfter && sepAfter.Tag is string st && st == "BatchSeparator"))
        {
            var batchSep = new Separator { Tag = "BatchSeparator" };
            menu.Items.Insert(afterRow, batchSep);
        }
    }

    private void OnViewLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Focus();
        ApplyExplorerShortcuts();
        EnsureBackgroundMenuDataContext();
    }

    private void EnsureBackgroundMenuDataContext()
    {
        if (this.TryGetResource("BackgroundMenu", null, out var res) && res is ContextMenu menu)
        {
            menu.Opening += (s, _) =>
            {
                if (s is ContextMenu cm && cm.PlacementTarget is Control target)
                {
                    cm.DataContext = target.DataContext;
                    if (target.DataContext is OutputExplorerViewModel vm)
                        vm.UpdateClipboardStateAsync();
                }
            };
        }
    }

    private void ReapplyExplorerShortcuts()
    {
        foreach (var b in _explorerKeyBindings)
            KeyBindings.Remove(b);
        _explorerKeyBindings.Clear();
        ApplyExplorerShortcuts();
    }

    private void ApplyExplorerShortcuts()
    {
        if (DataContext is not OutputExplorerViewModel vm) return;
        var keyBindings = KeyBindings;
        TryAddKeyBinding(keyBindings, vm.ShortcutNewFolderGesture, vm.OpenNewFolderPopupCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutNewFileGesture, vm.OpenNewFilePopupCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutFocusSearchGesture, vm.FocusPathBarCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutDeleteGesture, vm.DeleteItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutNewWindowGesture, vm.NewExplorerWindowCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutNewTabGesture, _requestNewTabCommand, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCloseTabGesture, _requestCloseTabOrWindowCommand, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutNavigateUpGesture, vm.NavigateUpCommand!, null, _explorerKeyBindings);
        
        // Special Case: Do NOT add KeyBinding for "Back" key directly.
        // The "Back" key is handled manually in OnRootKeyDown to prevent conflict with TextBox editing.
        // Only add if it's NOT "Back".
        if (!string.Equals(vm.ShortcutBackGesture, "Back", System.StringComparison.OrdinalIgnoreCase))
        {
            TryAddKeyBinding(keyBindings, vm.ShortcutBackGesture, vm.GoBackCommand!, null, _explorerKeyBindings);
        }

        TryAddKeyBinding(keyBindings, vm.ShortcutForwardGesture, vm.GoForwardCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutRenameGesture, vm.RenameItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutPermanentDeleteGesture, vm.PermanentDeleteItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutFocusSearchBoxGesture, vm.FocusSearchBoxCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutAddressBarGesture, vm.FocusPathBarCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutSwitchTabGesture, vm.SwitchTabCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutRefreshGesture, vm.RefreshCommand!, null, _explorerKeyBindings);
        
        // View Layout Shortcuts (Ctrl+Shift+1..8)
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout1Gesture, vm.SetViewLayoutCommand, 1, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout2Gesture, vm.SetViewLayoutCommand, 2, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout3Gesture, vm.SetViewLayoutCommand, 3, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout4Gesture, vm.SetViewLayoutCommand, 4, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout5Gesture, vm.SetViewLayoutCommand, 5, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout6Gesture, vm.SetViewLayoutCommand, 6, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout7Gesture, vm.SetViewLayoutCommand, 7, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutLayout8Gesture, vm.SetViewLayoutCommand, 8, _explorerKeyBindings);

        // Standard shortcuts (not customizable via Settings) -> Now Customizable!
        TryAddKeyBinding(keyBindings, "Ctrl+A", vm.SelectAllCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCopyGesture, vm.CopyItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCutGesture, vm.CutItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutPasteGesture, vm.PasteItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCopyPathGesture, vm.CopyPathCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutPastePathGesture, vm.PastePathCommand!, null, _explorerKeyBindings);
        // Removed explicit "Back" binding here to handle it conditionally in OnRootKeyDown
        // TryAddKeyBinding(keyBindings, "Back", vm.GoBackCommand!, null, _explorerKeyBindings);
    }

    private static void TryAddKeyBinding(
        System.Collections.IList keyBindings,
        string gestureStr,
        ICommand command,
        object? commandParameter,
        System.Collections.Generic.List<KeyBinding>? trackList = null)
    {
        if (string.IsNullOrWhiteSpace(gestureStr) || keyBindings == null) return;
        try
        {
            var kb = new KeyBinding { Gesture = KeyGesture.Parse(gestureStr), Command = command, CommandParameter = commandParameter };
            keyBindings.Add(kb);
            trackList?.Add(kb);
        }
        catch { /* ignore invalid gesture */ }
    }
    
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ExplorerItemViewModel viewModel)
        {
            if (this.DataContext is OutputExplorerViewModel context)
            {
                // Prevent opening if renaming
                if (viewModel.IsEditing) return;

                context.OpenItemCommand.Execute(viewModel);
            }
        }
    }

    private void OnHorizontalListPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Redirect vertical scroll wheel to horizontal scrolling for the horizontal list
        if (sender is ListBox listBox && listBox.DataContext is OutputExplorerViewModel context && context.IsHorizontalLayout)
        {
             var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
             if (scrollViewer != null)
             {
                 if (e.Delta.Y != 0 && e.Delta.X == 0)
                 {
                     // Convert Y delta to X offset
                     double speed = 50; 
                     double offset = scrollViewer.Offset.X - (e.Delta.Y * speed);
                     
                     // Clamp
                     if (offset < 0) offset = 0;
                     if (offset > scrollViewer.Extent.Width - scrollViewer.Viewport.Width) 
                        offset = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;
                        
                     scrollViewer.Offset = new Vector(offset, scrollViewer.Offset.Y);
                     e.Handled = true;
                 }
             }
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back)
        {
             // Check if the event originated from a TextBox (SearchBox, Rename, etc.)
             if (e.Source is TextBox)
             {
                 // Let the TextBox handle Backspace (delete char)
                 return;
             }

             // Also check FocusManager as fallback (e.g. if Source is inside a template)
             var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
             if (focusManager?.GetFocusedElement() is TextBox)
             {
                 return;
             }

             var context = this.DataContext as OutputExplorerViewModel;
             if (context != null && context.GoBackCommand.CanExecute(null))
             {
                 context.GoBackCommand.Execute(null);
                 e.Handled = true;
             }
        }
    }



    private void OnRootPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not OutputExplorerViewModel vm) return;
        if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control) return;
        if (e.Delta.Y == 0) return;
        vm.CycleViewModeCommand.Execute(e.Delta.Y > 0);
        e.Handled = true;
    }

    private void OnFileAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control senderControl) return;

        // Ignore if source is a ListBoxItem/its content (let ListBox handle it)
        var sourceCtrl = e.Source as Control;
        if (sourceCtrl != null)
        {
            var walk = sourceCtrl;
            while (walk != null && walk != senderControl)
            {
                if (walk is ListBoxItem) return;
                walk = walk.Parent as Control;
            }
        }

        // Ignore ScrollBar clicks
        if (sourceCtrl is global::Avalonia.Controls.Primitives.ScrollBar) return;
        if (sourceCtrl?.Parent is global::Avalonia.Controls.Primitives.ScrollBar) return;

        var vm = DataContext as OutputExplorerViewModel;
        var point = e.GetCurrentPoint(senderControl);

        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount > 1) return; // Ignore double-click

            // Create canvas/rect programmatically if not yet created
            if (_rubberBandCanvas == null)
            {
                _rubberBandCanvas = new Canvas { IsHitTestVisible = false, ZIndex = 100 };
                _selectionRect = new Avalonia.Controls.Shapes.Rectangle
                {
                    IsVisible = false,
                    Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x22, 0x00, 0x78, 0xD7)),
                    Stroke = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x55, 0x00, 0x78, 0xD7)),
                    StrokeThickness = 1
                };
                _rubberBandCanvas.Children.Add(_selectionRect);
                if (senderControl is Grid fileAreaGrid)
                    fileAreaGrid.Children.Add(_rubberBandCanvas);
            }

            // sender IS FileAreaGrid (wired in XAML), use it as coordinate root
            _selectionStartPoint = e.GetPosition(senderControl);
            _isDragSelectPending = true;
            _isSelecting = false;
            _selectionCaptureControl = senderControl;

            // Capture scroll offset at drag start
            _startScrollOffset = GetActiveScrollOffset(senderControl);

            // Clear selection unless Ctrl held
            if (vm != null)
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    vm.SelectedItems.Clear();
                _initialSelection = vm.SelectedItems.Cast<object>().ToList();
            }

            e.Pointer.Capture(senderControl);
            e.Handled = true; // Prevent bubbling
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            vm?.UpdateClipboardStateAsync();
        }

        if (point.Properties.IsXButton1Pressed)
        {
            if (vm?.GoBackCommand.CanExecute(null) == true)
            { vm.GoBackCommand.Execute(null); e.Handled = true; }
        }
        else if (point.Properties.IsXButton2Pressed)
        {
            if (vm?.GoForwardCommand.CanExecute(null) == true)
            { vm.GoForwardCommand.Execute(null); e.Handled = true; }
        }
    }

    private void OnFileAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragSelectPending && !_isSelecting) return;
        if (sender is not Control senderControl) return;

        // sender IS FileAreaGrid (wired in XAML)
        var currentPoint = e.GetPosition(senderControl);

        // Get current scroll offset and compute delta since drag started
        var currentScrollOffset = GetActiveScrollOffset(senderControl);
        var scrollDelta = currentScrollOffset - _startScrollOffset;

        // Adjust start point by scroll delta:
        // If content scrolled DOWN by 100px, the original click position (in content)
        // has moved UP by 100px in viewport space
        var adjustedStart = new Point(
            _selectionStartPoint.X - scrollDelta.X,
            _selectionStartPoint.Y - scrollDelta.Y
        );

        var dx = currentPoint.X - adjustedStart.X;
        var dy = currentPoint.Y - adjustedStart.Y;

        // Apply drag threshold before starting visual selection
        if (_isDragSelectPending)
        {
            var rawDx = currentPoint.X - _selectionStartPoint.X;
            var rawDy = currentPoint.Y - _selectionStartPoint.Y;
            if (Math.Abs(rawDx) < 5 && Math.Abs(rawDy) < 5) return;
            _isDragSelectPending = false;
            _isSelecting = true;
            if (_selectionRect != null)
            {
                _selectionRect.IsVisible = true;
                _selectionRect.Width = 0;
                _selectionRect.Height = 0;
            }
        }

        if (!_isSelecting) return;

        // Compute selection rect in viewport space (for visual display)
        var x = Math.Min(adjustedStart.X, currentPoint.X);
        var y = Math.Min(adjustedStart.Y, currentPoint.Y);
        var width = Math.Abs(dx);
        var height = Math.Abs(dy);

        // Clamp to FileAreaGrid bounds for visual rect
        var clampedX = Math.Max(0, x);
        var clampedY = Math.Max(0, y);
        var clampedRight = Math.Min(senderControl.Bounds.Width, x + width);
        var clampedBottom = Math.Min(senderControl.Bounds.Height, y + height);

        if (_selectionRect != null)
        {
            Canvas.SetLeft(_selectionRect, clampedX);
            Canvas.SetTop(_selectionRect, clampedY);
            _selectionRect.Width = Math.Max(0, clampedRight - clampedX);
            _selectionRect.Height = Math.Max(0, clampedBottom - clampedY);
        }

        // Use UNCLAMPED rect for hit-testing (includes scrolled-away area)
        var hitTestRect = new Rect(x, y, width, height);
        UpdateRubberBandSelection(hitTestRect, senderControl, e.KeyModifiers);

        // Auto-scroll: check all visible ScrollViewers (both X and Y)
        foreach (var sv in senderControl.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (!sv.IsVisible) continue;
            var posInSv = e.GetPosition(sv);
            double threshold = 30, amount = 12;
            
            // Vertical auto-scroll
            if (posInSv.Y < threshold && posInSv.Y > -threshold)
                sv.Offset = new Vector(sv.Offset.X, Math.Max(0, sv.Offset.Y - amount));
            else if (posInSv.Y > sv.Bounds.Height - threshold && posInSv.Y < sv.Bounds.Height + threshold)
                sv.Offset = new Vector(sv.Offset.X, Math.Min(sv.Extent.Height - sv.Viewport.Height, sv.Offset.Y + amount));
            
            // Horizontal auto-scroll
            if (posInSv.X < threshold && posInSv.X > -threshold)
                sv.Offset = new Vector(Math.Max(0, sv.Offset.X - amount), sv.Offset.Y);
            else if (posInSv.X > sv.Bounds.Width - threshold && posInSv.X < sv.Bounds.Width + threshold)
                sv.Offset = new Vector(Math.Min(sv.Extent.Width - sv.Viewport.Width, sv.Offset.X + amount), sv.Offset.Y);
        }

        e.Handled = true; // Prevent other handlers
    }

    /// <summary>
    /// Gets the current scroll offset of the active ScrollViewer based on layout mode.
    /// Uses explicit named controls to avoid picking up wrong internal ListBox ScrollViewers.
    /// </summary>
    private Vector GetActiveScrollOffset(Control fileAreaGrid)
    {
        if (DataContext is OutputExplorerViewModel vm)
        {
            if (vm.IsSplitLayout)
            {
                var sv = this.FindControl<ScrollViewer>("SplitScrollViewer");
                if (sv != null) return sv.Offset;
            }
            else
            {
                string? listBoxName = null;
                if (vm.IsVerticalLayout) listBoxName = "VerticalListBox";
                else if (vm.IsTilesLayout) listBoxName = "TilesListBox";
                else if (vm.IsHorizontalLayout) listBoxName = "HorizontalListBox";
                else if (vm.IsThumbnailLayout) listBoxName = "ThumbnailListBox";
                
                if (listBoxName != null)
                {
                    var listBox = this.FindControl<ListBox>(listBoxName);
                    if (listBox != null)
                    {
                        var sv = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                        if (sv != null) return sv.Offset;
                    }
                }
            }
        }
        return new Vector(0, 0);
    }

    private void OnFileAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasSelecting = _isSelecting;
        bool wasPending = _isDragSelectPending;
        
        _isDragSelectPending = false;
        _isSelecting = false;
        _selectionCaptureControl = null;
        
        if (wasSelecting)
        {
            if (_selectionRect != null)
                _selectionRect.IsVisible = false;
            e.Handled = true; // Don't let ListBox handle this release (would clear selection)
        }
        
        e.Pointer.Capture(null);
    }

    private void UpdateRubberBandSelection(Rect bounds, Visual root, KeyModifiers modifiers)
    {
        if (DataContext is not OutputExplorerViewModel vm) return;

        var itemsToSelect = new System.Collections.Generic.HashSet<ExplorerItemViewModel>();

        // Preserve initial selection when Ctrl is held
        if (modifiers.HasFlag(KeyModifiers.Control) && _initialSelection != null)
        {
            foreach (var item in _initialSelection)
                if (item is ExplorerItemViewModel evm) itemsToSelect.Add(evm);
        }

        // Track realized items: which are IN the rect and which are OUTSIDE
        var realizedInRect = new System.Collections.Generic.HashSet<ExplorerItemViewModel>();
        var realizedOutsideRect = new System.Collections.Generic.HashSet<ExplorerItemViewModel>();

        // Hit-test ALL visible ListBoxItems across ALL ListBoxes
        foreach (var listBox in this.GetVisualDescendants().OfType<ListBox>())
        {
            if (!listBox.IsVisible) continue;
            foreach (var container in listBox.GetVisualDescendants().OfType<ListBoxItem>())
            {
                if (container.DataContext is not ExplorerItemViewModel item) continue;
                if (!item.IsSelectable) continue;

                var transform = container.TransformToVisual(root);
                if (!transform.HasValue) continue;

                var topLeft = transform.Value.Transform(new Point(0, 0));
                var itemRect = new Rect(topLeft, container.Bounds.Size);

                if (bounds.Intersects(itemRect))
                    realizedInRect.Add(item);
                else
                    realizedOutsideRect.Add(item);
            }
        }

        // Add all realized items that are IN the rect
        foreach (var item in realizedInRect)
            itemsToSelect.Add(item);

        // For items currently selected: keep them if they're de-realized (off-screen due to virtualization)
        // Only remove them if they're realized AND outside the rect
        foreach (var existingItem in vm.SelectedItems)
        {
            // Skip if already being selected (in rect or initial selection)
            if (itemsToSelect.Contains(existingItem)) continue;
            
            // If this item is realized and outside the rect → DON'T keep it (it will be removed)
            if (realizedOutsideRect.Contains(existingItem)) continue;
            
            // If this item is NOT realized (virtualized / scrolled off-screen) → KEEP it selected
            if (!realizedInRect.Contains(existingItem) && !realizedOutsideRect.Contains(existingItem))
                itemsToSelect.Add(existingItem);
        }

        // Commit selection diff
        var currentSet = new System.Collections.Generic.HashSet<ExplorerItemViewModel>(vm.SelectedItems);
        if (!currentSet.SetEquals(itemsToSelect))
        {
            var toRemove = currentSet.Where(i => !itemsToSelect.Contains(i)).ToList();
            foreach (var i in toRemove) vm.SelectedItems.Remove(i);
            foreach (var i in itemsToSelect)
                if (!currentSet.Contains(i)) vm.SelectedItems.Add(i);
        }
    }

    // --- Drag-and-Drop: Incoming (from external apps) ---



    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Show copy cursor when files are dragged over
        if (e.Data.Contains(Avalonia.Input.DataFormats.Files) || e.Data.Contains(Avalonia.Input.DataFormats.FileNames))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not OutputExplorerViewModel vm) return;

        var paths = new System.Collections.Generic.List<string>();

        if (e.Data.Contains(Avalonia.Input.DataFormats.Files))
        {
            var storageItems = e.Data.GetFiles();
            if (storageItems != null)
            {
                foreach (var item in storageItems)
                {
                    try
                    {
                        var localPath = item.Path?.LocalPath;
                        if (!string.IsNullOrEmpty(localPath))
                            paths.Add(localPath);
                    }
                    catch { /* skip items without local path */ }
                }
            }
        }

        if (paths.Count == 0 && e.Data.Contains(Avalonia.Input.DataFormats.FileNames))
        {
            var fileNames = e.Data.GetFileNames();
            if (fileNames != null) paths.AddRange(fileNames);
        }

        if (paths.Count > 0)
        {
            vm.HandleDroppedFiles(paths);
        }

        e.Handled = true;
    }

    // --- Drag-and-Drop: Outgoing (to external apps like Photoshop) ---

    private bool _suppressSelectionChange;
    private ExplorerItemViewModel? _pendingSelectionItem;

    public void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ExplorerItemViewModel item)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
               _dragStartPoint = e.GetPosition(this);
               _isDragging = false;
               
               var vm = DataContext as OutputExplorerViewModel;
               
               // Unified Split View selection:
               // When clicking an item in Split View without Ctrl/Shift,
               // clear ALL selections first so both panes behave as one view.
               if (vm != null && vm.IsSplitLayout && e.KeyModifiers == KeyModifiers.None && !vm.SelectedItems.Contains(item))
               {
                   vm.SelectedItems.Clear();
                   // Don't set Handled — let ListBox add the clicked item normally
               }
               
               // Fix for Multi-select drag:
               // If clicking an item that is ALREADY selected, and no modifiers (Ctrl/Shift) are pressed,
               // we must NOT let the ListBox handle this event yet. 
               // Because ListBox will immediately deselect everything else on MouseDown.
               // We should wait until MouseUp to deselect others (if no drag occurred).
               if (vm != null && vm.SelectedItems.Contains(item) && e.KeyModifiers == KeyModifiers.None)
               {
                   _suppressSelectionChange = true;
                   _pendingSelectionItem = item;
                   e.Handled = true; // Suppress ListBox handling
               }
               else
               {
                   _suppressSelectionChange = false;
                   _pendingSelectionItem = null;
               }
            }
        }
    }

    public async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint == null) return;
        if (_isDragging) return;

        try
        {
            var currentPoint = e.GetPosition(this);
            var diff = currentPoint - _dragStartPoint.Value;

            if (System.Math.Abs(diff.X) > DragThreshold || System.Math.Abs(diff.Y) > DragThreshold)
            {
                if (DataContext is not OutputExplorerViewModel vm) return;
                
                // Get the item currently being dragged
                ExplorerItemViewModel? draggedItem = null;
                if (sender is Control c && c.DataContext is ExplorerItemViewModel item)
                    draggedItem = item;

                if (draggedItem == null) return; 

                _isDragging = true;
                _dragStartPoint = null;

                var filePaths = vm.GetSelectedFilePaths();
                
                // If dragged item is not selected, force select it for the drag (or just use it)
                if (!filePaths.Contains(draggedItem.FullPath))
                {
                    filePaths = new System.Collections.Generic.List<string> { draggedItem.FullPath };
                }

                if (filePaths.Count == 0) return;

                var dataObject = new DataObject();
                var storageItems = new System.Collections.Generic.List<Avalonia.Platform.Storage.IStorageItem>();
                
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var storageProvider = desktop.MainWindow?.StorageProvider;
                    if (storageProvider != null)
                    {
                        foreach (var path in filePaths)
                        {
                            try
                            {
                                if (System.IO.Directory.Exists(path))
                                {
                                    var folder = await storageProvider.TryGetFolderFromPathAsync(new System.Uri(path));
                                    if (folder != null) storageItems.Add(folder);
                                }
                                else if (System.IO.File.Exists(path))
                                {
                                    var file = await storageProvider.TryGetFileFromPathAsync(new System.Uri(path));
                                    if (file != null) storageItems.Add(file);
                                }
                            }
                            catch { /* skip */ }
                        }
                    }
                }

                if (storageItems.Count > 0)
                    dataObject.Set(Avalonia.Input.DataFormats.Files, storageItems);
                
                dataObject.Set(Avalonia.Input.DataFormats.Text, string.Join("\n", filePaths));

                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
                _isDragging = false;
            }
        }
        catch (System.Exception ex)
        {
            _isDragging = false;
            _dragStartPoint = null;
            System.Diagnostics.Debug.WriteLine($"Drag Drop Error: {ex.Message}");
            // Optional: Show notification if notification service is available
            //But here we are in View, so avoiding direct service call unless routed.
        }
    }


    private void OnRenameTextBoxLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox tb)
        {
            // Handle Ctrl+A inside the text box so it doesn't Select All items
            if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                tb.SelectAll();
                e.Handled = true;
            }
        }
    }
    public void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_suppressSelectionChange && !_isDragging && _pendingSelectionItem != null)
        {
            if (DataContext is OutputExplorerViewModel vm)
            {
                vm.SelectedItems.Clear();
                vm.SelectedItems.Add(_pendingSelectionItem);
            }
        }
        _suppressSelectionChange = false;
        _pendingSelectionItem = null;
        _dragStartPoint = null;
        _isDragging = false;
    }

}

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
        this.PointerPressed += OnRootPointerPressed;
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
        PointerWheelChanged += OnRootPointerWheelChanged;

        // Register DragDrop handlers
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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
        TryAddKeyBinding(keyBindings, vm.ShortcutBackGesture, vm.GoBackCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutForwardGesture, vm.GoForwardCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutRenameGesture, vm.RenameItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutPermanentDeleteGesture, vm.PermanentDeleteItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutFocusSearchBoxGesture, vm.FocusSearchBoxCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutAddressBarGesture, vm.FocusPathBarCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutSwitchTabGesture, vm.SwitchTabCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutRefreshGesture, vm.RefreshCommand!, null, _explorerKeyBindings);
        // Standard shortcuts (not customizable via Settings) -> Now Customizable!
        TryAddKeyBinding(keyBindings, "Ctrl+A", vm.SelectAllCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCopyGesture, vm.CopyItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutCutGesture, vm.CutItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, vm.ShortcutPasteGesture, vm.PasteItemCommand!, null, _explorerKeyBindings);
        TryAddKeyBinding(keyBindings, "Back", vm.GoBackCommand!, null, _explorerKeyBindings);
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

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var context = this.DataContext as OutputExplorerViewModel;
        if (context == null) return;

        var props = e.GetCurrentPoint(this).Properties;

        // Deselect on empty area click (if not handled by list item)
        // Note: PointerPressed bubbles, but ListBoxItem usually handles it.
        // If we reach here, it likely means we clicked the background.
        // Only clear if Left click and no modifiers (to avoid clearing when context menu is requested or special actions)
        if (props.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None)
        {
             context.SelectedItems.Clear();
        }

        if (props.IsRightButtonPressed)
            context.UpdateClipboardStateAsync();
        if (props.IsXButton1Pressed)
        {
            if (context.GoBackCommand.CanExecute(null))
            {
                context.GoBackCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (context.GoForwardCommand.CanExecute(null))
            {
                context.GoForwardCommand.Execute(null);
                e.Handled = true;
            }
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

    public void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ExplorerItemViewModel)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
               _dragStartPoint = e.GetPosition(this);
               _isDragging = false;
               // Don't set Handled=true to allow ListBox selection logic to run
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

    public void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartPoint = null;
        _isDragging = false;
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
}

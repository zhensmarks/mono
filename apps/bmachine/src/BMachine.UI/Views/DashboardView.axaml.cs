using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using System;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DashboardView : UserControl
{
    private bool _isTogglingTerminal = false;
    private bool _isRestoringState = true; // Skip auto-close during initial restore
    private object? _lastClickedSourceItem;
    private object? _lastClickedOutputItem;
    private System.Collections.Generic.List<object>? _sourceSelectionBeforeContextMenu;
    private System.Collections.Generic.List<object>? _outputSelectionBeforeContextMenu;

    public DashboardView()
    {
        InitializeComponent();
        
        // Register InAppToast control for ToastNotificationService
        var toastControl = this.FindControl<Controls.InAppToast>("AppToast");
        if (toastControl != null)
        {
            Services.ToastNotificationService.Instance.RegisterToastControl(toastControl);
        }
        
        // Wire up Batch Drop Zone drag-drop handlers
        // handledEventsToo: drop harus tetap sampai ke handler walau ada kontrol di dalamnya
        // (TreeView/Border) yang ikut menangani event drag-drop.
        AddHandler(DragDrop.DropEvent, OnBatchDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnBatchDragOver);

        this.SizeChanged += OnDashboardSizeChanged;
        
        // Allow auto-close after initial layout settles (500ms delay)
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            _isRestoringState = false;
        }, TimeSpan.FromMilliseconds(1500));

        // Wire up TreeView multi-select (Shift/Ctrl) so Replace (Auto) from the
        // context menu can batch-process EVERY selected folder at once.
        var sourceTree = this.FindControl<TreeView>("SourceTreeView");
        if (sourceTree != null)
        {
            sourceTree.SelectionChanged += (s, e) =>
            {
                if (DataContext is DashboardViewModel dashVm)
                {
                    dashVm.BatchVM?.UpdateSelectedSourceItems(sourceTree.SelectedItems);
                }
            };
            sourceTree.PointerReleased += OnBatchTreePointerReleased;
            sourceTree.AddHandler(PointerPressedEvent, OnBatchTreePointerPressed, RoutingStrategies.Tunnel);
        }

        var outputTree = this.FindControl<TreeView>("OutputTreeView");
        if (outputTree != null)
        {
            outputTree.SelectionChanged += (s, e) =>
            {
                if (DataContext is DashboardViewModel dashVm)
                {
                    dashVm.BatchVM?.UpdateSelectedOutputItems(outputTree.SelectedItems);
                }
            };
            outputTree.PointerReleased += OnBatchTreePointerReleased;
            outputTree.AddHandler(PointerPressedEvent, OnBatchTreePointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private void OnBatchTreePointerPressed(object? sender, PointerEventArgs e)
    {
        // Right-clicking a TreeViewItem normally collapses multi-selection to the
        // clicked item before its ContextMenu command runs. Preserve it in Tunnel,
        // then restore after the native TreeView finishes handling the click.
        if (sender is not TreeView tree || !e.GetCurrentPoint(tree).Properties.IsRightButtonPressed)
            return;

        var selected = tree.SelectedItems?.Cast<object>().ToList();
        if (selected == null || selected.Count <= 1)
            return;

        if (tree.Name == "OutputTreeView")
            _outputSelectionBeforeContextMenu = selected;
        else if (tree.Name == "SourceTreeView")
            _sourceSelectionBeforeContextMenu = selected;
        else
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => RestoreBatchTreeSelection(tree),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    private void RestoreBatchTreeSelection(TreeView tree)
    {
        var saved = tree.Name == "OutputTreeView"
            ? _outputSelectionBeforeContextMenu
            : _sourceSelectionBeforeContextMenu;
        if (saved == null || saved.Count <= 1) return;

        tree.SelectedItems?.Clear();
        foreach (var item in saved)
            tree.SelectedItems?.Add(item);

        if (DataContext is DashboardViewModel dashVm && dashVm.BatchVM != null)
        {
            if (tree.Name == "OutputTreeView")
                dashVm.BatchVM.UpdateSelectedOutputItems(tree.SelectedItems!);
            else
                dashVm.BatchVM.UpdateSelectedSourceItems(tree.SelectedItems!);
        }
    }

    private void OnBatchTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Native TreeView applies Ctrl toggle after press. Snapshot here is therefore
        // the final selection used by a context-menu Replace (Auto) immediately after.
        if (sender is not TreeView tree || DataContext is not DashboardViewModel dashVm || dashVm.BatchVM == null)
            return;

        if (tree.Name == "OutputTreeView")
            dashVm.BatchVM.UpdateSelectedOutputItems(tree.SelectedItems);
        else if (tree.Name == "SourceTreeView")
            dashVm.BatchVM.UpdateSelectedSourceItems(tree.SelectedItems);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // FIX AV11 MACOS BUG: Force layout updates 100ms after attaching to visual tree.
        // This ensures the Dashboard conforms to the restored window size instead of being clipped.
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            this.InvalidateMeasure();
            this.InvalidateArrange();
            this.UpdateLayout();
        }, TimeSpan.FromMilliseconds(100));

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            UpdateNavDensityClasses(Bounds.Width);
        }, TimeSpan.FromMilliseconds(150));
    }

    private void OnDashboardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Keep the Log Panel available during compact resizes; only fit its visual width.
        if (_isTogglingTerminal || _isRestoringState) return;
        if (e.PreviousSize.Width <= 0) return; // Skip initial layout pass

        if (DataContext is DashboardViewModel vm && vm.IsLogPanelOpen)
            UpdateLogPanelMaxWidth(e.NewSize.Width);

        UpdateNavDensityClasses(e.NewSize.Width);
    }

    // Subset nav tab height/padding/font to the available width so buttons
    // never dominate small layouts (640x480 test VM) and stay stable on resize.
    private void UpdateNavDensityClasses(double hostWidth)
    {
        if (double.IsNaN(hostWidth) || double.IsInfinity(hostWidth) || hostWidth <= 0) return;
        var root = this.FindControl<Control>("Root");
        if (root is null) return;
        root.Classes.Set("NavXs", hostWidth < 760);
        root.Classes.Set("NavSm", hostWidth >= 760 && hostWidth < 1000);
    }

    private void UpdateLogPanelMaxWidth(double hostWidth)
    {
        if (double.IsNaN(hostWidth) || double.IsInfinity(hostWidth) || hostWidth <= 0) return;

        var logPanel = this.FindControl<Control>("LogPanel");
        if (logPanel is null) return;

        // Reserve at least 340px for the compact dashboard and its 6px splitter.
        logPanel.MaxWidth = Math.Min(600, Math.Max(180, hostWidth - 340));
    }

    private void OnTerminalToggleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            bool wasOpen = vm.IsLogPanelOpen;

            _isTogglingTerminal = true;
            if (!wasOpen)
                UpdateLogPanelMaxWidth(Bounds.Width);

            // Opening/closing a sidebar must not silently resize the user's window.
            vm.IsLogPanelOpen = !wasOpen;
            _isTogglingTerminal = false;
        }
        Part_ProfileNavButton.Flyout?.Hide();
    }

    private void OnViewProfileClick(object? sender, RoutedEventArgs e)
    {
        Part_ProfileNavButton.Flyout?.Hide();
    }
    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        Part_ProfileNavButton.Flyout?.Hide();
        if (DataContext is DashboardViewModel vm && vm.OpenLogoutDialogCommand.CanExecute(null))
        {
            vm.OpenLogoutDialogCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnBatchDragOver(object? sender, DragEventArgs e)
    {
        // Only accept folders
        e.DragEffects = e.Data.Contains(DataFormats.Files) 
            ? DragDropEffects.Copy 
            : DragDropEffects.None;
    }

    private void OnBatchDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not DashboardViewModel vm) return;
            if (vm.BatchVM == null) return;
            
            // Prevent Drop if Locker is Active
            if (vm.IsLockerTabSelected) return;

            var files = e.Data.GetFiles();
            if (files == null) return;

            var folderPaths = files
                .Where(f => f?.Path?.LocalPath != null)
                .Select(f => f.Path.LocalPath)
                .Where(p => !string.IsNullOrEmpty(p) && (System.IO.Directory.Exists(p) || p.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (folderPaths.Length > 0)
            {
                vm.BatchVM.AddFolders(folderPaths);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnBatchDrop: {ex.Message}\n{ex.StackTrace}");
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DashboardViewModel vm && vm.BatchVM != null)
        {
            // Unsubscribe to avoid duplicates if DataContext is reset
            vm.BatchVM.RequestMasterPathBrowse -= HandleRequestMasterPathBrowse;
            vm.BatchVM.RequestMasterPathBrowse += HandleRequestMasterPathBrowse;
            vm.BatchVM.RequestManualReplaceFolderBrowse -= HandleRequestManualReplaceFolderBrowse;
            vm.BatchVM.RequestManualReplaceFolderBrowse += HandleRequestManualReplaceFolderBrowse;
        }
    }

    private async System.Threading.Tasks.Task<string?> HandleRequestMasterPathBrowse()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Master Root Folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async System.Threading.Tasks.Task<string?> HandleRequestManualReplaceFolderBrowse(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private void OnReportBugClick(object? sender, RoutedEventArgs e)
    {
        Part_ProfileNavButton.Flyout?.Hide();
        var dialog = new Dialogs.MantraData.ReportBugDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            // Reflection fallback if Command property isn't efficient or just to be safe
            var method = vm.GetType().GetMethod("OpenSettingsCommand"); 
             // Actually vm.OpenSettingsCommand is an IRelayCommand property
             if (vm.OpenSettingsCommand.CanExecute(null))
             {
                 vm.OpenSettingsCommand.Execute(null);
                 Part_ProfileNavButton.Flyout?.Hide();
             }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"DataContext is {DataContext?.GetType().Name ?? "null"}");
        }
    }

    private async void OnLockClick(object? sender, RoutedEventArgs e)
    {
        var config = BMachine.Core.Security.FolderLockerConfig.Load();
        
        var dialog = new Window
        {
            Title = "Folder Locker",
            Width = config.WindowWidth > 0 ? config.WindowWidth : 540,
            Height = config.WindowHeight > 0 ? config.WindowHeight : 480,
            WindowStartupLocation = WindowStartupLocation.Manual,
            CanResize = true, // Allow resizing to save preference
            Content = new FolderLockerView
            {
                DataContext = new FolderLockerViewModel()
            }
        };

        // Restore Position if valid
        if (config.WindowX != -1 && config.WindowY != -1)
        {
            dialog.Position = new PixelPoint(config.WindowX, config.WindowY);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        // Save Position on Close
        dialog.Closing += (s, args) =>
        {
            var c = BMachine.Core.Security.FolderLockerConfig.Load(); // Reload to be safe
            c.WindowX = dialog.Position.X;
            c.WindowY = dialog.Position.Y;
            c.WindowWidth = (int)dialog.Width;
            c.WindowHeight = (int)dialog.Height;
            c.Save();
        };

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }

    private void OnEmbeddedViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Detect Right Click for Back Navigation
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (DataContext is DashboardViewModel vm)
            {
                vm.NavigateBack();
                e.Handled = true;
            }
        }
    }

    private void OnOverlayBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        // Close folder overlay when backdrop clicked
        if (DataContext is DashboardViewModel vm && vm.BatchVM != null)
        {
            if (vm.BatchVM.CloseFolderOverlayCommand?.CanExecute(null) == true)
            {
                vm.BatchVM.CloseFolderOverlayCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Visual);
        var mod = e.KeyModifiers;

        bool isShift = (mod & Avalonia.Input.KeyModifiers.Shift) != 0;
        bool isCtrl  = (mod & Avalonia.Input.KeyModifiers.Control) != 0;

        // --- Shift+click: range select manual (Avalonia Multiple tidak support ini) ---
        if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed && isShift && !isCtrl)
        {
            if (sender is Control ctl)
            {
                var tv = FindParentTreeView(ctl);
                if (tv != null && ctl.DataContext != null)
                {
                    bool isOutput = tv.Name == "OutputTreeView";
                    var items = tv.ItemsSource?.Cast<object>().ToList();
                    if (items != null && items.Count > 0)
                    {
                        var targetItem = ctl.DataContext;
                        var targetIdx = items.IndexOf(targetItem);

                        // Anchor: gunakan last clicked item bila ada, fallback ke item pertama yang selected
                        var lastItem = isOutput ? _lastClickedOutputItem : _lastClickedSourceItem;
                        int anchorIdx = (lastItem != null) ? items.IndexOf(lastItem) : -1;
                        if (anchorIdx < 0)
                        {
                            var currentSelected = tv.SelectedItems?.Cast<object>().ToList();
                            anchorIdx = currentSelected?.Count > 0 ? items.IndexOf(currentSelected[0]) : -1;
                        }

                        if (targetIdx >= 0 && anchorIdx >= 0 && anchorIdx != targetIdx)
                        {
                            int lo = Math.Min(anchorIdx, targetIdx);
                            int hi = Math.Max(anchorIdx, targetIdx);
                            tv.SelectedItems?.Clear();
                            for (int i = lo; i <= hi; i++)
                                tv.SelectedItems?.Add(items[i]);
                            e.Handled = true;

                            // Sync ke VM
                            if (DataContext is DashboardViewModel dashVm2 && dashVm2.BatchVM != null)
                            {
                                if (isOutput)
                                    dashVm2.BatchVM.UpdateSelectedOutputItems(tv.SelectedItems!);
                                else
                                    dashVm2.BatchVM.UpdateSelectedSourceItems(tv.SelectedItems!);
                            }
                            return;
                        }
                    }
                }
            }
            return;
        }

        // Ctrl+click: Avalonia Multiple handle toggle — jangan intercept, biarkan jalan.
        if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed && isCtrl)
        {
            if (sender is Control ctlCtrl && ctlCtrl.DataContext != null)
            {
                var tvCtrl = FindParentTreeView(ctlCtrl);
                if (tvCtrl?.Name == "OutputTreeView") _lastClickedOutputItem = ctlCtrl.DataContext;
                else _lastClickedSourceItem = ctlCtrl.DataContext;
            }
            return;
        }

        // Alt+click: biarkan juga
        if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed
            && (mod & Avalonia.Input.KeyModifiers.Alt) != 0)
            return;

        // Detect Double Left Click (Open Text - BatchNodeItem only)
        if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
        {
            if (sender is Control control && control.DataContext is BatchNodeItem item)
            {
                if (DataContext is DashboardViewModel vm)
                {
                    HandleDoubleTap(item, vm);
                    e.Handled = true;
                }
            }
        }
        // Detect Single Left Click (Copy Path + set MASTER target)
        else if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed)
        {
            if (sender is Control control)
            {
                var clickMod = e.KeyModifiers;
                bool isMultiModifier = (clickMod & Avalonia.Input.KeyModifiers.Control) != 0
                                    || (clickMod & Avalonia.Input.KeyModifiers.Shift) != 0;

                string? pathToCopy = null;
                object? batchItem = null;
                bool isOutputRootClick = false;

                if (control.DataContext is BatchNodeItem item)
                {
                    pathToCopy = item.FullPath;
                    batchItem = item;
                }
                else if (control.DataContext is BatchFolderRoot root)
                {
                    isOutputRootClick = string.Equals(control.Tag as string, "Output", System.StringComparison.OrdinalIgnoreCase);
                    pathToCopy = isOutputRootClick ? root.OutputPath : root.SourcePath;
                    batchItem = root;
                }

                // Update the batch selection so the Log Panel MASTER status bar
                // TARGET follows the clicked source/output folder.
                // TAPI: kalau Ctrl/Shift modifier aktif, JANGAN set SelectedBatchItem
                // karena itu reset TreeView selection ke 1 item saja (TwoWay binding).
                if (!isMultiModifier && batchItem != null && DataContext is DashboardViewModel dashVm && dashVm.BatchVM != null)
                {
                    dashVm.BatchVM.SelectBatchItemFromTree(batchItem, isOutputRootClick);
                    if (isOutputRootClick) _lastClickedOutputItem = batchItem;
                    else _lastClickedSourceItem = batchItem;
                }

                // Copy path to clipboard tapi JANGAN set e.Handled = true
                // agar TreeViewItem tetap menerima event dan ter-select secara visual.
                if (!isMultiModifier && !string.IsNullOrEmpty(pathToCopy))
                {
                    _ = CopyToClipboardAsync(pathToCopy);
                }
            }
        }
    }

    private async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
                System.Diagnostics.Debug.WriteLine($"[Clipboard] Copied: {text}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Clipboard] Error: {ex.Message}");
        }
    }

    /// <summary>Cari TreeView induk via Visual Tree (akurat & andal).</summary>
    private static TreeView? FindParentTreeView(Control? ctl)
    {
        return ctl?.FindAncestorOfType<TreeView>();
    }


    private void HandleDoubleTap(BatchNodeItem item, DashboardViewModel vm)
    {
        if (item.IsDirectory) return;
        
        string ext = System.IO.Path.GetExtension(item.FullPath).ToLower();
        if (string.IsNullOrEmpty(ext)) return;

        // Text Files -> Console
        string[] textExts = { ".txt", ".json", ".xml", ".log", ".md", ".jsx", ".js", ".csv", ".ini" };
        if (textExts.Contains(ext))
        {
             if (item.OpenTextCommand.CanExecute(null)) 
             {
                 item.OpenTextCommand.Execute(null);
                 vm.BatchVM.SelectedActivityMode = 0; // Switch to Console
             }
        }
        // Photoshop Files -> Photoshop
        else if (ext == ".psd" || ext == ".psb")
        {
             _ = vm.BatchVM.SendFileToPhotoshop(item.FullPath, item.Name);
        }
        // Images -> Default Viewer
        else if (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp" || ext == ".tiff" || ext == ".tif")
        {
             try 
             {
                 System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FullPath) { UseShellExecute = true });
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"Failed to open image: {ex.GetType().Name}");
             }
        }
    }

    // ===== LOG PANEL MANUAL RESIZE =====
    private bool _isResizingLogPanel = false;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;

    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizingLogPanel = true;
            _resizeStartPoint = e.GetPosition(this);
            if (DataContext is ViewModels.DashboardViewModel vm)
                _resizeStartWidth = vm.LogPanelWidth;
            else
            {
                var logPanel = this.FindControl<Control>("LogPanel");
                _resizeStartWidth = logPanel?.Bounds.Width ?? 280;
            }
            e.Pointer.Capture((IInputElement)sender!);
            e.Handled = true;
        }
    }

    private void OnResizeHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingLogPanel) return;

        if (DataContext is not ViewModels.DashboardViewModel vm) return;

        var currentPos = e.GetPosition(this);
        double delta = _resizeStartPoint.X - currentPos.X;
        double maxWidth = Math.Max(180, Math.Min(600, Bounds.Width - 340));
        double newWidth = Math.Clamp(_resizeStartWidth + delta, 180, maxWidth);
        vm.LogPanelWidth = newWidth;
        e.Handled = true;
    }

    private void OnResizeHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizingLogPanel)
        {
            _isResizingLogPanel = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}

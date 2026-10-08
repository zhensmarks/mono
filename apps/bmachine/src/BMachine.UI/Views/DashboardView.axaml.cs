using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.ViewModels;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DashboardView : UserControl
{
    private bool _isTogglingTerminal = false;
    private bool _isRestoringState = true; // Skip auto-close during initial restore

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
        AddHandler(DragDrop.DropEvent, OnBatchDrop);
        AddHandler(DragDrop.DragOverEvent, OnBatchDragOver);

        this.SizeChanged += OnDashboardSizeChanged;
        
        // Allow auto-close after initial layout settles (500ms delay)
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            _isRestoringState = false;
        }, TimeSpan.FromMilliseconds(1500));
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
    }

    private void OnDashboardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Keep the Log Panel available during compact resizes; only fit its visual width.
        if (_isTogglingTerminal || _isRestoringState) return;
        if (e.PreviousSize.Width <= 0) return; // Skip initial layout pass

        if (DataContext is DashboardViewModel vm && vm.IsLogPanelOpen)
            UpdateLogPanelMaxWidth(e.NewSize.Width);
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

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Visual);

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
                if (batchItem != null && DataContext is DashboardViewModel dashVm && dashVm.BatchVM != null)
                {
                    dashVm.BatchVM.SelectBatchItemFromTree(batchItem, isOutputRootClick);
                }

                if (!string.IsNullOrEmpty(pathToCopy))
                {
                    _ = CopyToClipboardAsync(pathToCopy);
                    e.Handled = true;
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

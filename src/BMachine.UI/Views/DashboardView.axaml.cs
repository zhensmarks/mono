using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.ViewModels;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DashboardView : UserControl
{
    private double _previousWidth = 0;
    private bool _isTogglingTerminal = false;
    private bool _isRestoringState = true; // Skip auto-close during initial restore

    public DashboardView()
    {
        InitializeComponent();
        
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
        // Skip auto-close if we are mid-toggle, restoring state, or on initial layout
        if (_isTogglingTerminal || _isRestoringState) return;
        if (e.PreviousSize.Width <= 0) return; // Skip initial layout pass

        if (DataContext is DashboardViewModel vm)
        {
            // Auto close if window is manually resized smaller than 800
            if (vm.IsLogPanelOpen && e.NewSize.Width < 800 && e.NewSize.Width < e.PreviousSize.Width)
            {
                vm.IsLogPanelOpen = false;
                // Also shrink the window back
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window != null && window.WindowState == WindowState.Normal)
                {
                    double curW = window.Bounds.Width;
                    if (curW > 520) window.Width = Math.Max(520, curW - 279);
                }
            }

            _previousWidth = e.NewSize.Width;
        }
    }

    private void OnTerminalToggleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            bool wasOpen = vm.IsLogPanelOpen;
            
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null && window.WindowState == WindowState.Normal)
            {
                double currentWidth = window.Bounds.Width;
                if (currentWidth <= 0 || double.IsNaN(currentWidth))
                    currentWidth = window.Width;

                _isTogglingTerminal = true;

                if (!wasOpen) // Opening: expand window FIRST, then show panel
                {
                    window.Width = currentWidth + 279;
                    // Show panel on next layout pass so the window has already expanded
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.IsLogPanelOpen = true;
                        _isTogglingTerminal = false;
                    }, Avalonia.Threading.DispatcherPriority.Render);
                }
                else // Closing: hide panel first, then shrink window
                {
                    vm.IsLogPanelOpen = false;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        double w = window.Bounds.Width;
                        window.Width = Math.Max(520, w - 279);
                        _isTogglingTerminal = false;
                    }, Avalonia.Threading.DispatcherPriority.Render);
                }
            }
            else
            {
                // Maximized or window not found, just toggle
                vm.IsLogPanelOpen = !wasOpen;
            }
        }
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
    private void OnPixelcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;
        
        var win = new PixelcutWindow
        {
             WindowStartupLocation = WindowStartupLocation.CenterOwner,
             DataContext = new PixelcutViewModel(vm.Database)
        };
        
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) win.Show(owner);
        else win.Show();
    }

    private void OnGdriveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;

        var win = new GdriveWindow
        {
             WindowStartupLocation = WindowStartupLocation.CenterOwner,
             DataContext = new GdriveViewModel(vm.Database)
        };
        
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) win.Show(owner);
        else win.Show();
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

        // Detect Right Click (Expand/Collapse)
        if (point.Properties.IsRightButtonPressed)
        {
            if (sender is Control control)
            {
                if (control.DataContext is BatchNodeItem item)
                {
                    if (item.ExpandCommand.CanExecute(null))
                    {
                        item.ExpandCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (control.DataContext is BatchFolderRoot root)
                {
                    if (root.ExpandCommand.CanExecute(null))
                    {
                        root.ExpandCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }
        // Detect Double Left Click (Open Text - BatchNodeItem only)
        else if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
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
        // Detect Single Left Click (Copy Path)
        else if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed)
        {
            if (sender is Control control)
            {
                if (control.DataContext is BatchNodeItem item)
                {
                    if (item.CopyPathCommand.CanExecute(null))
                    {
                        item.CopyPathCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (control.DataContext is BatchFolderRoot root)
                {
                    if (root.CopyPathCommand.CanExecute(null))
                    {
                        root.CopyPathCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
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
                 System.Diagnostics.Debug.WriteLine($"Failed to open image: {ex.Message}");
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
        double newWidth = Math.Max(180, Math.Min(600, _resizeStartWidth + delta));
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

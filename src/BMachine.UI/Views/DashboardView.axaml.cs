using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.ViewModels;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        
        // Wire up Batch Drop Zone drag-drop handlers
        AddHandler(DragDrop.DropEvent, OnBatchDrop);
        AddHandler(DragDrop.DragOverEvent, OnBatchDragOver);
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
                .Where(p => !string.IsNullOrEmpty(p) && System.IO.Directory.Exists(p))
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

        // Detect Right Click (Expand)
        if (point.Properties.IsRightButtonPressed)
        {
            if (sender is Control control && control.DataContext is BatchNodeItem item)
            {
                if (item.ExpandCommand.CanExecute(null))
                {
                    item.ExpandCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
        // Detect Double Left Click (Open Text)
        else if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
        {
            if (sender is Control control && control.DataContext is BatchNodeItem item)
            {
                if (item.OpenTextCommand.CanExecute(null))
                {
                    item.OpenTextCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}

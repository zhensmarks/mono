using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using BMachine.UI.ViewModels;
using Avalonia.VisualTree;

namespace BMachine.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        
        // Wire DragDrop handlers for script drop zones
        var masterZone = this.FindControl<Grid>("MasterDropZone");
        var actionZone = this.FindControl<Grid>("ActionDropZone");
        var othersZone = this.FindControl<Grid>("OthersDropZone");
        
        if (masterZone != null)
        {
            masterZone.AddHandler(DragDrop.DragOverEvent, OnScriptDragOver);
            masterZone.AddHandler(DragDrop.DropEvent, OnScriptDrop);
        }
        if (actionZone != null)
        {
            actionZone.AddHandler(DragDrop.DragOverEvent, OnScriptDragOver);
            actionZone.AddHandler(DragDrop.DropEvent, OnScriptDrop);
        }
        if (othersZone != null)
        {
            othersZone.AddHandler(DragDrop.DragOverEvent, OnScriptDragOver);
            othersZone.AddHandler(DragDrop.DropEvent, OnScriptDrop);
        }
        
        // Handle PointerPressed (Right-Click) at Tunnel phase to ensure we catch it before controls swallow it
        // This fixes the issue where right-clicking an item selects it instead of going back.
        this.AddHandler(PointerPressedEvent, OnRootPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.PickExtensionFileFunc = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;
                
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Extension DLL",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("DLL Plugin") { Patterns = new[] { "*.dll" } } }
                });
                
                return files.Count > 0 ? files[0].Path.LocalPath : null;
            };

            vm.PickScriptFileFunc = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;
                
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Script File",
                    AllowMultiple = false
                });
                
                return files.Count > 0 ? files[0].Path.LocalPath : null;
            };

            vm.OpenAvatarSelectionRequested += () => OpenAvatarSelection(vm);
        }
    }

    private async void OnBrowseCredsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Google Credentials JSON",
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } }
        });

        if (files.Count > 0 && DataContext is SettingsViewModel vm)
        {
            vm.GoogleCredsPath = files[0].Path.LocalPath;
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            if (DataContext is SettingsViewModel vm)
            {
                // If in Mobile View and Content is Open, Close Content (Back)
                if (vm.IsMobileView && vm.IsMobileContentOpen)
                {
                    vm.IsMobileContentOpen = false;
                    e.Handled = true;
                    return;
                }
                
                // Otherwise normal GoBack (Back to Dashboard)
                if (vm.GoBackCommand.CanExecute(null))
                {
                    vm.GoBackCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
             if (DataContext is SettingsViewModel vm)
             {
                 if (vm.IsMobileView && vm.IsMobileContentOpen)
                 {
                     vm.IsMobileContentOpen = false;
                     e.Handled = true;
                     return;
                 }
                 
                 if (vm.GoBackCommand.CanExecute(null))
                 {
                     vm.GoBackCommand.Execute(null);
                     e.Handled = true;
                 }
             }
        }
    }

    // --- Drag-and-Drop for Scripts ---
    private void OnScriptDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnScriptDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not SettingsViewModel vm) return;

        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        // Determine target type based on sender's Tag
        string targetType = (sender as Control)?.Tag?.ToString() ?? "Others";

        string targetSubDir = targetType switch
        {
            "Master" => "Master",
            "Action" => "Action",
            _ => "Others"
        };

        var targetDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", targetSubDir);
        if (!System.IO.Directory.Exists(targetDir)) System.IO.Directory.CreateDirectory(targetDir);

        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            var dest = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(path));
            try
            {
                System.IO.File.Copy(path, dest, overwrite: true);
            }
            catch { }
        }

        vm.LoadAllScripts();
        vm.StatusMessage = $"{files.Count} file(s) added to {targetType}!";
        vm.IsStatusVisible = true;
        await System.Threading.Tasks.Task.Delay(2000);
        vm.IsStatusVisible = false;
    }

    // --- Enter Key to Confirm Rename ---
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return && e.Key != Key.Enter) return;
        
        if (sender is TextBox tb && tb.DataContext is ScriptItem item)
        {
            if (item.SaveRenameCommand.CanExecute(null))
            {
                item.SaveRenameCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private async void OpenAvatarSelection(SettingsViewModel settingVm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window parentWindow) return;

        var avatarVm = new AvatarSelectionViewModel();
        
        avatarVm.PickCustomImageFunc = async () =>
        {
             var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
             {
                 Title = "Select Avatar Image",
                 AllowMultiple = false,
                 FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
             });
             return files.Count > 0 ? files[0].Path.LocalPath : null;
        };

        avatarVm.OnAvatarSelected += (source) =>
        {
            settingVm.AvatarSource = source;
            // Trigger preview update if needed
        };
        
        var dialog = new AvatarSelectionWindow
        {
            DataContext = avatarVm
        };
        
        avatarVm.OnAvatarSelected += _ => dialog.Close();
        avatarVm.OnCancel += () => dialog.Close();

        await dialog.ShowDialog(parentWindow);
    }
    private void OnRootGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid) return;
        if (DataContext is not SettingsViewModel vm) return;
        
        var sidebar = this.FindControl<Control>("Part_Sidebar");
        var contentWrapper = this.FindControl<Control>("Part_ContentWrapper");
        
        if (sidebar == null || contentWrapper == null) return;

        double threshold = 600;
        bool isMobile = e.NewSize.Width < threshold;

        vm.IsMobileView = isMobile;

        if (isMobile)
        {
            // Mobile: Single Column, Overlay Logic
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            
            // Sidebar in Col 0 (Full Width)
            Grid.SetColumn(sidebar, 0);
            sidebar.Margin = new Thickness(0);

            // Content in Col 0 (Full Width)
            Grid.SetColumn(contentWrapper, 0);
        }
        else
        {
            // Desktop: Side by Side
            grid.ColumnDefinitions = new ColumnDefinitions("300, *");
            
            // Sidebar in Col 0 with margin
            Grid.SetColumn(sidebar, 0);
            sidebar.Margin = new Thickness(0, 0, 30, 0);

            // Content in Col 1
            Grid.SetColumn(contentWrapper, 1);
        }
    }

    // --- Drag-and-Drop for Script Reordering ---

    // --- Drag-and-Drop for Script Reordering ---

    private bool _isDown = false;
    private Point _dragStartPoint;

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var point = e.GetCurrentPoint(listBox);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDown = true;
            _dragStartPoint = point.Position;
            // Do NOT Handle here, let it tunnel/bubble so selection works
        }
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDown) return;
        if (sender is not ListBox listBox) return;

        var point = e.GetCurrentPoint(listBox);
        if (!point.Properties.IsLeftButtonPressed) 
        {
            _isDown = false;
            return;
        }

        // Check drag threshold (e.g. 10 pixels)
        var diff = _dragStartPoint - point.Position;
        if (Math.Abs(diff.X) > 10 || Math.Abs(diff.Y) > 10)
        {
            _isDown = false; // Stop tracking, start drag

            var visual = listBox.InputHitTest(_dragStartPoint) as Visual;
            var itemContainer = visual?.FindAncestorOfType<ListBoxItem>();
            
            if (itemContainer?.DataContext is ScriptOrderItem scriptItem)
            {
                 var data = new DataObject();
                 data.Set("ScriptItem", scriptItem);
                 data.Set("SourceList", listBox.Name);

                 // Use DragDropEffects.Move
                 await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDown = false;
    }

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        
        if (e.Data.Contains("ScriptItem") && sender is ListBox targetList)
        {
            // Verify Source matches Target to prevent moving across lists
            var sourceListName = e.Data.Get("SourceList") as string;
            if (sourceListName == targetList.Name)
            {
                e.DragEffects = DragDropEffects.Move;
            }
        }
    }

    private void OnListDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox targetList) return;
        if (DataContext is not SettingsViewModel vm) return;
        
        if (e.Data.Contains("ScriptItem") && e.Data.Get("ScriptItem") is ScriptOrderItem scriptItem)
        {
            var point = e.GetPosition(targetList);
            int newIndex = GetDropIndex(targetList, point);
            
            if (newIndex >= 0)
            {
                if (targetList.Name == "MasterListBox")
                {
                    vm.MoveMasterScript(scriptItem, newIndex);
                }
                else if (targetList.Name == "ActionListBox")
                {
                    vm.MoveActionScript(scriptItem, newIndex);
                }
            }
        }
    }

    private int GetDropIndex(ListBox list, Point point)
    {
        // Simple hit testing to find which item we are over
        var visual = list.InputHitTest(point) as Visual;
        var itemContainer = visual?.FindAncestorOfType<ListBoxItem>();
        
        if (itemContainer != null)
        {
            return list.IndexFromContainer(itemContainer);
        }
        
        // If not over an item, check if at bottom
        return list.ItemCount - 1; 
    }
}

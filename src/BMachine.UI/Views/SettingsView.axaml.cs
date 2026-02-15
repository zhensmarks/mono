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
        // 1. Check for Recording Mode FIRST
        if (DataContext is SettingsViewModel vm && vm.IsRecordingShortcut)
        {
            var props = e.GetCurrentPoint(this).Properties;
            
            var trigger = new BMachine.UI.Models.TriggerConfig
            {
                Type = BMachine.UI.Models.TriggerType.Mouse,
                Modifiers = (int)e.KeyModifiers
            };

            // 0=Left, 1=Right, 2=Middle, 3=X1, 4=X2
            if (props.IsLeftButtonPressed) trigger.MouseButton = 0;
            else if (props.IsRightButtonPressed) trigger.MouseButton = 1;
            else if (props.IsMiddleButtonPressed) trigger.MouseButton = 2;
            else if (props.IsXButton1Pressed) trigger.MouseButton = 3;
            else if (props.IsXButton2Pressed) trigger.MouseButton = 4;
            else return; 

            e.Handled = true;
            vm.OnShortcutRecorded(new Messages.TriggerRecordedMessage(trigger));
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            if (DataContext is SettingsViewModel navVm)
            {
                // If in Mobile View and Content is Open, Close Content (Back)
                if (navVm.IsMobileView && navVm.IsMobileContentOpen)
                {
                    navVm.IsMobileContentOpen = false;
                    e.Handled = true;
                    return;
                }
                
                // Otherwise normal GoBack (Back to Dashboard)
                if (navVm.GoBackCommand.CanExecute(null))
                {
                    navVm.GoBackCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // 1. Check for Recording Mode
        if (DataContext is SettingsViewModel vm && vm.IsRecordingShortcut)
        {
            e.Handled = true;
            
            // Ignore Modifier keys being pressed alone
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            var sharpKey = MapAvaloniaKeyToSharpHook(e.Key);
            if (sharpKey == 0) return; // Unknown key

            var trigger = new BMachine.UI.Models.TriggerConfig
            {
                Type = BMachine.UI.Models.TriggerType.Keyboard,
                Key = sharpKey,
                Modifiers = (int)e.KeyModifiers
            };

            vm.OnShortcutRecorded(new Messages.TriggerRecordedMessage(trigger));
            return;
        }

        if (e.Key == Key.Escape)
        {
             if (DataContext is SettingsViewModel navVm)
             {
                 if (navVm.IsMobileView && navVm.IsMobileContentOpen)
                 {
                     navVm.IsMobileContentOpen = false;
                     e.Handled = true;
                     return;
                 }
                 
                 if (navVm.GoBackCommand.CanExecute(null))
                 {
                     navVm.GoBackCommand.Execute(null);
                     e.Handled = true;
                 }
             }
        }
    }

    private int MapAvaloniaKeyToSharpHook(Key key)
    {
        // SharpHook KeyCodes (from SharpHook.Native.KeyCode)
        // Verified against SharpHook documentation/source
        return key switch
        {
            Key.Escape => 1,
            Key.F1 => 59, Key.F2 => 60, Key.F3 => 61, Key.F4 => 62,
            Key.F5 => 63, Key.F6 => 64, Key.F7 => 65, Key.F8 => 66,
            Key.F9 => 67, Key.F10 => 68, Key.F11 => 87, Key.F12 => 88,
            Key.D0 => 11, Key.D1 => 2, Key.D2 => 3, Key.D3 => 4, Key.D4 => 5,
            Key.D5 => 6, Key.D6 => 7, Key.D7 => 8, Key.D8 => 9, Key.D9 => 10,
            Key.Back => 14, Key.Tab => 15, Key.Enter => 28, Key.Space => 57,
            Key.A => 30, Key.B => 48, Key.C => 46, Key.D => 32, Key.E => 18,
            Key.F => 33, Key.G => 34, Key.H => 35, Key.I => 23, Key.J => 36,
            Key.K => 37, Key.L => 38, Key.M => 50, Key.N => 49, Key.O => 24,
            Key.P => 25, Key.Q => 16, Key.R => 19, Key.S => 31, Key.T => 20,
            Key.U => 22, Key.V => 47, Key.W => 17, Key.X => 45, Key.Y => 21, Key.Z => 44,
            Key.Left => 57419, Key.Right => 57421, Key.Up => 57416, Key.Down => 57424,
            Key.Delete => 57427, Key.Home => 57415, Key.End => 57423,
            Key.PageUp => 57417, Key.PageDown => 57425,
            Key.OemTilde => 41, // `
            _ => 0
        };
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

        await vm.LoadAllScriptsAsync();
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

    public async void OnHamburgerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ScriptItem item) return;

        // Prevent click from bubbling if we want to consume it, but for DnD we usually don't need to consume until drag starts.
        // However, since this is a drag handle, we can start drag immediately.
        
        var dragData = new DataObject();
        dragData.Set("ScriptItem", item);
        dragData.Set("OriginType", "Script"); // Origin check is handled by Drop handler via Contains 
        // We can infer IsMaster from the collection it belongs to, but ScriptItem doesn't know. 
        // Actually, we can check the parent ItemsControl? Harder.
        // Easier: The Drop handler checks if Source Item matches Target Collection type.
        // Let's just pass the item.
        
        // Visual trick: To prevent the pointer press from being "click", we might invalid command execution.
        // But the hamburger has no command.
        
        // Start Drag
        // Use "Move" effect
        var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
    }

    public void OnItemDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        
        if (e.Data.Contains("ScriptItem") && sender is Control targetControl && targetControl.DataContext is ScriptItem targetItem)
        {
             // Check if source and target are compatible
             // We can check if the Target Control Tag matches the Source Item's inferred type?
             // Since we don't know Source Item's type easily without reference, let's assume if they are in the same list they are compatible.
             // But we need to prevent dragging Master to Action.
             
             // Simplest: Check if the dragged item exists in the collection associated with the target list?
             // Access ViewModel
             if (DataContext is SettingsViewModel vm)
             {
                 var draggedItem = e.Data.Get("ScriptItem") as ScriptItem;
                 if (draggedItem == null) return;
                 
                 // Check Tag of target control to know which list we are hovering
                 string targetType = targetControl.Tag?.ToString() ?? "";
                 bool isTargetMaster = targetType == "Master";
                 
                 // Verify Dragged Item belongs to the Target Collection
                 bool belongs = isTargetMaster 
                     ? vm.MasterScripts.Contains(draggedItem) 
                     : vm.ActionScripts.Contains(draggedItem);
                     
                 if (belongs && draggedItem != targetItem)
                 {
                     e.DragEffects = DragDropEffects.Move;
                 }
             }
        }
    }

    public void OnItemDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control targetControl || 
            targetControl.DataContext is not ScriptItem targetItem ||
            DataContext is not SettingsViewModel vm) return;

        if (e.Data.Contains("ScriptItem") && e.Data.Get("ScriptItem") is ScriptItem draggedItem)
        {
             string targetType = targetControl.Tag?.ToString() ?? "";
             bool isTargetMaster = targetType == "Master";
             
             // Verify again
             bool belongs = isTargetMaster 
                 ? vm.MasterScripts.Contains(draggedItem) 
                 : vm.ActionScripts.Contains(draggedItem);

             if (belongs)
             {
                 var collection = isTargetMaster ? vm.MasterScripts : vm.ActionScripts;
                 int newIndex = collection.IndexOf(targetItem);
                 
                 if (newIndex >= 0)
                 {
                     vm.MoveScript(draggedItem, newIndex, isTargetMaster);
                 }
             }
        }
    }

    private void AutoCompleteBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox acb)
        {
            acb.IsDropDownOpen = true;
        }
    }
}

// Extension helper logic not strictly needed if we do the Contains check.

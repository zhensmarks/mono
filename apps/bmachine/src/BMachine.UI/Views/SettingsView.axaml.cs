using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using BMachine.UI.ViewModels;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace BMachine.UI.Views;

public partial class SettingsView : UserControl
{
    private bool _hasAppliedResponsiveLayout;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnViewLoaded;
        
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
        this.AddHandler(PointerPressedEvent, OnRadialRootPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        this.AddHandler(PointerMovedEvent, OnRadialRootPointerMoved, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        this.AddHandler(PointerReleasedEvent, OnRadialRootPointerReleased, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
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
            // Mode edit radial: layout baris (roda + panel detail) dihitung ulang saat panel
            // dibuka/ditutup, supaya panel tidak pernah keluar dari lebar jendela.
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.IsSlotDetailsOpen))
                    Avalonia.Threading.Dispatcher.UIThread.Post(UpdateRadialRowLayout);
            };
            vm.IsMobileContentOpen = false;
            _hasAppliedResponsiveLayout = false;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(DataContext, vm) && this.FindControl<Grid>("Part_RootGrid") is { } rootGrid)
                    ApplyResponsiveLayout(rootGrid, Math.Max(rootGrid.Bounds.Width, Bounds.Width));
            });
        }
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Grid>("Part_RootGrid") is not { } rootGrid)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            ApplyResponsiveLayout(rootGrid, Math.Max(rootGrid.Bounds.Width, Bounds.Width)));
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
        var sectionList = this.FindControl<ListBox>("SettingsSectionList");
        if (properties.IsLeftButtonPressed && DataContext is SettingsViewModel compactVm && compactVm.IsMobileView && sectionList is { IsVisible: true, IsHitTestVisible: true })
        {
            var listPoint = e.GetPosition(sectionList);
            if (listPoint.X >= 0 && listPoint.Y >= 0 && listPoint.X < sectionList.Bounds.Width && listPoint.Y < sectionList.Bounds.Height)
            {
                var sectionItems = new[]
                {
                    this.FindControl<ListBoxItem>("SettingsItemGeneral"),
                    this.FindControl<ListBoxItem>("SettingsItemAppearance"),
                    this.FindControl<ListBoxItem>("SettingsItemAccount"),
                    this.FindControl<ListBoxItem>("SettingsItemExtensions"),
                    this.FindControl<ListBoxItem>("SettingsItemScriptManager"),
                    this.FindControl<ListBoxItem>("SettingsItemPaths"),
                    this.FindControl<ListBoxItem>("SettingsItemAbout")
                };

                for (var index = 0; index < sectionItems.Length; index++)
                {
                    var candidate = sectionItems[index];
                    if (candidate is null || !candidate.IsVisible || !candidate.IsHitTestVisible)
                        continue;

                    var itemPoint = e.GetPosition(candidate);
                    if (itemPoint.X < 0 || itemPoint.Y < 0 || itemPoint.X > candidate.Bounds.Width || itemPoint.Y > candidate.Bounds.Height)
                        continue;

                    compactVm.SelectedMenuIndex = index;
                    compactVm.IsMobileContentOpen = true;
                    InvalidateSettingsSurface();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (properties.IsRightButtonPressed)
        {
            if (DataContext is SettingsViewModel navVm)
            {
                // If in Mobile View and Content is Open, Close Content (Back)
                if (navVm.IsMobileView && navVm.IsMobileContentOpen)
                {
                    navVm.IsMobileContentOpen = false;
                    InvalidateSettingsSurface();
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

    private void OnMobileBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.IsMobileView)
            vm.IsMobileContentOpen = false;

        InvalidateSettingsSurface();
        e.Handled = true;
    }

    private void InvalidateSettingsSurface()
    {
        if (this.FindControl<Grid>("Part_RootGrid") is { } rootGrid)
            rootGrid.InvalidateVisual();

        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Esc menutup mode edit radial (panel SLOT DETAILS) SEBELUM handler lain
        // (mis. KeyBinding Escape di DashboardView) sempat jalan.
        if (e.Key == Avalonia.Input.Key.Escape && DataContext is SettingsViewModel escVm && escVm.IsSlotDetailsOpen)
        {
            escVm.CloseSlotDetailsCommand.Execute(null);
            e.Handled = true;
            return;
        }

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
    private void OnSettingsMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || !vm.IsMobileView || e.Source is not Control source)
            return;

        if (source is ListBoxItem || source.FindAncestorOfType<ListBoxItem>() is not null)
        {
            vm.IsMobileContentOpen = true;
            InvalidateSettingsSurface();
        }
    }

    private void OnRootGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Grid grid)
            ApplyResponsiveLayout(grid, e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(Grid grid, double width)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var sidebar = this.FindControl<Control>("Part_Sidebar");
        var contentWrapper = this.FindControl<Control>("Part_ContentWrapper");
        if (sidebar == null || contentWrapper == null) return;

        if (width <= 0)
            width = Math.Max(grid.Bounds.Width, Bounds.Width);
        if (width <= 0) return;

        var wasMobile = vm.IsMobileView;
        const double threshold = 800;
        bool isMobile = width < threshold;
        vm.IsMobileView = isMobile;

        if (isMobile)
        {
            // Keep a page visible when a laid-out desktop view crosses into compact mode;
            // the first compact layout still opens on the section list.
            if (!wasMobile)
                vm.IsMobileContentOpen = _hasAppliedResponsiveLayout && vm.SelectedMenuIndex >= 0;

            grid.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetColumn(sidebar, 0);
            sidebar.Margin = new Thickness(0);
            Grid.SetColumn(contentWrapper, 0);
        }
        else
        {
            // Desktop keeps both panes visible and hides the compact-only back affordance.
            vm.IsMobileContentOpen = false;
            grid.ColumnDefinitions = new ColumnDefinitions("300, *");
            Grid.SetColumn(sidebar, 0);
            sidebar.Margin = new Thickness(0, 0, 30, 0);
            Grid.SetColumn(contentWrapper, 1);
        }

        _hasAppliedResponsiveLayout = true;
        UpdateRadialRowLayout();
        InvalidateSettingsSurface();
    }

    /// <summary>
    /// Layout baris radial (roda + panel SLOT DETAILS) dihitung dari lebar nyata:
    /// - panel tertutup  -> roda di TENGAH
    /// - panel terbuka & lebar cukup -> roda kiri + panel kanan, kolom panel "*" (fleksibel)
    ///   sehingga panel ikut menyusut dan TIDAK pernah terpotong di tepi jendela
    /// - panel terbuka tapi terlalu sempit -> panel turun ke baris bawah (fallback)
    /// </summary>
    private void UpdateRadialRowLayout()
    {
        var row = this.FindControl<Grid>("RadialRowGrid");
        var wheel = this.FindControl<Control>("RadialWheelCard");
        var details = this.FindControl<Control>("RadialDetailsCard");
        if (row == null || wheel == null || details == null) return;

        bool open = DataContext is SettingsViewModel vm && vm.IsSlotDetailsOpen;
        double avail = row.Bounds.Width;
        if (avail <= 0) avail = Math.Max(Bounds.Width - 330, 0);
        if (avail <= 0) return;

        const double wheelW = 234;   // 200 (roda) + padding 16*2 + border 2
        const double panelMin = 280; // MinWidth panel detail
        const double gap = 16;

        if (!open)
        {
            row.ColumnDefinitions = new ColumnDefinitions("*, Auto, *");
            row.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(wheel, 1);
            Grid.SetColumn(details, 2);
        }
        else if (avail >= wheelW + gap + panelMin + 8)
        {
            row.ColumnDefinitions = new ColumnDefinitions("Auto, *");
            row.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(wheel, 0);
            Grid.SetColumn(details, 1);
        }
        else
        {
            row.ColumnDefinitions = new ColumnDefinitions("*");
            row.RowDefinitions = new RowDefinitions("Auto, Auto");
            Grid.SetColumn(wheel, 0);
            Grid.SetColumn(details, 0);
            Grid.SetRow(details, 1);
        }

        Grid.SetRow(wheel, 0);
        wheel.Margin = new Thickness(0, 0, gap, 16);
        details.Margin = new Thickness(0, 0, 0, 16);
    }

    // --- Drag-and-Drop for Script Reordering ---

    private ScriptItem? _pressedMasterScript;
    private Point? _masterScriptPressPoint;
    private Control? _masterDropTarget;
    private bool _masterDropBefore;
    private bool _masterDragActive;

    public void OnHamburgerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ScriptItem item ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        _pressedMasterScript = item;
        _masterScriptPressPoint = e.GetPosition(this);
        e.Pointer.Capture(control);
    }

    public async void OnHamburgerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_masterScriptPressPoint is not Point start || _pressedMasterScript is null || sender is not Control control)
            return;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - start.X) < 6 && Math.Abs(position.Y - start.Y) < 6)
            return;

        var draggedItem = _pressedMasterScript;
        _pressedMasterScript = null;
        _masterScriptPressPoint = null;
        e.Pointer.Capture(null);

        var dragData = new DataObject();
        dragData.Set("ScriptItem", draggedItem);
        _masterDragActive = true;
        var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
        _masterDragActive = false;

        // Windows/OLE kadang tidak mengirim Drop walau target masih aktif saat mouse dilepas.
        if (result == DragDropEffects.None && _masterDropTarget?.DataContext is ScriptItem targetItem &&
            DataContext is SettingsViewModel vm && vm.MasterScripts.Contains(draggedItem))
        {
            MoveMasterScriptToTarget(vm, draggedItem, targetItem, _masterDropBefore);
        }

        ClearMasterDropIndicator();
    }

    public void OnHamburgerPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearMasterDragCandidate(e);

    public void OnHamburgerPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_masterDragActive)
            ClearMasterDragCandidate();
    }

    private void ClearMasterDragCandidate(PointerReleasedEventArgs? e = null)
    {
        e?.Pointer.Capture(null);
        _pressedMasterScript = null;
        _masterScriptPressPoint = null;
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
                     // Arah drag menentukan sisi celah tempat item disisipkan.
                     var before = vm.MasterScripts.IndexOf(draggedItem) > vm.MasterScripts.IndexOf(targetItem);
                     ShowMasterDropIndicator(targetControl, before);
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
                 if (isTargetMaster)
                     MoveMasterScriptToTarget(vm, draggedItem, targetItem, _masterDropBefore);
                 else
                     vm.MoveScript(draggedItem, vm.ActionScripts.IndexOf(targetItem), false);
             }
        }

        ClearMasterDropIndicator();
    }

    private void MoveMasterScriptToTarget(SettingsViewModel vm, ScriptItem draggedItem, ScriptItem targetItem, bool before)
    {
        var oldIndex = vm.MasterScripts.IndexOf(draggedItem);
        var targetIndex = vm.MasterScripts.IndexOf(targetItem);
        if (oldIndex < 0 || targetIndex < 0) return;

        var newIndex = before ? targetIndex : targetIndex + 1;
        if (oldIndex < newIndex) newIndex--;
        if (newIndex != oldIndex)
            vm.MoveScript(draggedItem, newIndex, true);
    }

    private void ShowMasterDropIndicator(Control target, bool before)
    {
        if (_masterDropTarget == target && _masterDropBefore == before) return;

        ClearMasterDropIndicator();
        _masterDropTarget = target;
        _masterDropBefore = before;

        var indicator = target.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Name == "MasterDropIndicator");
        if (indicator == null) return;

        // Grid berada di dalam padding 12 px; tambah 6 px untuk mencapai pusat celah 12 px.
        indicator.VerticalAlignment = before ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
        indicator.Margin = new Thickness(0);
        indicator.RenderTransform = new Avalonia.Media.TranslateTransform(0, before ? -18 : 18);
        indicator.IsVisible = true;
    }

    private void ClearMasterDropIndicator()
    {
        if (_masterDropTarget != null)
        {
            var indicator = _masterDropTarget.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Name == "MasterDropIndicator");
            if (indicator != null) indicator.IsVisible = false;
        }

        _masterDropTarget = null;
    }

    private void AutoCompleteBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox acb)
        {
            BMachine.UI.Controls.AutoCompleteBoxHelper.OnGotFocus(acb, e);
        }
    }

    // ─── Radial Slot Drag & Drop ──────────────────────────────────────────────
    
    private const double RadialDragThreshold = 8.0;
    private const double RadialDropRadius = 25.0;
    private const double RadialSlotHalf = 18.0;
    private const double RadialHitRadius = 20.0;
    private const int RadialPageSwitchDelayMs = 600;
    private RadialSlotViewModel? _pressedRadialSlot;
    private Button? _pressedRadialButton;
    private int _pressedRadialClickCount;
    private Avalonia.Point _radialPressPoint;
    private RadialSlotViewModel? _radialDragSource;
    private RadialSlotViewModel? _radialDropTarget;
    private (int Page, int Index)? _radialPreviewKey;
    private Button? _radialDragButton;
    private DispatcherTimer? _radialPageTimer;
    private bool _radialNavArmed = true;

    private void OnRadialRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DataContext is not SettingsViewModel vm) return;

        var local = e.GetPosition(RadialSlotsControl);
        // Slot navigasi (MORE/BACK) IKUT dicatat supaya klik-nya bisa dipakai pindah halaman;
        // drag-nya diblokir terpisah di OnRadialRootPointerMoved.
        var slot = vm.RadialSlots.FirstOrDefault(s =>
            !s.IsEmpty &&
            Math.Sqrt(Math.Pow(local.X - (s.X + RadialSlotHalf), 2) + Math.Pow(local.Y - (s.Y + RadialSlotHalf), 2)) <= RadialHitRadius);
        if (slot == null) return;

        _pressedRadialSlot = slot;
        _pressedRadialButton = RadialSlotsControl.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Tag, slot));
        _radialPressPoint = e.GetPosition(this);
        _pressedRadialClickCount = e.ClickCount;
    }
    private void OnRadialRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_radialDragSource == null)
        {
            if (_pressedRadialSlot == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            // Slot navigasi tidak bisa di-drag (hanya diklik untuk pindah halaman).
            if (_pressedRadialSlot.IsNavigationReserved) return;

            var pos = e.GetPosition(this);
            double dx = pos.X - _radialPressPoint.X;
            double dy = pos.Y - _radialPressPoint.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < RadialDragThreshold) return;

            StartRadialDrag(_pressedRadialSlot, _pressedRadialButton, e);
        }

        UpdateRadialDrag(e);
    }

    private void OnRadialRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var clickedSlot = _pressedRadialSlot;
        _pressedRadialSlot = null;
        _pressedRadialButton = null;
        if (_radialDragSource == null)
        {
            if (clickedSlot != null && DataContext is SettingsViewModel clickVm)
            {
                // Slot navigasi (MORE / BACK): KLIK = pindah halaman radial.
                // Chevron < > sudah dihapus, dan sebelumnya slot ini hanya bereaksi ke dwell hover —
                // jadi klik di MORE/BACK tidak melakukan apa-apa.
                if (clickedSlot.IsNavigationReserved)
                {
                    var label = clickedSlot.NavigationLabel;
                    if (!string.IsNullOrEmpty(label))
                    {
                        StopRadialPageTimer();
                        _radialNavArmed = false;
                        if (string.Equals(label, "BACK", StringComparison.OrdinalIgnoreCase))
                            clickVm.SwitchToRadialPage1Command.Execute(null);
                        else
                            clickVm.SwitchToRadialPage2Command.Execute(null);
                    }
                }
                else if (_pressedRadialClickCount == 2)
                {
                    clickVm.SelectRadialSlotCommand.Execute(clickedSlot);
                }
            }
            return;
        }

        e.Pointer.Capture(null);
        var target = _radialDropTarget;
        ResetRadialDrag();

        if (DataContext is SettingsViewModel vm)
        {
            if (target != null) vm.CommitRadialPreview();
            else vm.CancelRadialPreview();
        }
    }

    private void StartRadialDrag(RadialSlotViewModel slot, Button? button, PointerEventArgs e)
    {
        _pressedRadialSlot = null;
        _pressedRadialButton = null;
        _radialDragSource = slot;
        _radialDragButton = button;
        _radialNavArmed = true;
        if (DataContext is SettingsViewModel vmStart) vmStart.BeginRadialPreview();

        if (button != null)
        {
            RadialDragGhostText.Text = slot.Script?.ShortCode ?? "";
            button.Opacity = 0.35;
        }

        RadialDragGhost.IsVisible = true;
        e.Pointer.Capture(this);
    }

    private void UpdateRadialDrag(PointerEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || _radialDragSource == null) return;

        var pos = e.GetPosition(RadialSlotsControl);
        Canvas.SetLeft(RadialDragGhost, pos.X - RadialSlotHalf);
        Canvas.SetTop(RadialDragGhost, pos.Y - RadialSlotHalf);

        RadialSlotViewModel? target = null;
        double best = RadialDropRadius;
        foreach (var slot in vm.RadialSlots)
        {
            if (slot.Script != null && slot.Script == _radialDragSource.Script) continue;
            double dist = Math.Sqrt(Math.Pow(pos.X - (slot.X + RadialSlotHalf), 2) + Math.Pow(pos.Y - (slot.Y + RadialSlotHalf), 2));
            if (dist < best)
            {
                best = dist;
                target = slot;
            }
        }

        _radialDropTarget = target;
        var key = target == null ? ((int, int)?)null : (target.PageIndex, target.AbsoluteIndex);
        if (key != _radialPreviewKey)
        {
            _radialPreviewKey = key;
            vm.PreviewRadialMove(_radialDragSource, target);
        }
        RadialDropHighlight.IsVisible = target != null;
        if (target != null)
        {
            Canvas.SetLeft(RadialDropHighlight, target.X + RadialSlotHalf - 22);
            Canvas.SetTop(RadialDropHighlight, target.Y + RadialSlotHalf - 22);
        }

        UpdateRadialNavHover(vm, target);
    }

    private void UpdateRadialNavHover(SettingsViewModel vm, RadialSlotViewModel? target)
    {
        bool onNav = target is { IsNavigationReserved: true } && !string.IsNullOrEmpty(target.NavigationLabel);
        if (!onNav)
        {
            _radialNavArmed = true;
            StopRadialPageTimer();
            return;
        }

        if (!_radialNavArmed || _radialPageTimer != null) return;

        _radialPageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RadialPageSwitchDelayMs) };
        _radialPageTimer.Tick += (_, _) =>
        {
            StopRadialPageTimer();
            _radialNavArmed = false;
            if (vm.RadialEditorPage == 0) vm.SwitchToRadialPage2Command.Execute(null);
            else vm.SwitchToRadialPage1Command.Execute(null);
        };
        _radialPageTimer.Start();
    }

    private void ResetRadialDrag()
    {
        StopRadialPageTimer();
        RadialDragGhost.IsVisible = false;
        RadialDropHighlight.IsVisible = false;
        if (_radialDragButton != null) _radialDragButton.Opacity = 1;
        _radialDragButton = null;
        _radialDragSource = null;
        _radialDropTarget = null;
        _radialPreviewKey = null;
    }

    private void StopRadialPageTimer()
    {
        _radialPageTimer?.Stop();
        _radialPageTimer = null;
    }
    // Buka dialog "Lapork Bug". Event-handler code-behind (bukan ViewModel)
    // karena ini murni interaksi UI: menampilkan dialog modal. Logika kirim
    // ada di ReportBugDialog + shared BugReporter.
    private void OnReportBugClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.MantraData.ReportBugDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // Owner null-safe: integration test/designer bisa saja tanpa window induk.
        if (this.GetVisualRoot() is Window owner)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }
}

// Extension helper logic not strictly needed if we do the Contains check.

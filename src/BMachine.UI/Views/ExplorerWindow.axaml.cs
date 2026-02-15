using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using BMachine.UI.ViewModels;
using System.Linq;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.ComponentModel;

namespace BMachine.UI.Views;

public partial class ExplorerWindow : Window
{
    private readonly List<KeyBinding> _windowExplorerKeyBindings = new();

    public ExplorerWindow()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<RequestCloseExplorerWindowMessage>(this, (_, m) =>
        {
            if (m.Value != this) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(HandleCloseTabOrWindow);
        });
        WeakReferenceMessenger.Default.Register<AddExplorerTabMessage>(this, (_, m) =>
        {
            if (m.Value != this) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(HandleAddTab);
        });
        WeakReferenceMessenger.Default.Register<ExplorerShortcutsReadyMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyWindowExplorerShortcuts);
        });
        WeakReferenceMessenger.Default.Register<SwitchExplorerTabMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(HandleSwitchTab);
        });
    }

    /// <summary>Ctrl+W: close active tab only when there is more than one tab; do not close window when only one tab.</summary>
    public void HandleCloseTabOrWindow()
    {
        if (DataContext is not ExplorerWindowViewModel wvm || wvm.SelectedTab == null)
        {
            SaveWindowSize();
            Close();
            return;
        }
        if (wvm.Tabs.Count > 1)
        {
            wvm.CloseTab(wvm.SelectedTab);
            return;
        }
        // Single tab: do nothing (keep window open)
    }

    private void HandleAddTab()
    {
        if (DataContext is ExplorerWindowViewModel wvm)
            wvm.AddTabCommand.Execute(null);
    }

    private void HandleSwitchTab()
    {
        if (DataContext is not ExplorerWindowViewModel wvm || wvm.Tabs.Count < 2) return;
        var idx = wvm.SelectedTab != null ? wvm.Tabs.IndexOf(wvm.SelectedTab) : -1;
        var next = (idx + 1) % wvm.Tabs.Count;
        wvm.SelectedTab = wvm.Tabs[next];
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is ExplorerWindowViewModel wvm)
        {
            wvm.PropertyChanged += OnExplorerWindowViewModelPropertyChanged;
            ApplyWindowExplorerShortcuts();
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(FocusActiveExplorerView, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnExplorerWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerWindowViewModel.SelectedTab))
            ApplyWindowExplorerShortcuts();
    }

    /// <summary>Apply explorer shortcuts at window level so they work when e.g. list has focus.</summary>
    private void ApplyWindowExplorerShortcuts()
    {
        foreach (var b in _windowExplorerKeyBindings)
            KeyBindings.Remove(b);
        _windowExplorerKeyBindings.Clear();

        if (DataContext is not ExplorerWindowViewModel wvm || wvm.SelectedTab?.ExplorerViewModel is not OutputExplorerViewModel vm)
            return;

        var keyBindings = KeyBindings;
        TryAdd(keyBindings, vm.ShortcutNewFolderGesture, vm.OpenNewFolderPopupCommand!, null);
        TryAdd(keyBindings, vm.ShortcutNewFileGesture, vm.OpenNewFilePopupCommand!, null);
        TryAdd(keyBindings, vm.ShortcutFocusSearchGesture, vm.FocusPathBarCommand!, null);
        TryAdd(keyBindings, vm.ShortcutDeleteGesture, vm.DeleteItemCommand!, vm.SelectedItems);
        TryAdd(keyBindings, vm.ShortcutNewWindowGesture, vm.NewExplorerWindowCommand!, null);
        TryAdd(keyBindings, vm.ShortcutNewTabGesture, new CommunityToolkit.Mvvm.Input.RelayCommand(() => HandleAddTab()), null);
        TryAdd(keyBindings, vm.ShortcutCloseTabGesture, new CommunityToolkit.Mvvm.Input.RelayCommand(() => HandleCloseTabOrWindow()), null);
        TryAdd(keyBindings, vm.ShortcutNavigateUpGesture, vm.NavigateUpCommand!, null);
        TryAdd(keyBindings, vm.ShortcutBackGesture, vm.GoBackCommand!, null);
        TryAdd(keyBindings, vm.ShortcutForwardGesture, vm.GoForwardCommand!, null);
        TryAdd(keyBindings, vm.ShortcutRenameGesture, vm.RenameItemCommand!, vm.SelectedItems);
        TryAdd(keyBindings, vm.ShortcutPermanentDeleteGesture, vm.PermanentDeleteItemCommand!, vm.SelectedItems);
        TryAdd(keyBindings, vm.ShortcutFocusSearchBoxGesture, vm.FocusSearchBoxCommand!, null);
        TryAdd(keyBindings, vm.ShortcutAddressBarGesture, vm.FocusPathBarCommand!, null);
        TryAdd(keyBindings, vm.ShortcutSwitchTabGesture, vm.SwitchTabCommand!, null);
        TryAdd(keyBindings, vm.ShortcutRefreshGesture, vm.RefreshCommand!, null);
        // Standard shortcuts (not customizable)
        TryAdd(keyBindings, "Ctrl+A", vm.SelectAllCommand!, null);
        TryAdd(keyBindings, "Ctrl+C", vm.CopyItemCommand!, vm.SelectedItems);
        TryAdd(keyBindings, "Ctrl+X", vm.CutItemCommand!, vm.SelectedItems);
        TryAdd(keyBindings, "Ctrl+V", vm.PasteItemCommand!, null);
    }

    private void TryAdd(IList<KeyBinding> keyBindings, string gestureStr, System.Windows.Input.ICommand command, object? parameter)
    {
        if (string.IsNullOrWhiteSpace(gestureStr)) return;
        try
        {
            var kb = new KeyBinding { Gesture = KeyGesture.Parse(gestureStr), Command = command, CommandParameter = parameter };
            keyBindings.Add(kb);
            _windowExplorerKeyBindings.Add(kb);
        }
        catch { }
    }

    /// <summary>Focus the active (visible) OutputExplorerView so keyboard shortcuts work.</summary>
    private void FocusActiveExplorerView()
    {
        var explorerView = this.GetVisualDescendants()
            .OfType<OutputExplorerView>()
            .FirstOrDefault(v => v.IsVisible);
        explorerView?.Focus();
    }

    private void OnDragZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private BMachine.SDK.IDatabase? _database;
    private const string SETTING_KEY_WIDTH = "ExplorerWindow_Width";
    private const string SETTING_KEY_HEIGHT = "ExplorerWindow_Height";

    public void Init(BMachine.SDK.IDatabase database)
    {
        _database = database;
        LoadWindowSize();
    }

    private async void LoadWindowSize()
    {
        if (_database == null) return;
        
        try
        {
            var w = await _database.GetAsync<string>(SETTING_KEY_WIDTH);
            var h = await _database.GetAsync<string>(SETTING_KEY_HEIGHT);

            if (double.TryParse(w, out double width) && width > 100) this.Width = width;
            if (double.TryParse(h, out double height) && height > 100) this.Height = height;
        }
        catch { }
    }

    private async void SaveWindowSize()
    {
        if (_database == null) return;
        try
        {
            await _database.SetAsync<string>(SETTING_KEY_WIDTH, this.Bounds.Width.ToString());
            await _database.SetAsync<string>(SETTING_KEY_HEIGHT, this.Bounds.Height.ToString());
        }
        catch { }
    }

    private void OnCloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveWindowSize();
        Close();
    }

    private void OnCloseTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control btn || DataContext is not ExplorerWindowViewModel wvm) return;
        if (btn.DataContext is ExplorerTabItemViewModel tab)
        {
            wvm.CloseTab(tab);
            if (wvm.Tabs.Count == 0)
            {
                SaveWindowSize();
                Close();
            }
        }
    }
    
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SaveWindowSize();
    }

    private void OnTabHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control ctrl || DataContext is not ExplorerWindowViewModel wvm) return;
        if (ctrl.DataContext is not ExplorerTabItemViewModel tab) return;

        var point = e.GetCurrentPoint(ctrl);
        // Middle-click: close tab (standard OS explorer / browser behavior)
        if (point.Properties.IsMiddleButtonPressed)
        {
            wvm.CloseTab(tab);
            if (wvm.Tabs.Count == 0)
            {
                SaveWindowSize();
                Close();
            }
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            wvm.SelectedTab = tab;
            e.Handled = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(FocusActiveExplorerView, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}

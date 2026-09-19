using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class MantraContextMenuSettingsDialog : MantraDialogBase
{
    public bool Confirmed { get; private set; }

    public MantraContextMenuSettingsDialog()
    {
        InitializeComponent();
    }

    public MantraContextMenuSettingsDialog(MantraContextMenuSettingsViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnShortcutGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.BorderBrush = SolidColorBrush.Parse("#388BFD");
            tb.Background = SolidColorBrush.Parse("#1A2030");
        }
    }

    private void OnShortcutLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.BorderBrush = SolidColorBrush.Parse("#27272A");
            tb.Background = SolidColorBrush.Parse("#18181B");
        }
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not MantraContextMenuItemConfig item)
            return;

        if (e.Key == Key.Escape)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            item.ShortcutKey = string.Empty;
            tb.Text = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        e.Handled = true;
        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        var gestureStr = gesture.ToString();
        if (!string.IsNullOrWhiteSpace(gestureStr))
        {
            item.ShortcutKey = gestureStr;
            tb.Text = gestureStr;
        }
    }

    private void OnClearShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is MantraContextMenuItemConfig item)
        {
            item.ShortcutKey = string.Empty;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MantraContextMenuSettingsViewModel vm)
        {
            vm.SaveToSettings();
        }
        Confirmed = true;
        Close();
    }
}

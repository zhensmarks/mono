using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;

namespace BMachine.UI.Views;

public partial class ExplorerSettingsView : UserControl
{
    public ExplorerSettingsView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        WeakReferenceMessenger.Default.Register<ExplorerSettingsFocusMessage>(this, (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus());
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ExplorerSettingsViewModel vm || !vm.IsRecordingShortcut)
            return;

        if (e.Key == Key.Escape)
        {
            vm.CancelRecordingCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LWin || e.Key == Key.RWin)
            return;

        e.Handled = true;
        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        var gestureStr = gesture.ToString();
        if (!string.IsNullOrWhiteSpace(gestureStr))
            vm.ApplyRecordedShortcut(gestureStr);
    }
}

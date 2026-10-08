using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace BMachine.UI.Views.Controls;

public partial class InAppToast : UserControl
{
    public static readonly StyledProperty<bool> IsVisibleProperty =
        AvaloniaProperty.Register<InAppToast, bool>(nameof(IsVisible), false);

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<InAppToast, string>(nameof(Message), string.Empty);

    public static readonly StyledProperty<Geometry?> IconPathProperty =
        AvaloniaProperty.Register<InAppToast, Geometry?>(nameof(IconPath));

    public static readonly StyledProperty<IBrush?> IconColorProperty =
        AvaloniaProperty.Register<InAppToast, IBrush?>(nameof(IconColor));

    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public Geometry? IconPath
    {
        get => GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public IBrush? IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public InAppToast()
    {
        InitializeComponent();
        DataContext = this;
    }

    public async Task ShowAsync(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Message = message;
            ApplyTypeStyle(type);
            IsVisible = true;
        });

        await Task.Delay(durationMs);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsVisible = false;
        });
    }

    private void ApplyTypeStyle(ToastType type)
    {
        var border = this.FindControl<Border>("ToastBorder");
        if (border == null) return;

        border.Classes.Clear();
        border.Classes.Add("ToastContainer");

        // Load icon & color dari StaticResource TablerIcons.axaml
        switch (type)
        {
            case ToastType.Success:
                border.Classes.Add("Success");
                IconColor = GetResource<IBrush>("AccentGreenBrush");
                IconPath = GetResource<Geometry>("IconCheck");
                break;
            case ToastType.Error:
                border.Classes.Add("Error");
                IconColor = GetResource<IBrush>("ButtonDangerBackgroundBrush");
                IconPath = GetResource<Geometry>("IconAlertCircle");
                break;
            case ToastType.Warning:
                border.Classes.Add("Warning");
                IconColor = GetResource<IBrush>("AccentOrangeBrush");
                IconPath = GetResource<Geometry>("IconAlertTriangle");
                break;
            case ToastType.Info:
            default:
                border.Classes.Add("Info");
                IconColor = GetResource<IBrush>("AccentBlueBrush");
                IconPath = GetResource<Geometry>("IconInfoCircle");
                break;
        }
    }

    private T? GetResource<T>(string key) where T : class
    {
        if (Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) == true)
        {
            return value as T;
        }
        return null;
    }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

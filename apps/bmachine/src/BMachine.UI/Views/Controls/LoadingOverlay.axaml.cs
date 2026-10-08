using Avalonia;
using Avalonia.Controls;

namespace BMachine.UI.Views.Controls;

public partial class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsLoading), false);

    public static readonly StyledProperty<string> LoadingTitleProperty =
        AvaloniaProperty.Register<LoadingOverlay, string>(nameof(LoadingTitle), string.Empty);

    public static readonly StyledProperty<string> LoadingMessageProperty =
        AvaloniaProperty.Register<LoadingOverlay, string>(nameof(LoadingMessage), string.Empty);

    public static readonly StyledProperty<double> LoadingProgressProperty =
        AvaloniaProperty.Register<LoadingOverlay, double>(nameof(LoadingProgress), 0);

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(ShowProgress), false);

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsIndeterminate), true);

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string LoadingTitle
    {
        get => GetValue(LoadingTitleProperty);
        set => SetValue(LoadingTitleProperty, value);
    }

    public string LoadingMessage
    {
        get => GetValue(LoadingMessageProperty);
        set => SetValue(LoadingMessageProperty, value);
    }

    public double LoadingProgress
    {
        get => GetValue(LoadingProgressProperty);
        set => SetValue(LoadingProgressProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public LoadingOverlay()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Show(string title = "Memproses...", string message = "", bool showProgress = false, bool indeterminate = true)
    {
        LoadingTitle = title;
        LoadingMessage = message;
        ShowProgress = showProgress;
        IsIndeterminate = indeterminate;
        LoadingProgress = 0;
        IsLoading = true;
    }

    public void Hide()
    {
        IsLoading = false;
    }

    public void UpdateProgress(double progress, string? message = null)
    {
        LoadingProgress = progress;
        if (message != null)
            LoadingMessage = message;
    }
}

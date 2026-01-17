using Avalonia;
using Avalonia.Controls;
using System.Collections;
using System.Windows.Input;

namespace BMachine.UI.Controls;

public partial class ActivityFeed : UserControl
{
    public static readonly StyledProperty<IEnumerable> ActivitiesProperty =
        AvaloniaProperty.Register<ActivityFeed, IEnumerable>(nameof(Activities));

    public static readonly StyledProperty<ICommand> ClearCommandProperty =
        AvaloniaProperty.Register<ActivityFeed, ICommand>(nameof(ClearCommand));

    public IEnumerable Activities
    {
        get => GetValue(ActivitiesProperty);
        set => SetValue(ActivitiesProperty, value);
    }

    public ICommand ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public ActivityFeed()
    {
        InitializeComponent();
    }
}

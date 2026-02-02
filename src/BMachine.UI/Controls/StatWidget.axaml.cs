using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace BMachine.UI.Controls;

public partial class StatWidget : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<StatWidget, string>(nameof(Value), "0");

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatWidget, string>(nameof(Label));

    public static readonly StyledProperty<IBrush> ColorProperty =
        AvaloniaProperty.Register<StatWidget, IBrush>(nameof(Color), Brushes.Blue);
        
    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<StatWidget, double>(nameof(Percentage), 0);
        
    public static readonly StyledProperty<Geometry> IconProperty =
        AvaloniaProperty.Register<StatWidget, Geometry>(nameof(Icon));
        
    public static readonly StyledProperty<bool> ShowIconProperty =
        AvaloniaProperty.Register<StatWidget, bool>(nameof(ShowIcon), true);

    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<StatWidget, bool>(nameof(IsOnline), true);

    public static readonly StyledProperty<System.Windows.Input.ICommand> CommandProperty =
        AvaloniaProperty.Register<StatWidget, System.Windows.Input.ICommand>(nameof(Command));

    public static readonly StyledProperty<System.Windows.Input.ICommand> WindowCommandProperty =
        AvaloniaProperty.Register<StatWidget, System.Windows.Input.ICommand>(nameof(WindowCommand));

    public System.Windows.Input.ICommand WindowCommand
    {
        get => GetValue(WindowCommandProperty);
        set => SetValue(WindowCommandProperty, value);
    }

    public System.Windows.Input.ICommand Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IBrush Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
    
    public double Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }
    
    public Geometry Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    
    public bool ShowIcon
    {
        get => GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }
    
    public bool IsOnline
    {
        get => GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }
    
    // Calculated property for Arc SweepAngle
    public double SweepAngle => Percentage * 360;

    // Animation Duration Property
    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<StatWidget, TimeSpan>(nameof(AnimationDuration), TimeSpan.FromSeconds(1.5));

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    private Avalonia.Controls.Shapes.Arc? _progressArc;

    public StatWidget()
    {
        InitializeComponent();
        
        // Update SweepAngle when Percentage changes
        PropertyChanged += (sender, e) => 
        {
            if (e.Property == PercentageProperty)
            {
                RaisePropertyChanged(SweepAngleProperty, 0, SweepAngle);
            }
            // Update Transition when AnimationDuration changes
            else if (e.Property == AnimationDurationProperty)
            {
                UpdateTransition();
            }
        };
    }
    
    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _progressArc = e.NameScope.Find<Avalonia.Controls.Shapes.Arc>("PART_ProgressArc");
        UpdateTransition();
    }

    private void UpdateTransition()
    {
        if (_progressArc == null) return;
        
        var transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition
            {
                Property = Avalonia.Controls.Shapes.Arc.SweepAngleProperty,
                Duration = AnimationDuration
            }
        };
        _progressArc.Transitions = transitions;
    }
    
    public static readonly DirectProperty<StatWidget, double> SweepAngleProperty =
        AvaloniaProperty.RegisterDirect<StatWidget, double>(
            nameof(SweepAngle),
            o => o.SweepAngle);
}

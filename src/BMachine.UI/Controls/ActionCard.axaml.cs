using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Windows.Input;

namespace BMachine.UI.Controls;

public partial class ActionCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ActionCard, string>(nameof(Title));

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<ActionCard, string>(nameof(Description));

    public static readonly StyledProperty<Geometry> IconDataProperty =
        AvaloniaProperty.Register<ActionCard, Geometry>(nameof(IconData));

    public static readonly StyledProperty<IBrush> AccentColorProperty =
        AvaloniaProperty.Register<ActionCard, IBrush>(nameof(AccentColor), Brushes.Blue);
        
    public static readonly StyledProperty<ICommand> CommandProperty =
        AvaloniaProperty.Register<ActionCard, ICommand>(nameof(Command));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public IBrush AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }
    
    public ICommand Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ActionCard()
    {
        InitializeComponent();
    }
}

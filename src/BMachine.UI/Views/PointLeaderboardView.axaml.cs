using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BMachine.UI.Views;

public partial class PointLeaderboardView : UserControl
{
    public PointLeaderboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

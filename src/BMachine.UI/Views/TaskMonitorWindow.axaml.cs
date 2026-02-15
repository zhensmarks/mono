using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BMachine.UI.Views;

public partial class TaskMonitorWindow : Window
{
    public TaskMonitorWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

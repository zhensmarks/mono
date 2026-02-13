using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class OutputExplorerView : UserControl
{
    public OutputExplorerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ExplorerItemViewModel viewModel)
        {
            if (this.DataContext is OutputExplorerViewModel context)
            {
                context.OpenItemCommand.Execute(viewModel);
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class EditingCardListView : UserControl
{
    public EditingCardListView()
    {
        InitializeComponent();
        this.PointerPressed += EditingCardListView_PointerPressed;
    }

    private void EditingCardListView_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (DataContext is BMachine.UI.ViewModels.EditingCardListViewModel vm)
            {
                // If any panel is open, close it first
                if (vm.IsCommentPanelOpen || vm.IsChecklistPanelOpen || vm.IsMovePanelOpen || vm.IsAttachmentPanelOpen)
                {
                    vm.IsCommentPanelOpen = false;
                    vm.IsChecklistPanelOpen = false;
                    vm.IsMovePanelOpen = false;
                    vm.IsAttachmentPanelOpen = false;
                    e.Handled = true;
                    return;
                }

                vm.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (DataContext is BMachine.UI.ViewModels.EditingCardListViewModel vm)
            {
                if (vm.IsCommentPanelOpen || vm.IsChecklistPanelOpen || vm.IsMovePanelOpen || vm.IsAttachmentPanelOpen)
                {
                    vm.IsCommentPanelOpen = false;
                    vm.IsChecklistPanelOpen = false;
                    vm.IsMovePanelOpen = false;
                    vm.IsAttachmentPanelOpen = false;
                    e.Handled = true;
                    return;
                }

                vm.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private async void OnIdClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.Tag is string id && !string.IsNullOrEmpty(id))
        {
             var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
             if (topLevel?.Clipboard != null)
             {
                 await topLevel.Clipboard.SetTextAsync(id);
             }
        }
    }

    private void OnRootGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid) return;
        
        var commentPanel = this.FindControl<Border>("Part_CommentPanel");
        var checklistPanel = this.FindControl<Border>("Part_ChecklistPanel");
        var movePanel = this.FindControl<Border>("Part_MovePanel");
        var attachmentPanel = this.FindControl<Border>("Part_AttachmentPanel");
        var mainContent = this.FindControl<Grid>("Part_MainContent");

        if (commentPanel == null || mainContent == null) return; 

        double threshold = 800; 
        bool isMobile = e.NewSize.Width < threshold;
        
        if (isMobile)
        {
            grid.ColumnDefinitions = ColumnDefinitions.Parse("*");
            
            UpdatePanelForMobile(commentPanel);
            UpdatePanelForMobile(checklistPanel);
            UpdatePanelForMobile(movePanel);
            UpdatePanelForMobile(attachmentPanel);
        }
        else
        {
            grid.ColumnDefinitions = ColumnDefinitions.Parse("*, Auto");
            
            UpdatePanelForDesktop(commentPanel);
            UpdatePanelForDesktop(checklistPanel);
            UpdatePanelForDesktop(movePanel);
            UpdatePanelForDesktop(attachmentPanel);
        }
    }

    private void UpdatePanelForMobile(Border? panel)
    {
        if (panel == null) return;
        Grid.SetColumn(panel, 0); 
        panel.Width = double.NaN; // Auto
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        panel.Margin = new Avalonia.Thickness(0);
        
        if (Application.Current!.TryGetResource("AppBackgroundBrush", out var res) && res is IBrush brush)
        {
             panel.Background = brush;
        }
        else
        {
             panel.Background = Brushes.Black; // Fallback
        }
    }

    private void UpdatePanelForDesktop(Border? panel)
    {
        if (panel == null) return;
        Grid.SetColumn(panel, 1);
        panel.Width = 400;
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        panel.Margin = new Avalonia.Thickness(0);
        panel.Background = Brushes.Transparent;
    }
}

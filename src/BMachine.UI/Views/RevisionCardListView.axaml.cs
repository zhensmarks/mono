using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class RevisionCardListView : UserControl
{
    public RevisionCardListView()
    {
        InitializeComponent();
        this.PointerPressed += OnRootPointerPressed;
    }

    private void OnRootPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (DataContext is RevisionCardListViewModel vm)
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
            if (DataContext is RevisionCardListViewModel vm)
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
    
    // Copy ID functionality
    private async void OnIdClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id && !string.IsNullOrEmpty(id))
        {
             var topLevel = TopLevel.GetTopLevel(this);
             if (topLevel != null && topLevel.Clipboard != null)
             {
                 await topLevel.Clipboard.SetTextAsync(id);
                 // Optional: Show small toast/tooltip success? 
                 // For now, simple copy.
                 Console.WriteLine($"Copied: {id}");
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
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            UpdatePanelForMobile(commentPanel);
            UpdatePanelForMobile(checklistPanel);
            UpdatePanelForMobile(movePanel);
            UpdatePanelForMobile(attachmentPanel);
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*, Auto");
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
        panel.Width = double.NaN; 
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        panel.Margin = new Avalonia.Thickness(0);
        if (Avalonia.Application.Current!.TryGetResource("AppBackgroundBrush", out var res) && res is Avalonia.Media.IBrush brush)
        {
             panel.Background = brush;
        }
    }

    private void UpdatePanelForDesktop(Border? panel)
    {
        if (panel == null) return;
        Grid.SetColumn(panel, 1);
        panel.Width = 400;
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        panel.Margin = new Avalonia.Thickness(0);
        panel.Background = Avalonia.Media.Brushes.Transparent;
    }
}

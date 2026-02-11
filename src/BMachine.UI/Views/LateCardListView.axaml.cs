using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class LateCardListView : UserControl
{
    public LateCardListView()
    {
        InitializeComponent();
        this.PointerPressed += OnRootPointerPressed;
    }

    private void OnRootPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (DataContext is LateCardListViewModel vm)
            {
                // If any panel is open, close it first
                if (vm.IsDetailPanelOpen || vm.IsCommentPanelOpen || vm.IsChecklistPanelOpen || vm.IsMovePanelOpen || vm.IsAttachmentPanelOpen)
                {
                    vm.IsDetailPanelOpen = false;
                    vm.IsCommentPanelOpen = false;
                    vm.IsChecklistPanelOpen = false;
                    vm.IsMovePanelOpen = false;
                    vm.IsAttachmentPanelOpen = false;
                    e.Handled = true;
                    return;
                }

                // Navigate Back using Message
                CommunityToolkit.Mvvm.Messaging.IMessenger messenger = CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default;
                messenger.Send(new BMachine.UI.Messages.NavigateBackMessage());
                e.Handled = true;
            }
        }
    }

    private void OnDetailPanelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
         var props = e.GetCurrentPoint(sender as Visual).Properties;
         if (props.IsRightButtonPressed)
         {
             if (DataContext is LateCardListViewModel vm)
             {
                 vm.IsDetailPanelOpen = false;
                 e.Handled = true;
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
        var detailPanel = this.FindControl<Border>("Part_DetailPanel");
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
            UpdatePanelForMobile(detailPanel);
        }
        else
        {
            grid.ColumnDefinitions = ColumnDefinitions.Parse("*, Auto");
            UpdatePanelForDesktop(commentPanel);
            UpdatePanelForDesktop(checklistPanel);
            UpdatePanelForDesktop(movePanel);
            UpdatePanelForDesktop(attachmentPanel);
            UpdatePanelForDesktop(detailPanel);
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
        
        // Ensure solid background for embedded view
        if (Application.Current!.TryGetResource("AppBackgroundBrush", out var res) && res is IBrush brush)
        {
             panel.Background = brush;
        }
    }

    private void AutoCompleteBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox acb)
        {
            acb.IsDropDownOpen = true;
        }
    }
}

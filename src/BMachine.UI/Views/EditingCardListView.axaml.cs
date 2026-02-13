using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class EditingCardListView : UserControl
{
    public EditingCardListView()
    {
        InitializeComponent();
        this.PointerPressed += EditingCardListView_PointerPressed;
        
        // Fix Scrolling Issue: Focus on Hover
        this.Focusable = true;
        this.PointerEntered += (s, e) => this.Focus(); 
    }

    private void EditingCardListView_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (DataContext is BMachine.UI.ViewModels.EditingCardListViewModel vm)
            {
                // If any panel is open, close it first
                if (vm.IsDetailPanelOpen || vm.IsCommentPanelOpen || vm.IsChecklistPanelOpen || vm.IsMovePanelOpen || vm.IsAttachmentPanelOpen)
                {
                    vm.IsDetailPanelOpen = false; // Close detail panel too if user right clicks main area
                    vm.IsCommentPanelOpen = false;
                    vm.IsChecklistPanelOpen = false;
                    vm.IsMovePanelOpen = false;
                    vm.IsAttachmentPanelOpen = false;
                    e.Handled = true;
                    return;
                }

                // If no panels open, Navigate Back (DashboardViewModel handles this if we send a message or call a command?)
                // Since this view is now embedded, we need to talk to the navigation controller (DashboardVM).
                // DashboardVM isn't the DataContext here (EditingCardListViewModel is).
                // We should use Messenger to signal Back.
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
             if (DataContext is BMachine.UI.ViewModels.EditingCardListViewModel vm)
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

    private void AutoCompleteBox_GotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox acb)
        {
            acb.IsDropDownOpen = true;
        }
    }

    private void OnNextViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        object? sourceWindow = null;
        if (this.VisualRoot is BMachine.UI.Views.CardListWindow win)
        {
            sourceWindow = win;
        }
        WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.NavigateToNextTrelloViewMessage("Editing", sourceWindow));
    }

    private Vector _savedCommentScrollOffset;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is BaseTrelloListViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BaseTrelloListViewModel.IsLoadingComments))
        {
             if (DataContext is BaseTrelloListViewModel vm)
             {
                 var scrollViewer = this.FindControl<ScrollViewer>("Part_CommentScrollViewer");
                 if (scrollViewer == null) return;

                 if (vm.IsLoadingComments)
                 {
                     _savedCommentScrollOffset = scrollViewer.Offset;
                 }
                 else
                 {
                     Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                     {
                         scrollViewer.Offset = _savedCommentScrollOffset;
                     }, Avalonia.Threading.DispatcherPriority.Loaded);
                 }
             }
        }
    }
}

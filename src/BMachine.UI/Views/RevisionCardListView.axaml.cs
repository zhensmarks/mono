using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
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
             if (DataContext is RevisionCardListViewModel vm)
             {
                 vm.IsDetailPanelOpen = false;
                 e.Handled = true;
             }
         }
    }

    private void OnSubPanelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
         var props = e.GetCurrentPoint(sender as Visual).Properties;
         if (props.IsRightButtonPressed)
         {
             if (DataContext is RevisionCardListViewModel vm)
             {
                 // Close the sub-panel and return to Card Detail
                 vm.IsCommentPanelOpen = false;
                 vm.IsChecklistPanelOpen = false;
                 vm.IsMovePanelOpen = false;
                 vm.IsAttachmentPanelOpen = false;
                 if (vm.SelectedCard != null) vm.IsDetailPanelOpen = true;
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

    private void OnNextViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        object? sourceWindow = null;
        if (this.VisualRoot is BMachine.UI.Views.CardListWindow win)
        {
            sourceWindow = win;
        }
        WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.NavigateToNextTrelloViewMessage("Revisi", sourceWindow));
    }

    private void OnOpenCommentWindowClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is BaseTrelloListViewModel vm)
        {
            var win = new CommentWindow
            {
                DataContext = vm
            };
            win.Show();
            
            // Optional: Close the inline panel if desired. 
            // For now, let's keep it open or maybe close it to avoid confusion?
            // "saat saya sedang kerjakan yang ada di komentar saya bisa langsung scroll tanpa harus di klik dulu untuk fokus atau dia kehilangan fokus setelah beberapa lama membuatnya menjadi harus ke atas lagi"
            // The user wants a persistent window. Closing the panel seems appropriate to avoid clutter.
            vm.IsCommentPanelOpen = false;
        }
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
        // Only save/restore scroll when comments are intentionally reloaded, NOT on click or focus.
        if (e.PropertyName == nameof(BaseTrelloListViewModel.IsLoadingComments))
        {
             if (DataContext is BaseTrelloListViewModel vm)
             {
                 var scrollViewer = this.FindControl<ScrollViewer>("Part_CommentScrollViewer");
                 if (scrollViewer == null) return;

                 if (vm.IsLoadingComments)
                 {
                     // Save BEFORE the reload begins
                     _savedCommentScrollOffset = scrollViewer.Offset;
                 }
                 else
                 {
                     // Restore ONLY if we had a real saved position from a reload
                     if (_savedCommentScrollOffset.Y > 0)
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
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.KeyDown -= CommentTextBox_KeyDown;
            textBox.KeyDown += CommentTextBox_KeyDown;
        }

        // Block ALL RequestBringIntoView events at the UserControl level.
        // Using Tunnel ensures we catch it BEFORE any ScrollViewer processes it.
        this.AddHandler(
            Avalonia.Controls.Control.RequestBringIntoViewEvent,
            OnRequestBringIntoView,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            true);

        // Save/restore scroll position on window switch
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.Activated -= OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
            window.Activated += OnWindowActivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        this.RemoveHandler(
            Avalonia.Controls.Control.RequestBringIntoViewEvent,
            OnRequestBringIntoView);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.Activated -= OnWindowActivated;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("Part_CommentScrollViewer");
        if (scrollViewer != null)
            _savedCommentScrollOffset = scrollViewer.Offset;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_savedCommentScrollOffset.Y > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var scrollViewer = this.FindControl<ScrollViewer>("Part_CommentScrollViewer");
                if (scrollViewer != null)
                    scrollViewer.Offset = _savedCommentScrollOffset;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private async void CommentTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
         if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
         {
             if (sender is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText))
             {
                 var topLevel = TopLevel.GetTopLevel(this);
                 if (topLevel?.Clipboard != null)
                 {
                     await topLevel.Clipboard.SetTextAsync(tb.SelectedText);
                     // Stop event from bubbling and causing UI side-effects (scroll jump).
                     e.Handled = true;
                 }
             }
         }
    }

    private async void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is BMachine.UI.Models.TrelloCard card)
        {
            var dragData = new DataObject();
            dragData.Set("TrelloCard", card);
            
            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
        }
    }

    private void OnCardDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        
        if (e.Data.Contains("TrelloCard") && sender is Control targetControl && targetControl.DataContext is BMachine.UI.Models.TrelloCard targetCard)
        {
             if (DataContext is BMachine.UI.ViewModels.RevisionCardListViewModel vm)
             {
                 var draggedCard = e.Data.Get("TrelloCard") as BMachine.UI.Models.TrelloCard;
                 if (draggedCard != null && draggedCard != targetCard && vm.Cards.Contains(draggedCard))
                 {
                     e.DragEffects = DragDropEffects.Move;
                 }
             }
        }
    }

    private void OnCardDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control targetControl || 
            targetControl.DataContext is not BMachine.UI.Models.TrelloCard targetCard ||
            DataContext is not BMachine.UI.ViewModels.RevisionCardListViewModel vm) return;

        if (e.Data.Contains("TrelloCard") && e.Data.Get("TrelloCard") is BMachine.UI.Models.TrelloCard draggedCard)
        {
             if (vm.Cards.Contains(draggedCard))
             {
                 int oldIndex = vm.Cards.IndexOf(draggedCard);
                 int newIndex = vm.Cards.IndexOf(targetCard);
                 
                 if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                 {
                     vm.Cards.Move(oldIndex, newIndex);
                     
                     // Calculate new Pos for Trello
                     double newPos = 10000;
                     if (newIndex == 0)
                     {
                         if (vm.Cards.Count > 1) {
                             newPos = vm.Cards[1].Pos / 2.0; 
                         }
                     }
                     else if (newIndex == vm.Cards.Count - 1)
                     {
                         if (vm.Cards.Count > 1) {
                             newPos = vm.Cards[newIndex - 1].Pos + 10000;
                         } 
                     }
                     else
                     {
                         double prevPos = vm.Cards[newIndex - 1].Pos;
                         double nextPos = vm.Cards[newIndex + 1].Pos;
                         newPos = (prevPos + nextPos) / 2.0;
                     }
                     
                     draggedCard.Pos = newPos;
                     
                     if (vm.UpdateCardPositionCommand.CanExecute(draggedCard))
                     {
                         vm.UpdateCardPositionCommand.Execute(draggedCard);
                     }
                 }
             }
        }
    }
}

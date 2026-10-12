using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia;
using BMachine.UI.ViewModels;
using System.Collections.Generic;
using System.Linq;

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
                 if (vm.SelectedCard != null)
                 {
                     vm.SelectedCard.IsActive = false;
                     vm.SelectedCard = null;
                 }
                 e.Handled = true;
             }
         }
    }

    private void OnSubPanelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
         var props = e.GetCurrentPoint(sender as Visual).Properties;
         if (props.IsRightButtonPressed)
         {
             if (DataContext is LateCardListViewModel vm)
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
            UpdatePanelForMobile(commentPanel);
            UpdatePanelForMobile(checklistPanel);
            UpdatePanelForMobile(movePanel);
            UpdatePanelForMobile(attachmentPanel);
            UpdatePanelForMobile(detailPanel);
        }
        else
        {
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
            BMachine.UI.Controls.AutoCompleteBoxHelper.OnGotFocus(acb, e);
        }
    }

    private void OnNextViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        object? sourceWindow = null;
        if (this.VisualRoot is BMachine.UI.Views.CardListWindow win)
        {
            sourceWindow = win;
        }
        WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.NavigateToNextTrelloViewMessage("Late", sourceWindow));
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
            vm.PickAttachmentFilesFunc = PickAttachmentFilesAsync;
            _ = vm.LoadBoardMembers();
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
        this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnCardDragPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        this.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnCardDragPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        this.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnCardDragPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        this.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent, OnCardDragPointerCaptureLost, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        this.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnCardDragKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, CommentTextBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            textBox.PropertyChanged -= CommentTextBox_PropertyChanged;
            textBox.PropertyChanged += CommentTextBox_PropertyChanged;
            DragDrop.SetAllowDrop(textBox, true);
            textBox.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            textBox.AddHandler(DragDrop.DropEvent, OnAttachDrop);
        }
        var attachButton = this.FindControl<Button>("Part_InlineAttachButton");
        if (attachButton != null)
        {
            DragDrop.SetAllowDrop(attachButton, true);
            attachButton.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            attachButton.AddHandler(DragDrop.DropEvent, OnAttachDrop);
        }

        this.AddHandler(
            Avalonia.Controls.Control.RequestBringIntoViewEvent,
            OnRequestBringIntoView,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            true);

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
         bool isCmdOrCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

         if (isCmdOrCtrl && e.Key == Key.C)
         {
             if (sender is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText))
             {
                 var topLevel = TopLevel.GetTopLevel(this);
                 if (topLevel?.Clipboard != null)
                 {
                     await topLevel.Clipboard.SetTextAsync(tb.SelectedText);
                     e.Handled = true;
                 }
             }
         }
         else if (isCmdOrCtrl && e.Key == Key.V)
         {
             _ = HandleClipboardPaste();
         }
    }

    private void CommentTextBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty) return;
        if (sender is not TextBox tb || DataContext is not BaseTrelloListViewModel vm) return;
        vm.HandleCommentTextChanged(tb.Text ?? "", tb.CaretIndex);
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<string>> PickAttachmentFilesAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return System.Array.Empty<string>();
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Images to Attach",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll,
                new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    private void OnAttachDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnAttachDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not BaseTrelloListViewModel vm) return;
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;
        foreach (var file in files)
            vm.AddAttachmentFromPath(file.Path.LocalPath);
    }

    private async System.Threading.Tasks.Task HandleClipboardPaste()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;
            var formats = await topLevel.Clipboard.GetFormatsAsync();
            if (formats == null) return;
            if (DataContext is not BaseTrelloListViewModel vm) return;

            if (formats.Contains(Avalonia.Input.DataFormats.Files))
            {
                var data = await topLevel.Clipboard.GetDataAsync(Avalonia.Input.DataFormats.Files);
                if (data is IEnumerable<Avalonia.Platform.Storage.IStorageItem> items)
                {
                    foreach (var item in items)
                        vm.AddAttachmentFromPath(item.Path.LocalPath);
                    return;
                }
            }

            string[] imageFormats = { "PNG", "image/png", "JPEG", "image/jpeg", "public.png", "public.jpeg", "Avalonia.Media.Imaging.Bitmap" };
            foreach (var format in imageFormats)
            {
                if (formats.Contains(format))
                {
                    var data = await topLevel.Clipboard.GetDataAsync(format);
                    if (data is byte[] bytes)
                    {
                        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paste_{System.Guid.NewGuid()}.png");
                        await System.IO.File.WriteAllBytesAsync(tempFile, bytes);
                        vm.AddAttachmentFromPath(tempFile);
                        return;
                    }
                    if (data is Avalonia.Media.Imaging.Bitmap bitmap)
                    {
                        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paste_{System.Guid.NewGuid()}.png");
                        bitmap.Save(tempFile);
                        vm.AddAttachmentFromPath(tempFile);
                        return;
                    }
                }
            }
        }
        catch { /* Ignore clipboard errors */ }
    }

    // Drag dari seluruh kartu: tekan kiri lalu geser 6px. Klik tanpa geser tetap ke Button.
    private Point? _cardPressPoint;
    private BMachine.UI.Models.TrelloCard? _pressedCard;
    private BMachine.UI.Models.TrelloCard? _dropCard;
    private bool _dropBefore;
    private Border? _draggedContainer;
    private RenderTargetBitmap? _ghostBitmap;
    private TranslateTransform? _ghostTransform;
    private bool _dragActive;
    private bool _dragCancelled;

    private BMachine.UI.Models.TrelloCard? FindCardFrom(Visual? from)
    {
        Visual? v = from;
        while (v != null && !ReferenceEquals(v, this))
        {
            if (v is Control c && c.DataContext is BMachine.UI.Models.TrelloCard card)
                return card;
            v = v.GetVisualParent();
        }
        return null;
    }

    private Border? FindCardContainer(Visual? from)
    {
        Visual? v = from;
        while (v != null && !ReferenceEquals(v, this))
        {
            if (v is Border b && b.Name == "CardContainer")
                return b;
            v = v.GetVisualParent();
        }
        return null;
    }

    private void OnCardDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var card = FindCardFrom(e.Source as Visual);
        if (card == null) { return; }
        _pressedCard = card;
        _cardPressPoint = e.GetPosition(this);
    }

    private async void OnCardDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cardPressPoint is not Point start || _pressedCard == null) return;
        var pos = e.GetPosition(this);
        if (System.Math.Abs(pos.X - start.X) < 6 && System.Math.Abs(pos.Y - start.Y) < 6) return;

        var card = _pressedCard;
        _cardPressPoint = null;
        _pressedCard = null;
        var dragData = new DataObject();
        dragData.Set("TrelloCard", card);

        // Ghost: render kartu jadi gambar, kartu asli memudar.
        _draggedContainer = FindCardContainer(e.Source as Visual);
        if (_draggedContainer != null && _draggedContainer.Bounds.Width > 0 && _draggedContainer.Bounds.Height > 0)
        {
            var gw = _draggedContainer.Bounds.Width;
            var gh = _draggedContainer.Bounds.Height;
            var bmp = new RenderTargetBitmap(new PixelSize((int)gw, (int)gh), new Vector(96, 96));
            bmp.Render(_draggedContainer);
            _ghostBitmap?.Dispose();
            _ghostBitmap = bmp;
            Part_DragGhost.Width = gw;
            Part_DragGhost.Height = gh;
            Part_DragGhostImage.Source = bmp;
            var startPt = e.GetPosition(Part_RootGrid);
            _ghostTransform = new TranslateTransform(startPt.X - gw / 2, startPt.Y - gh / 2);
            Part_DragGhost.RenderTransform = _ghostTransform;
            Part_DragGhost.IsVisible = true;
            _draggedContainer.Classes.Add("dragging");
        }

        _dragActive = true;
        _dragCancelled = false;
        var res = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
        _dragActive = false;
        Part_DragGhost.IsVisible = false;
        _draggedContainer?.Classes.Remove("dragging");
        _draggedContainer = null;

        // Windows/OLE tidak selalu memanggil event Drop (efek terakhir None saat release).
        // Kalau indikator tujuan masih aktif dan user tidak menekan Escape, pindahkan manual.
        if (res == DragDropEffects.None && !_dragCancelled && _dropCard != null)
        {
            MoveCardToDropTarget(card, _dropCard, _dropBefore);
        }
        ClearDropIndicator();
    }

    private void OnCardDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragActive && e.Key == Key.Escape)
            _dragCancelled = true;
    }

    private void OnCardDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _cardPressPoint = null;
        _pressedCard = null;
    }

    private void OnCardDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _cardPressPoint = null;
        _pressedCard = null;
    }

    private void OnCardDragLeave(object? sender, DragEventArgs e)
    {
    }

    private void ClearDropIndicator()
    {
        _dropCard = null;
        Part_DropLine.IsVisible = false;
    }

    private void ShowDropLine(BMachine.UI.Models.TrelloCard targetCard, bool before)
    {
        // Cari visual CardContainer untuk kartu target.
        if (DataContext is not BMachine.UI.ViewModels.LateCardListViewModel vm) return;
        var container = FindCardContainerFor(targetCard);
        if (container == null) return;
        var pt = container.TranslatePoint(default, Part_RootGrid);
        if (pt == null) return;
        double y = before ? pt.Value.Y - 4 : pt.Value.Y + container.Bounds.Height - 1;
        Part_DropLine.Margin = new Thickness(6, y, 6, 0);
        Part_DropLine.IsVisible = true;
    }

    private Border? FindCardContainerFor(BMachine.UI.Models.TrelloCard card)
    {
        // Cari semua CardContainer, cocokkan DataContext-nya.
        foreach (var c in this.GetVisualDescendants())
        {
            if (c is Border b && b.Name == "CardContainer" && ReferenceEquals(b.DataContext, card))
                return b;
        }
        return null;
    }

    private void OnCardDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (!e.Data.Contains("TrelloCard")) { return; }
        if (sender is not Control target || target.DataContext is not BMachine.UI.Models.TrelloCard targetCard) { return; }
        if (DataContext is not BMachine.UI.ViewModels.LateCardListViewModel vm) { return; }
        if (e.Data.Get("TrelloCard") is not BMachine.UI.Models.TrelloCard dragged) { return; }
        if (dragged == targetCard) { return; }
        if (!vm.Cards.Contains(dragged)) { return; }

        var pos = e.GetPosition(target);
        bool before = pos.Y < target.Bounds.Height / 2;
        e.DragEffects = DragDropEffects.Move;

        if (_ghostTransform != null)
        {
            var pt = e.GetPosition(Part_RootGrid);
            _ghostTransform.X = pt.X - Part_DragGhost.Width / 2;
            _ghostTransform.Y = pt.Y - Part_DragGhost.Height / 2;
        }

        if (_dropCard != targetCard || _dropBefore != before)
        {
            ClearDropIndicator();
            _dropCard = targetCard;
            _dropBefore = before;
            ShowDropLine(targetCard, before);
        }
    }

    private void OnCardDrop(object? sender, DragEventArgs e)
    {
        var target = _dropCard;
        bool before = _dropBefore;
        ClearDropIndicator();
        if (target == null) return;
        if (e.Data.Get("TrelloCard") is not BMachine.UI.Models.TrelloCard dragged) return;
        MoveCardToDropTarget(dragged, target, before);
    }

    private void MoveCardToDropTarget(BMachine.UI.Models.TrelloCard dragged, BMachine.UI.Models.TrelloCard target, bool before)
    {
        if (DataContext is not BMachine.UI.ViewModels.LateCardListViewModel vm) return;
        if (!vm.Cards.Contains(dragged) || !vm.Cards.Contains(target)) return;

        int oldIndex = vm.Cards.IndexOf(dragged);
        int targetIndex = vm.Cards.IndexOf(target);
        int newIndex = before ? targetIndex : targetIndex + 1;
        if (oldIndex < newIndex) newIndex--;
        if (oldIndex == newIndex || newIndex < 0 || newIndex >= vm.Cards.Count) return;

        vm.Cards.Move(oldIndex, newIndex);

        // Hitung Pos Trello dari tetangga (logika sama dengan versi sebelumnya).
        double newPos;
        if (newIndex == 0)
            newPos = vm.Cards.Count > 1 ? vm.Cards[1].Pos / 2.0 : 10000;
        else if (newIndex == vm.Cards.Count - 1)
            newPos = vm.Cards[newIndex - 1].Pos + 10000;
        else
            newPos = (vm.Cards[newIndex - 1].Pos + vm.Cards[newIndex + 1].Pos) / 2.0;

        dragged.Pos = newPos;
        if (vm.UpdateCardPositionCommand.CanExecute(dragged))
            vm.UpdateCardPositionCommand.Execute(dragged);
    }
}

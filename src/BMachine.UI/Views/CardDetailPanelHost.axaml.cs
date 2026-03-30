using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class CardDetailPanelHost : UserControl
{
    private Vector _savedCommentScrollOffset;

    public CardDetailPanelHost()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.AddHandler(
            Avalonia.Controls.Control.RequestBringIntoViewEvent,
            OnRequestBringIntoView,
            RoutingStrategies.Tunnel,
            true);

        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.AddHandler(InputElement.KeyDownEvent, CommentTextBox_KeyDown, RoutingStrategies.Tunnel);
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        this.RemoveHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView);

        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.RemoveHandler(InputElement.KeyDownEvent, CommentTextBox_KeyDown);
            textBox.PropertyChanged -= CommentTextBox_PropertyChanged;
            textBox.RemoveHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            textBox.RemoveHandler(DragDrop.DropEvent, OnAttachDrop);
        }

        var attachButton = this.FindControl<Button>("Part_InlineAttachButton");
        if (attachButton != null)
        {
            attachButton.RemoveHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            attachButton.RemoveHandler(DragDrop.DropEvent, OnAttachDrop);
        }

        if (DataContext is BaseTrelloListViewModel vm)
            vm.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void OnDetailPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as Visual).Properties;
        if (!props.IsRightButtonPressed || DataContext is not BaseTrelloListViewModel vm) return;

        vm.IsDetailPanelOpen = false;
        e.Handled = true;
    }

    private void OnSubPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as Visual).Properties;
        if (!props.IsRightButtonPressed || DataContext is not BaseTrelloListViewModel vm) return;

        vm.IsCommentPanelOpen = false;
        vm.IsChecklistPanelOpen = false;
        vm.IsMovePanelOpen = false;
        vm.IsAttachmentPanelOpen = false;
        if (vm.SelectedCard != null) vm.IsDetailPanelOpen = true;
        e.Handled = true;
    }

    private void OnOpenCommentWindowClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseTrelloListViewModel vm) return;

        var win = new CommentWindow
        {
            DataContext = vm
        };
        win.Show();
        vm.IsCommentPanelOpen = false;
    }

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
        if (e.PropertyName != nameof(BaseTrelloListViewModel.IsLoadingComments)) return;
        if (DataContext is not BaseTrelloListViewModel vm) return;

        var scrollViewer = this.FindControl<ScrollViewer>("Part_CommentScrollViewer");
        if (scrollViewer == null) return;

        if (vm.IsLoadingComments)
        {
            _savedCommentScrollOffset = scrollViewer.Offset;
        }
        else if (_savedCommentScrollOffset.Y > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                scrollViewer.Offset = _savedCommentScrollOffset;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<string>> PickAttachmentFilesAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return Array.Empty<string>();

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
        var text = tb.Text ?? "";
        var caret = tb.CaretIndex;
        vm.HandleCommentTextChanged(text, caret);
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

            if (formats.Contains(DataFormats.Files))
            {
                var data = await topLevel.Clipboard.GetDataAsync(DataFormats.Files);
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
                if (!formats.Contains(format)) continue;
                var data = await topLevel.Clipboard.GetDataAsync(format);
                if (data is byte[] bytes)
                {
                    var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paste_{Guid.NewGuid()}.png");
                    await System.IO.File.WriteAllBytesAsync(tempFile, bytes);
                    vm.AddAttachmentFromPath(tempFile);
                    return;
                }
                if (data is Avalonia.Media.Imaging.Bitmap bitmap)
                {
                    var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paste_{Guid.NewGuid()}.png");
                    bitmap.Save(tempFile);
                    vm.AddAttachmentFromPath(tempFile);
                    return;
                }
            }
        }
        catch { /* Ignore clipboard errors */ }
    }
}

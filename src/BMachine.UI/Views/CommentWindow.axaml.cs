using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class CommentWindow : Window
{
    public CommentWindow()
    {
        InitializeComponent();
    }

    private Vector _savedScrollOffset;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is BaseTrelloListViewModel vm)
        {
            // Wire up file picker func
            vm.PickAttachmentFilesFunc = PickAttachmentFilesAsync;
            
            // Load board members for @mention
            _ = vm.LoadBoardMembers();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.Activated += OnWindowActivated;
        this.Deactivated += OnWindowDeactivated;
        
        this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, CommentTextBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            textBox.PropertyChanged += CommentTextBox_PropertyChanged;
        }

        var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
        if (scrollViewer != null)
        {
            scrollViewer.AddHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        }
        
        // Set up Drag-and-Drop on the attach button area
        var attachButton = this.FindControl<Button>("Part_AttachButton");
        if (attachButton != null)
        {
            DragDrop.SetAllowDrop(attachButton, true);
            attachButton.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            attachButton.AddHandler(DragDrop.DropEvent, OnAttachDrop);
        }
        
        // Also allow drop on the entire textbox area
        if (textBox != null)
        {
            DragDrop.SetAllowDrop(textBox, true);
            textBox.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
            textBox.AddHandler(DragDrop.DropEvent, OnAttachDrop);
        }
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        this.Activated -= OnWindowActivated;
        this.Deactivated -= OnWindowDeactivated;
        this.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerPressed);
        
        var textBox = this.FindControl<TextBox>("Part_CommentTextBox");
        if (textBox != null)
        {
            textBox.RemoveHandler(Avalonia.Input.InputElement.KeyDownEvent, CommentTextBox_KeyDown);
            textBox.PropertyChanged -= CommentTextBox_PropertyChanged;
        }

        base.OnClosed(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        SaveScrollOffset();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_savedScrollOffset.Y > 0)
        {
             Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
                 if (scrollViewer != null)
                 {
                     scrollViewer.Offset = _savedScrollOffset;
                 }
             }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SaveScrollOffset();
    }

    private void SaveScrollOffset()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("CommentScrollViewer");
        if (scrollViewer != null)
        {
            _savedScrollOffset = scrollViewer.Offset;
        }
    }

    // --- Copy (Ctrl+C) fix ---
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
         // Ctrl+V or Cmd+V: Paste image from clipboard
         else if (isCmdOrCtrl && e.Key == Key.V)
         {
             _ = HandleClipboardPaste();
             // Don't mark handled so text paste still works
         }
    }

    // --- @Mention detection on text change ---
    private void CommentTextBox_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty) return;
        if (sender is not TextBox tb || DataContext is not BaseTrelloListViewModel vm) return;
        
        var text = tb.Text ?? "";
        var caret = tb.CaretIndex;
        vm.HandleCommentTextChanged(text, caret);
    }

    // --- File Picker for Attachments ---
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

    // --- Drag-and-Drop for Attachments ---
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
        {
            vm.AddAttachmentFromPath(file.Path.LocalPath);
        }
    }

    // --- Clipboard Paste for Images ---
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
}

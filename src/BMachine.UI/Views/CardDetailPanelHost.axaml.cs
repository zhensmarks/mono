using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia;
using BMachine.UI.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class CardDetailPanelHost : UserControl
{
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
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        this.RemoveHandler(Avalonia.Controls.Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequestBringIntoView(object? sender, Avalonia.Controls.RequestBringIntoViewEventArgs e)
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
            vm.PickAttachmentFilesFunc = PickAttachmentFilesAsync;
        }
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
}

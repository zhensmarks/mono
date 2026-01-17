using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System.Linq;

namespace BMachine.UI.Views;

public partial class FolderLockerView : UserControl
{
    public FolderLockerView()
    {
        InitializeComponent();
        
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only allow if it contains files/folders and COPY operation
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null || !files.Any()) return;

        // Get the first folder found
        var folder = files.FirstOrDefault(f => System.IO.Directory.Exists(f.Path.LocalPath));
        
        if (folder != null && DataContext is FolderLockerViewModel vm)
        {
            vm.SetFolder(folder.Path.LocalPath);
        }
    }

    public void OnSetupPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (point.Properties.IsRightButtonPressed)
        {
            if (DataContext is FolderLockerViewModel vm)
            {
                vm.IsSetupMode = false;
            }
        }
    }
}

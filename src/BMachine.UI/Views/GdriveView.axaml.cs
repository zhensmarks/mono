using Avalonia.Controls;
using Avalonia.Input;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views;

public partial class GdriveView : UserControl
{
    public GdriveView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
         if (e.Data.Contains(DataFormats.Files))
         {
             e.DragEffects = DragDropEffects.Copy; 
         }
         else
         {
             e.DragEffects = DragDropEffects.None;
         }
         e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null && DataContext is GdriveViewModel vm)
            {
               var paths = new System.Collections.Generic.List<string>();
               foreach(var f in files)
               {
                   if (f.Path.IsAbsoluteUri) paths.Add(f.Path.LocalPath);
                   else paths.Add(f.Path.ToString());
               }
               vm.DropFilesCommand.Execute(paths.ToArray());
            }
        }
        e.Handled = true;
    }

    private void SettingsOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GdriveViewModel vm)
        {
            vm.IsSettingsOpen = false;
        }
    }
}

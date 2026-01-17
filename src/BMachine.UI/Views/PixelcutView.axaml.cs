using Avalonia.Controls;
using Avalonia.Input;
using BMachine.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging; // If needed later
using Avalonia.Interactivity;

namespace BMachine.UI.Views;

public partial class PixelcutView : UserControl
{
    public PixelcutView()
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
            if (files != null && DataContext is PixelcutViewModel vm)
            {
               var paths = new System.Collections.Generic.List<string>();
               foreach(var f in files)
               {
                   string localPath = f.Path.IsAbsoluteUri ? f.Path.LocalPath : f.Path.ToString();
                   
                   // Check if Directory
                   if (System.IO.Directory.Exists(localPath))
                   {
                       try
                       {
                           var images = System.IO.Directory.GetFiles(localPath, "*.*", System.IO.SearchOption.AllDirectories);
                           foreach (var img in images)
                           {
                               string ext = System.IO.Path.GetExtension(img).ToLower();
                               if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                               {
                                   paths.Add(img);
                               }
                           }
                       }
                       catch { /* Ignore access errors */ }
                   }
                   else
                   {
                       string ext = System.IO.Path.GetExtension(localPath).ToLower();
                       if (ext == ".png")
                       {
                           // Smart Recovery: If PNG is small (<= 1KB/1024 bytes), try to find source JPG
                           try 
                           {
                               var info = new System.IO.FileInfo(localPath);
                               if (info.Length <= 1024)
                               {
                                   string dir = System.IO.Path.GetDirectoryName(localPath) ?? "";
                                   string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(localPath);
                                   string jpgPath = System.IO.Path.Combine(dir, nameNoExt + ".jpg");
                                   string jpegPath = System.IO.Path.Combine(dir, nameNoExt + ".jpeg");
                                   
                                   if (System.IO.File.Exists(jpgPath)) 
                                   {
                                       paths.Add(jpgPath);
                                       continue; // Skip adding the PNG
                                   }
                                   else if (System.IO.File.Exists(jpegPath))
                                   {
                                       paths.Add(jpegPath);
                                       continue; // Skip adding the PNG
                                   }
                               }
                           }
                           catch { /* Ignore */ }
                       }

                       if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                       {
                           paths.Add(localPath);
                       }
                   }
               }
               vm.DropFilesCommand.Execute(paths.ToArray());
            }
        }
        e.Handled = true;
    }
}

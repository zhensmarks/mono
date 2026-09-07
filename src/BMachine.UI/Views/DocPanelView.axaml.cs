using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DocPanelView : UserControl
{
    public DocPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLogo1Drop(object? sender, Avalonia.Input.DragEventArgs e) => HandleLogoDrop(e, "1");
    private void OnLogo2Drop(object? sender, Avalonia.Input.DragEventArgs e) => HandleLogoDrop(e, "2");

    private void HandleLogoDrop(Avalonia.Input.DragEventArgs e, string slot)
    {
        // BatchVM should be available from DataContext since we inherit it or bind it
        BatchViewModel? batchVm = null;
        
        if (DataContext is DashboardViewModel dashboard)
            batchVm = dashboard.BatchVM;
        else if (DataContext is BatchViewModel vm)
            batchVm = vm;

        if (batchVm == null) return;

        var paths = new List<string>();

        if (e.Data.Contains(Avalonia.Input.DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    try
                    {
                        var path = file.Path?.LocalPath ?? file.Path?.ToString();
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                    catch { /* skip */ }
                }
            }
        }

        if (paths.Count == 0 && e.Data.Contains(Avalonia.Input.DataFormats.FileNames))
        {
            var names = e.Data.GetFileNames();
            if (names != null) paths.AddRange(names);
        }
        
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
            var ext = System.IO.Path.GetExtension(path);
            if (imageExtensions.Any(ext2 => ext.Equals(ext2, StringComparison.OrdinalIgnoreCase)))
            {
                _ = batchVm.ProcessLogoFile(path, slot);
                e.Handled = true; // Stop event from bubbling
                return;
            }
        }
    }

    private void OnDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        e.DragEffects = Avalonia.Input.DragDropEffects.Copy;
        e.Handled = true;
    }
}

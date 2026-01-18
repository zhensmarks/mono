using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BMachine.UI.ViewModels;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using System;

namespace BMachine.UI.Views;

public partial class CompactPixelcutView : UserControl
{
    private DispatcherTimer? _autoScrollTimer;

    public CompactPixelcutView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        FileListBox.SelectionChanged += OnSelectionChanged;
    }
    
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is PixelcutViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PixelcutViewModel.IsProcessing))
        {
            if (DataContext is PixelcutViewModel vm && vm.IsProcessing)
            {
                StartAutoScroll();
            }
            else
            {
                StopAutoScroll();
            }
        }
    }

    private void StartAutoScroll()
    {
        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoScrollTimer.Tick += AutoScrollTick;
        }
        _autoScrollTimer.Start();
    }

    private void StopAutoScroll()
    {
        _autoScrollTimer?.Stop();
    }

    private void AutoScrollTick(object? sender, EventArgs e)
    {
        if (DataContext is PixelcutViewModel vm)
        {
            var processingItem = vm.Files.FirstOrDefault(x => x.IsProcessing);
            if (processingItem != null)
            {
                FileListBox.ScrollIntoView(processingItem);
            }
        }
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
        try
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null && DataContext is PixelcutViewModel vm)
                {
                   var paths = new System.Collections.Generic.List<string>();
                   // Enumeration might throw, so we catch inside loop too, but overall TryCatch is safety net.
                   foreach(var f in files)
                   {
                       try
                       {
                           if (f.Path.IsAbsoluteUri)
                               paths.Add(f.Path.LocalPath);
                           else
                               paths.Add(f.Path.ToString());
                       }
                       catch { }
                   }
                   
                   if (paths.Any())
                   {
                       vm.DropFilesCommand.Execute(paths.ToArray());
                   }
                }
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[CompactPixelcutView] Drop Error: {ex.Message}");
        }
        e.Handled = true;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.AddedItems)
        {
            if (item is BMachine.UI.Models.PixelcutFileItem fileItem)
            {
                fileItem.IsSelected = true;
            }
        }

        foreach (var item in e.RemovedItems)
        {
            if (item is BMachine.UI.Models.PixelcutFileItem fileItem)
            {
                fileItem.IsSelected = false;
            }
        }
    }
}

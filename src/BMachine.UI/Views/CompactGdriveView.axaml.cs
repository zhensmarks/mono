using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using BMachine.UI.ViewModels;
using System;
using System.Linq;

namespace BMachine.UI.Views;

public partial class CompactGdriveView : UserControl
{
    private DispatcherTimer? _autoScrollTimer;

    public CompactGdriveView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        
        // Sync Selection (Match Pixelcut Style)
        FileListBox.SelectionChanged += (s, e) =>
        {
             foreach (var item in e.AddedItems.OfType<BMachine.UI.Models.GdriveFileItem>())
                 item.IsSelected = true;
             foreach (var item in e.RemovedItems.OfType<BMachine.UI.Models.GdriveFileItem>())
                 item.IsSelected = false;
        };
    }


    
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
            
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null && DataContext is GdriveViewModel vm)
                {
                   var paths = new System.Collections.Generic.List<string>();
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
            System.Console.WriteLine($"[CompactGdriveView] Drop Error: {ex.Message}");
        }
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is GdriveViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GdriveViewModel.IsProcessing))
        {
            if (DataContext is GdriveViewModel vm && vm.IsProcessing)
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
            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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
        if (DataContext is GdriveViewModel vm && vm.HasFiles)
        {
            // Find first processing item or last added? 
            // Better to scroll to the one currently "Status=Uploading..."?
            // Since explicit "IsProcessing" isn't on FileItem (check GdriveFileItem model, it doesn't have IsProcessing like PixelcutFileItem might)
            // But GdriveViewModel updates "Status" and "Progress".
            // Let's scroll to the item that is NOT Done and NOT Failed which matches current loop.
            // Or just last item?
            // "Files.Where(j => j.Status == "Ready" || j.IsFailed)" is the loop.
            
            // Simpler: Scroll to the first non-completed item.
            var item = vm.Files.FirstOrDefault(f => !f.IsDone && !f.IsFailed);
            if (item != null)
            {
                 FileListBox.ScrollIntoView(item);
            }
        }
    }

    public static readonly StyledProperty<bool> IsWindowModeProperty =
        AvaloniaProperty.Register<CompactGdriveView, bool>(nameof(IsWindowMode));

    public bool IsWindowMode
    {
        get => GetValue(IsWindowModeProperty);
        set => SetValue(IsWindowModeProperty, value);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                window.BeginMoveDrag(e);
            }
        }
    }

    private void OnCloseWindow(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
         var window = TopLevel.GetTopLevel(this) as Window;
         window?.Close();
    }

    private void OnFileItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is BMachine.UI.Models.GdriveFileItem item)
        {
            if (DataContext is GdriveViewModel vm)
            {
                 // Check Shift Key
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    vm.SelectRange(item);
                }
                else
                {
                    // Handle Left Click for Toggle
                    var props = e.GetCurrentPoint(this).Properties;
                    if (props.IsLeftButtonPressed)
                    {
                        vm.ToggleSelectionCommand.Execute(item);
                    }
                }
            }
        }
    }
}

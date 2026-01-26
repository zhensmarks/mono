using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using PixelcutCompact.ViewModels;
using PixelcutCompact.Services;
using Avalonia;

namespace PixelcutCompact.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Enforce Min Size
        MinWidth = 380;
        MinHeight = 350;

        // Load Window Settings immediately
        LoadWindowSettings();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        
        Closing += OnClosing;
    }

    private void LoadWindowSettings()
    {
        var settings = _settingsService.Load();
        
        if (settings.WindowWidth >= MinWidth) Width = settings.WindowWidth;
        if (settings.WindowHeight >= MinHeight) Height = settings.WindowHeight;
        
        // Restore Position
        if (settings.WindowX != -1 && settings.WindowY != -1)
        {
            Position = new PixelPoint(settings.WindowX, settings.WindowY);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = _settingsService.Load();
        
        settings.IsMaximized = WindowState == WindowState.Maximized;
        
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
        }

        _settingsService.Save(settings);
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
                if (files != null && DataContext is MainWindowViewModel vm)
                {
                   var paths = new System.Collections.Generic.List<string>();
                   foreach(var f in files)
                   {
                       if (f.Path.IsAbsoluteUri)
                           paths.Add(f.Path.LocalPath);
                       else
                           paths.Add(f.Path.ToString());
                   }
                   
                   if (paths.Count > 0)
                   {
                       vm.DropFilesCommand.Execute(paths.ToArray());
                   }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Drop Error: {ex.Message}");
        }
        e.Handled = true;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs e)
    {
         Close();
    }

    private void OnFileItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is PixelcutCompact.Models.PixelcutFileItem item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Check Shift Key using KeyModifiers
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    vm.SelectRange(item);
                }
                else
                {
                    // To ensure standard ToggleSelection works even if we bypass Button
                    // We only want to handle left click though
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

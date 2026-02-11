using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PixelcutCompact.ViewModels;
using System.Diagnostics;
using System.IO;

namespace PixelcutCompact.Views;

public partial class GalleryWindow : Window
{
    public GalleryWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Handled by ListBoxItem automatically for selection
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GalleryItemViewModel item)
        {
             OpenPreview(item);
        }
    }

    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GalleryItemViewModel item)
        {
            OpenPreview(item);
        }
    }

    private void OnOpenPhotoshopClick(object? sender, RoutedEventArgs e)
    {
        // TODO: Fix this - command doesn't exist yet
        // if (DataContext is MainWindowViewModel vm)
        // {
        //     vm.OpenSelectedInPhotoshopCommand.Execute(null);
        // }
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GalleryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
            {
                var folder = Path.GetDirectoryName(item.FilePath);
                if (folder != null)
                {
                    Process.Start("explorer.exe", folder);
                }
            }
        }
    }
    
    
    private void OpenPreview(GalleryItemViewModel item)
    {
        // Use MainWindowViewModel's OpenFullPreviewWindow command
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenFullPreviewWindowCommand.Execute(item.ParentItem);
        }
    }
}

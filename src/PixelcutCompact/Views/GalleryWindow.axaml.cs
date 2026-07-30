using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using PixelcutCompact.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
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

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GalleryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
            {
                var folder = Path.GetDirectoryName(item.FilePath);
                if (folder != null)
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
            }
        }
    }

    /// <summary>Show hover overlay smoothly via Opacity (no scale transform — lightweight).</summary>
    private void OnTilePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Visual v)
        {
            var overlay = v.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TileHoverOverlay");
            if (overlay != null) overlay.Opacity = 1;
        }
    }

    /// <summary>Hide hover overlay on pointer exit.</summary>
    private void OnTilePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Visual v)
        {
            var overlay = v.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TileHoverOverlay");
            if (overlay != null) overlay.Opacity = 0;
        }
    }

    private void OpenPreview(GalleryItemViewModel item)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenFullPreviewWindowCommand.Execute(item.ParentItem);
        }
    }
}

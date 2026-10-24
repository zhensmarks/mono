using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;

namespace FrameStudio.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly PhotoFileService _photoFiles = new();
    private Bitmap? _templateCompositeBitmap;
    private Point? _panStart;
    private double _panOriginX;
    private double _panOriginY;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _templateCompositeBitmap?.Dispose();
        _viewModel.SelectedPhoto = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Zoom) or nameof(MainViewModel.Rotation) or nameof(MainViewModel.OffsetX) or nameof(MainViewModel.OffsetY) or nameof(MainViewModel.IsFitMode))
            UpdatePreviewTransform();
        if (e.PropertyName == nameof(MainViewModel.TemplatePreviewPng))
            UpdateTemplateCompositePreview();
    }

    private void UpdateTemplateCompositePreview()
    {
        _templateCompositeBitmap?.Dispose();
        _templateCompositeBitmap = null;
        TemplateCompositeImage.Source = null;

        if (_viewModel.TemplatePreviewPng is not { Length: > 0 } png)
            return;

        try
        {
            using var stream = new MemoryStream(png, writable: false);
            _templateCompositeBitmap = new Bitmap(stream);
            TemplateCompositeImage.Source = _templateCompositeBitmap;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            _viewModel.Status = $"Composite PSD dibaca, tetapi pratinjau gambar gagal dibuka: {exception.Message}";
        }
    }

    private async void OnChooseMaster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih PSD master",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Photoshop document") { Patterns = new[] { "*.psd", "*.psb" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0)
            return;

        var path = GetLocalPath(files[0]);
        if (path is null)
        {
            _viewModel.Status = "File master tidak bisa dibaca dari penyimpanan ini.";
            return;
        }

        _viewModel.SetMaster(path);
        await _viewModel.InspectSelectedMasterAsync();
    }

    private async void OnChooseSourceFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder source foto",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        var folder = GetLocalPath(folders[0]);
        if (folder is null || !Directory.Exists(folder))
        {
            _viewModel.Status = "Folder source tidak bisa dibaca.";
            return;
        }

        _viewModel.SourcePath = folder;
        var files = Directory.EnumerateFiles(folder)
            .Where(_photoFiles.IsSupported)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        await AddPhotoFilesAsync(files);
        _viewModel.Status = files.Length == 0
            ? "Folder source dipilih, tetapi belum ada foto JPG, PNG, BMP, atau WebP di tingkat teratas."
            : $"{files.Length} foto dimasukkan ke antrean dari folder source.";
    }

    private async void OnChooseOutputFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder hasil",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        var folder = GetLocalPath(folders[0]);
        if (folder is null)
        {
            _viewModel.Status = "Folder hasil tidak bisa digunakan.";
            return;
        }

        _viewModel.OutputPath = folder;
        _viewModel.Status = "Folder hasil dipilih. Ekspor masih menunggu mesin PSD.";
    }

    private async void OnChoosePhotos(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih foto source",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Foto") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" } },
                FilePickerFileTypes.ImageAll
            }
        });

        var paths = files.Select(GetLocalPath).Where(path => path is not null).Cast<string>().ToArray();
        await AddPhotoFilesAsync(paths);
        if (paths.Length == 0 && files.Count > 0)
            _viewModel.Status = "Foto dipilih, tetapi lokasinya tidak dapat dibaca.";
    }

    private async Task AddPhotoFilesAsync(IEnumerable<string> paths)
    {
        var validPaths = paths.Where(_photoFiles.IsSupported).ToArray();
        var loaded = await Task.Run(() => validPaths.Select(_photoFiles.Load).ToArray());
        foreach (var photo in loaded)
            _viewModel.AddSourcePhoto(photo);
    }

    private static string? GetLocalPath(IStorageItem item) => item.Path.IsFile ? item.Path.LocalPath : null;

    private void OnQueueSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PhotoQueue.SelectedItem is SourcePhoto photo)
            _viewModel.SelectedPhoto = photo;
    }

    private void OnSlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is FrameSlot slot)
            _viewModel.SelectSlot(slot);
        e.Handled = true;
    }

    private void OnAssignPhoto(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.AssignSelectedPhoto(_viewModel.SelectedSlot))
            _viewModel.Status = _viewModel.SelectedPhoto is null
                ? "Pilih foto dari antrean terlebih dahulu."
                : "Pilih slot foto XL atau S sebelum memasangkan foto.";
    }

    private void OnClearQueue(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.ClearQueue();

    private async void OnExportFlattenedPreview(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await _viewModel.ExportFlattenedPreviewAsync();

    private void OnPreviewTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel.HasTextCandidateSelected)
            _viewModel.Status = "Teks disunting untuk pratinjau lokal saja; PSD tidak diubah.";
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.TryGetFiles()?.Any(file => _photoFiles.IsSupported(file.Path.LocalPath)) == true;
        var hasPathText = e.DataTransfer.TryGetText() is { } text && _photoFiles.IsSupported(text);
        e.DragEffects = hasFiles || hasPathText ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.Path.LocalPath).ToList() ?? new List<string>();
        if (paths.Count == 0 && e.DataTransfer.TryGetText() is { } text && _photoFiles.IsSupported(text))
            paths.Add(text);

        await AddPhotoFilesAsync(paths);
        if (paths.Count == 0)
        {
            _viewModel.Status = "Tidak ada foto yang didukung dalam drop tersebut.";
            e.Handled = true;
            return;
        }

        if (_viewModel.SelectedSlot is { } slot && (slot.IsPhoto || slot.IsLogo))
            _viewModel.AssignSelectedPhoto(_viewModel.SelectedSlot);
        else
            _viewModel.Status = "Foto ditambahkan ke antrean. Pilih slot foto/logo lalu tekan Pasangkan.";
        e.Handled = true;
    }

    private void OnRotateLeft(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.Rotation -= 5;
    private void OnRotateRight(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.Rotation += 5;
    private void OnResetCrop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.ResetCrop();
    private void OnFillFrame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.SetPreviewMode(showWholePhoto: false);
        ZoomSlider.Value = _viewModel.Zoom;
    }

    private void OnFitFrame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.SetPreviewMode(showWholePhoto: true);
        ZoomSlider.Value = _viewModel.Zoom;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.HasSelectedPhoto)
            return;
        _panStart = e.GetPosition(PhotoFrame);
        _panOriginX = _viewModel.OffsetX;
        _panOriginY = _viewModel.OffsetY;
        e.Pointer.Capture(PreviewImage);
        e.Handled = true;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panStart is not { } start || e.Pointer.Captured != PreviewImage)
            return;
        var point = e.GetPosition(PhotoFrame);
        _viewModel.OffsetX = _panOriginX + point.X - start.X;
        _viewModel.OffsetY = _panOriginY + point.Y - start.Y;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == PreviewImage)
            e.Pointer.Capture(null);
        _panStart = null;
    }

    private void UpdatePreviewTransform()
    {
        PreviewImage.Stretch = _viewModel.IsFitMode ? Stretch.Uniform : Stretch.UniformToFill;
        PreviewImage.RenderTransformOrigin = RelativePoint.Center;
        PreviewImage.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_viewModel.Zoom, _viewModel.Zoom),
                new RotateTransform(_viewModel.Rotation),
                new TranslateTransform(_viewModel.OffsetX, _viewModel.OffsetY)
            }
        };
    }
}

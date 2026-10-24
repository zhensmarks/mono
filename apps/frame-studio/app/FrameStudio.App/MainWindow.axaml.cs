using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.App.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FrameStudio.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly PhotoFileService _photoFiles = new();
    private readonly RasterPngComposer _rasterComposer = new();
    private readonly PngOutputWriter _pngOutputWriter = new();
    private Bitmap? _templateCompositeBitmap;
    private Point? _photoPanStart;
    private double _photoPanOriginX;
    private double _photoPanOriginY;
    private Point? _artboardPanStart;
    private FrameSlot? _artboardPanSlot;
    private double _artboardPanOriginX;
    private double _artboardPanOriginY;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupPsdPath)
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Slots.CollectionChanged += OnSlotsChanged;
        TemplateArtboard.PointerPressed += OnArtboardPointerPressed;
        TemplateArtboard.PointerMoved += OnArtboardPointerMoved;
        TemplateArtboard.PointerReleased += OnArtboardPointerReleased;
        Closed += (_, _) =>
        {
            _templateCompositeBitmap?.Dispose();
            foreach (var photo in _viewModel.SourcePhotos)
                photo.Dispose();
        };
        Opened += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(startupPsdPath))
                return;
            if (!File.Exists(startupPsdPath))
            {
                _viewModel.Status = "The selected local PSD path was not found. No file was changed.";
                return;
            }

            _viewModel.SetMaster(startupPsdPath);
            await _viewModel.InspectSelectedMasterAsync();
        };
        _viewModel.SelectedPhoto = null;
        UpdatePreviewTransform();
        RebuildArtboard();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Zoom) or nameof(MainViewModel.Rotation) or nameof(MainViewModel.OffsetX) or nameof(MainViewModel.OffsetY) or nameof(MainViewModel.IsFitMode) or nameof(MainViewModel.SelectedPhoto))
            UpdatePreviewTransform();

        if (e.PropertyName == nameof(MainViewModel.TemplatePreviewPng))
            UpdateTemplateCompositePreview();
        else if (e.PropertyName is nameof(MainViewModel.TemplatePreviewNotice) or nameof(MainViewModel.SelectedSlot) or nameof(MainViewModel.SelectedPhoto) or nameof(MainViewModel.Zoom) or nameof(MainViewModel.Rotation) or nameof(MainViewModel.OffsetX) or nameof(MainViewModel.OffsetY) or nameof(MainViewModel.IsFitMode) or nameof(MainViewModel.DocumentWidth) or nameof(MainViewModel.DocumentHeight))
            RebuildArtboard();
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (FrameSlot slot in e.OldItems)
                slot.PropertyChanged -= OnSlotPropertyChanged;
        if (e.NewItems is not null)
            foreach (FrameSlot slot in e.NewItems)
                slot.PropertyChanged += OnSlotPropertyChanged;
        RebuildArtboard();
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FrameSlot.Photo) or nameof(FrameSlot.Value) or nameof(FrameSlot.Zoom) or nameof(FrameSlot.Rotation) or nameof(FrameSlot.OffsetX) or nameof(FrameSlot.OffsetY) or nameof(FrameSlot.IsFitMode) or nameof(FrameSlot.IsSelected))
        {
            if (sender is FrameSlot slot && ReferenceEquals(slot, _viewModel.SelectedSlot))
                UpdatePreviewTransform();
            RebuildArtboard();
        }
    }

    private void UpdateTemplateCompositePreview()
    {
        var previousBitmap = _templateCompositeBitmap;
        _templateCompositeBitmap = null;
        TemplateCompositeImage.Source = null;
        previousBitmap?.Dispose();

        if (_viewModel.TemplatePreviewPng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png, writable: false);
                _templateCompositeBitmap = new Bitmap(stream);
                TemplateCompositeImage.Source = _templateCompositeBitmap;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                _viewModel.Status = $"Composite PSD terbaca, tetapi pratinjau tidak dapat dibuka: {exception.Message}";
            }
        }

        RebuildArtboard();
    }

    private void RebuildArtboard()
    {
        if (TemplateArtboard is null)
            return;

        TemplateArtboard.Children.Clear();

        if (_templateCompositeBitmap is null || _viewModel.DocumentWidth <= 0 || _viewModel.DocumentHeight <= 0)
        {
            TemplateArtboard.Width = 640;
            TemplateArtboard.Height = 420;
            TemplateArtboard.Background = PaletteBrush("CanvasBackgroundBrush", "#0C0C0E");
            var message = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_viewModel.TemplatePreviewNotice)
                    ? "Pilih PSD master untuk menampilkan composite."
                    : _viewModel.TemplatePreviewNotice,
                Width = 380,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = PaletteBrush("TextMutedBrush", "#71717A"),
                FontSize = 13
            };
            Canvas.SetLeft(message, 130);
            Canvas.SetTop(message, 185);
            TemplateArtboard.Children.Add(message);
            return;
        }

        var documentWidth = _viewModel.DocumentWidth;
        var documentHeight = _viewModel.DocumentHeight;
        TemplateArtboard.Width = documentWidth;
        TemplateArtboard.Height = documentHeight;
        TemplateArtboard.Background = Brushes.Transparent;
        TemplateCompositeImage.Width = documentWidth;
        TemplateCompositeImage.Height = documentHeight;
        TemplateCompositeImage.Stretch = Stretch.Fill;
        TemplateCompositeImage.Source = _templateCompositeBitmap;
        Canvas.SetLeft(TemplateCompositeImage, 0);
        Canvas.SetTop(TemplateCompositeImage, 0);
        TemplateArtboard.Children.Add(TemplateCompositeImage);

        foreach (var slot in _viewModel.Slots)
        {
            if (slot.Bounds is not { Width: > 0, Height: > 0 } bounds)
                continue;

            var x = Math.Max(0, bounds.X);
            var y = Math.Max(0, bounds.Y);
            if (x >= documentWidth || y >= documentHeight)
                continue;
            var width = Math.Min(bounds.Width - (x - bounds.X), documentWidth - x);
            var height = Math.Min(bounds.Height - (y - bounds.Y), documentHeight - y);
            if (width <= 0 || height <= 0)
                continue;

            Border? contentFrame = null;
            if ((slot.IsPhoto || slot.IsLogo) && slot.Photo?.Image is { } assignedImage)
            {
                var image = new Image
                {
                    Source = assignedImage,
                    Width = width,
                    Height = height,
                    Stretch = slot.IsLogo || slot.IsFitMode ? Stretch.Uniform : Stretch.UniformToFill,
                    RenderTransformOrigin = RelativePoint.Center,
                    RenderTransform = BuildSlotTransform(slot, width, height)
                };
                contentFrame = new Border
                {
                    Width = width,
                    Height = height,
                    ClipToBounds = true,
                    Background = Brushes.Transparent,
                    Child = image
                };
            }
            else if (slot.IsText && !string.IsNullOrWhiteSpace(slot.Value))
            {
                var text = new TextBlock
                {
                    Text = slot.Value,
                    Width = Math.Max(1, width - 12),
                    MaxHeight = height,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    FontSize = Math.Clamp(height * 0.28, 9, 160),
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush.Parse("#FFFFFF"),
                    RenderTransformOrigin = RelativePoint.Center,
                    RenderTransform = BuildSlotTransform(slot, width, height)
                };
                contentFrame = new Border
                {
                    Width = width,
                    Height = height,
                    ClipToBounds = true,
                    Background = Brushes.Transparent,
                    Child = text
                };
            }
            if (contentFrame is not null)
            {
                Canvas.SetLeft(contentFrame, x);
                Canvas.SetTop(contentFrame, y);
                TemplateArtboard.Children.Add(contentFrame);
            }

            var guide = new Border
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                BorderBrush = PaletteBrush("AccentBrush", "#D7F06B"),
                BorderThickness = slot.IsSelected ? new Thickness(3) : new Thickness(1),
                Opacity = slot.IsSelected ? 1 : 0.42,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(guide, x);
            Canvas.SetTop(guide, y);
            TemplateArtboard.Children.Add(guide);
        }
    }

    private static TransformGroup BuildSlotTransform(FrameSlot slot, double width, double height) => new()
    {
        Children =
        {
            new ScaleTransform(slot.Zoom, slot.Zoom),
            new RotateTransform(slot.Rotation),
            new TranslateTransform(slot.OffsetX * width, slot.OffsetY * height)
        }
    };

    private static IBrush PaletteBrush(string key, string fallback)
    {
        if (Application.Current is { } application && application.TryGetResource(key, null, out var resource) && resource is IBrush brush)
            return brush;
        return Brush.Parse(fallback);
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
        if (!_viewModel.CanEditPhotoTextLogo)
            return;

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
            : $"{files.Length} foto dimasukkan ke antrean source.";
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
        _viewModel.Status = "Folder hasil dipilih. Ekspor membuat salinan PNG baru dan tidak menimpa file yang ada.";
    }

    private async void OnChoosePhotos(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.CanEditPhotoTextLogo)
            return;

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
        if (!_viewModel.CanEditPhotoTextLogo)
            return;

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
        if (_viewModel.CanEditPhotoTextLogo && sender is Control control && control.DataContext is FrameSlot slot)
            _viewModel.SelectSlot(slot);
        e.Handled = true;
    }

    private void OnAssignPhoto(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.CanPlaceSelectedSlot)
        {
            _viewModel.Status = "Slot foto belum disiapkan. Penempatan nonaktif.";
            return;
        }

        if (!_viewModel.AssignSelectedPhoto(_viewModel.SelectedSlot))
            _viewModel.Status = _viewModel.SelectedPhoto is null
                ? "Pilih foto dari antrean terlebih dahulu."
                : "Pilih area foto yang sudah memiliki batas valid.";
    }

    private void OnClearQueue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.ClearQueue();
        PhotoQueue.SelectedItem = null;
        RebuildArtboard();
    }

    private async void OnExportPngCopies(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.CanExportPngCopies)
            return;

        if (_viewModel.IsFallbackPreviewMode)
        {
            _viewModel.Status = "Ekspor nonaktif dalam mode pratinjau hanya-baca.";
            return;
        }

        if (_viewModel.DocumentWidth <= 0 || _viewModel.DocumentHeight <= 0)
        {
            _viewModel.Status = "Ukuran artboard belum tersedia; ekspor dibatalkan.";
            return;
        }

        var targetSlot = _viewModel.SelectedSlot is { IsPlacementReady: true } selectedSlot && (selectedSlot.IsPhoto || selectedSlot.IsLogo)
            ? selectedSlot
            : _viewModel.Slots.FirstOrDefault(slot => slot.IsPlacementReady && (slot.IsPhoto || slot.IsLogo));
        if (_viewModel.SourcePhotos.Count > 0 && targetSlot is null)
        {
            _viewModel.Status = "Slot foto belum disiapkan. Ekspor nonaktif.";
            return;
        }
        if (_viewModel.SourcePhotos.Count > 0 && targetSlot is { IsPlacementReady: false })
        {
            _viewModel.Status = "Area foto tidak memiliki batas tervalidasi. Ekspor dibatalkan.";
            return;
        }
        var unrenderableAssignment = _viewModel.Slots.FirstOrDefault(slot =>
            (slot.IsPhoto || slot.IsLogo) && slot.Photo is not null && !slot.IsPlacementReady);
        if (unrenderableAssignment is not null)
        {
            _viewModel.Status = $"Penempatan pada {unrenderableAssignment.Label} tidak memiliki batas tervalidasi; ekspor dibatalkan.";
            return;
        }

        var previousSelectedPhoto = _viewModel.SelectedPhoto;
        var previousAssignedPhoto = targetSlot?.Photo;
        var photosToRender = targetSlot is not null && _viewModel.SourcePhotos.Count > 0
            ? _viewModel.SourcePhotos.Cast<SourcePhoto?>().ToArray()
            : new SourcePhoto?[] { targetSlot?.Photo };
        var written = 0;
        _viewModel.SetExporting(true);
        try
        {
            Directory.CreateDirectory(_viewModel.OutputPath);
            foreach (var photo in photosToRender)
            {
                if (targetSlot is not null && photo is not null)
                {
                    targetSlot.Photo = photo;
                    _viewModel.SelectedPhoto = photo;
                }

                RebuildArtboard();
                await Task.Yield();
                SaveRenderedPng(photo);
                written++;
                _viewModel.Status = $"Mengekspor {written} dari {photosToRender.Length} PNG…";
            }

            _viewModel.Status = _viewModel.IsFallbackPreviewMode
                ? "Separate stored-composite PNG copy saved. The master PSD was not changed."
                : written == 1
                    ? "PNG output saved separately. The master PSD was not changed."
                    : $"{written} PNG outputs saved separately. The master PSD was not changed.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            _viewModel.Status = "PNG output failed. The master PSD was not changed.";
        }
        catch (OutOfMemoryException)
        {
            _viewModel.Status = "Not enough memory to render the PNG output. The master PSD was not changed.";
        }
        finally
        {
            if (targetSlot is not null)
                targetSlot.Photo = previousAssignedPhoto;
            _viewModel.SelectedPhoto = previousSelectedPhoto;
            _viewModel.SetExporting(false);
            RebuildArtboard();
        }
    }

    private void SaveRenderedPng(SourcePhoto? photo)
    {
        if (_viewModel.TemplatePreviewPng is not { Length: > 0 } storedComposite)
            throw new InvalidOperationException("Composite tersimpan tidak tersedia.");
        var composedPng = _rasterComposer.Compose(storedComposite, _viewModel.Slots);
        var masterName = SanitizeFileName(Path.GetFileNameWithoutExtension(_viewModel.MasterPath));
        if (string.IsNullOrWhiteSpace(masterName))
            masterName = "frame-studio";
        var photoName = photo is null ? "preview" : SanitizeFileName(Path.GetFileNameWithoutExtension(photo.FileName));
        if (string.IsNullOrWhiteSpace(photoName))
            photoName = "output";
        var stem = $"{masterName}-{photoName}";

        _pngOutputWriter.WriteNewPng(_viewModel.OutputPath, stem, composedPng, _viewModel.MasterPath);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return safe.TrimEnd('.', ' ');
    }

    private void OnPreviewTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel.CanEditPhotoTextLogo && _viewModel.HasTextCandidateSelected)
            _viewModel.Status = "Teks akan dirender sebagai overlay PNG; layer teks PSD asli tetap utuh.";
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (!_viewModel.CanPlaceSelectedSlot)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var hasFiles = e.DataTransfer.TryGetFiles()?.Any(file => _photoFiles.IsSupported(file.Path.LocalPath)) == true;
        var hasPathText = e.DataTransfer.TryGetText() is { } text && _photoFiles.IsSupported(text);
        e.DragEffects = hasFiles || hasPathText ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (!_viewModel.CanPlaceSelectedSlot)
        {
            e.Handled = true;
            return;
        }

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

        if (_viewModel.SelectedSlot is { IsPlacementReady: true } slot && (slot.IsPhoto || slot.IsLogo))
            _viewModel.AssignSelectedPhoto(slot);
        else
            _viewModel.Status = "Slot foto belum disiapkan. Penempatan nonaktif.";
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
        if (!_viewModel.CanPlaceSelectedSlot || !_viewModel.HasSelectedPhoto)
            return;
        _photoPanStart = e.GetPosition(PhotoFrame);
        _photoPanOriginX = _viewModel.OffsetX;
        _photoPanOriginY = _viewModel.OffsetY;
        e.Pointer.Capture(PreviewImage);
        e.Handled = true;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_photoPanStart is not { } start || e.Pointer.Captured != PreviewImage)
            return;
        var point = e.GetPosition(PhotoFrame);
        var width = Math.Max(1, PhotoFrame.Bounds.Width);
        var height = Math.Max(1, PhotoFrame.Bounds.Height);
        _viewModel.OffsetX = Math.Clamp(_photoPanOriginX + (point.X - start.X) / width, -2, 2);
        _viewModel.OffsetY = Math.Clamp(_photoPanOriginY + (point.Y - start.Y) / height, -2, 2);
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == PreviewImage)
            e.Pointer.Capture(null);
        _photoPanStart = null;
    }

    private void OnArtboardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(TemplateArtboard);
        var slot = _viewModel.Slots.LastOrDefault(item => item.Bounds is { } bounds
            && point.X >= bounds.X && point.X <= bounds.X + bounds.Width
            && point.Y >= bounds.Y && point.Y <= bounds.Y + bounds.Height);
        if (slot is null)
            return;

        _viewModel.SelectSlot(slot);
        if ((slot.IsPhoto || slot.IsLogo) && slot.IsPlacementReady)
        {
            _artboardPanSlot = slot;
            _artboardPanStart = point;
            _artboardPanOriginX = slot.OffsetX;
            _artboardPanOriginY = slot.OffsetY;
            e.Pointer.Capture(TemplateArtboard);
        }
        e.Handled = true;
    }

    private void OnArtboardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_artboardPanStart is not { } start || _artboardPanSlot?.Bounds is not { } bounds || e.Pointer.Captured != TemplateArtboard)
            return;
        var point = e.GetPosition(TemplateArtboard);
        _artboardPanSlot.OffsetX = Math.Clamp(_artboardPanOriginX + (point.X - start.X) / bounds.Width, -2, 2);
        _artboardPanSlot.OffsetY = Math.Clamp(_artboardPanOriginY + (point.Y - start.Y) / bounds.Height, -2, 2);
    }

    private void OnArtboardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == TemplateArtboard)
            e.Pointer.Capture(null);
        _artboardPanStart = null;
        _artboardPanSlot = null;
    }

    private void UpdatePreviewTransform()
    {
        if (PreviewImage is null)
            return;
        PreviewImage.Stretch = _viewModel.IsFitMode ? Stretch.Uniform : Stretch.UniformToFill;
        PreviewImage.RenderTransformOrigin = RelativePoint.Center;
        PreviewImage.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_viewModel.Zoom, _viewModel.Zoom),
                new RotateTransform(_viewModel.Rotation),
                new TranslateTransform(_viewModel.OffsetX * Math.Max(1, PhotoFrame.Bounds.Width), _viewModel.OffsetY * Math.Max(1, PhotoFrame.Bounds.Height))
            }
        };
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using FrameStudio.App.Models;
using FrameStudio.App.Services;
using SkiaSharp;

namespace FrameStudio.App;

public partial class FrameToolPrototypeWindow : Window
{
    private enum InteractionMode
    {
        None,
        Rectangle,
        Polygon
    }

    private readonly FrameToolPrototypeRenderer _renderer = new();
    private readonly List<NormalizedFramePoint> _polygonPoints = [];
    private FrameMask? _mask = FrameMask.ExamplePolygon();
    private FrameInsertionPoint _insertionPoint = FrameInsertionPoint.BetweenBackgroundAndForeground;
    private InteractionMode _interactionMode;
    private bool _isFrameConfirmed = true;
    private bool _suppressControlEvents;
    private Point? _rectangleStart;
    private Point? _panStart;
    private double _panOriginX;
    private double _panOriginY;
    private double _offsetX;
    private double _offsetY;
    private SKBitmap? _photo;
    private Bitmap? _artboardBitmap;
    private byte[]? _lastRenderedPng;

    public FrameToolPrototypeWindow()
    {
        InitializeComponent();
        _photo = FrameToolPrototypeRenderer.CreateSyntheticPhoto();
        SavedFramesList.SelectionChanged += (_, _) => UseSavedFrameButton.IsEnabled = SavedFramesList.SelectedItem is SavedFramePreset;
        Closed += (_, _) =>
        {
            _photo?.Dispose();
            _artboardBitmap?.Dispose();
        };

        UpdateFrameStatus();
        UpdatePreview();
    }

    private void OnChooseRectangle(object? sender, RoutedEventArgs e)
    {
        BeginDrawing(InteractionMode.Rectangle);
        ShapeInstructions.Text = "Tarik satu sudut ke sudut lain pada artboard. Konfirmasikan bentuk setelah selesai.";
        CanvasStatusText.Text = "Mode gambar persegi panjang · tarik di kanvas.";
    }

    private void OnChoosePolygon(object? sender, RoutedEventArgs e)
    {
        BeginDrawing(InteractionMode.Polygon);
        ShapeInstructions.Text = "Klik sedikitnya tiga titik di kanvas, lalu pilih Selesaikan poligon dan konfirmasikan.";
        CanvasStatusText.Text = "Mode gambar poligon · klik titik sudut, lalu selesaikan.";
    }

    private void BeginDrawing(InteractionMode mode)
    {
        _interactionMode = mode;
        _mask = null;
        _isFrameConfirmed = false;
        _rectangleStart = null;
        _polygonPoints.Clear();
        DrawingOverlay.Children.Clear();
        UpdateFrameStatus();
        UpdatePreview();
    }

    private void OnFinishPolygon(object? sender, RoutedEventArgs e)
    {
        if (_interactionMode != InteractionMode.Polygon || _polygonPoints.Count < 3)
        {
            CanvasStatusText.Text = "Poligon perlu sedikitnya tiga titik.";
            return;
        }

        try
        {
            _mask = FrameMask.Polygon(_polygonPoints);
            _interactionMode = InteractionMode.None;
            _polygonPoints.Clear();
            DrawingOverlay.Children.Clear();
            _isFrameConfirmed = false;
            ShapeInstructions.Text = "Poligon sudah terbentuk. Konfirmasikan bingkai untuk mengaktifkan crop dan ekspor PNG.";
            CanvasStatusText.Text = "Poligon siap dikonfirmasi.";
            UpdateFrameStatus();
            UpdatePreview();
        }
        catch (ArgumentException)
        {
            CanvasStatusText.Text = "Titik poligon belum membentuk area yang valid; atur ulang titiknya.";
        }
    }

    private void OnConfirmFrame(object? sender, RoutedEventArgs e)
    {
        if (_mask is not { IsValid: true } || !Enum.IsDefined(_insertionPoint))
        {
            CanvasStatusText.Text = "Buat mask persegi atau poligon dan pilih posisi sisip yang didukung terlebih dahulu.";
            return;
        }

        _isFrameConfirmed = true;
        _interactionMode = InteractionMode.None;
        DrawingOverlay.Children.Clear();
        ShapeInstructions.Text = "Bingkai terkonfirmasi. Tarik foto di kanvas untuk mengatur posisi crop.";
        CanvasStatusText.Text = $"{_mask.ShapeLabel} terkonfirmasi · {_mask.Points.Count} titik · posisi layer tervalidasi.";
        UpdateFrameStatus();
        UpdatePreview();
    }

    private void OnInsertionPointChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressControlEvents || InsertionPointCombo is null)
            return;

        _insertionPoint = InsertionPointCombo.SelectedIndex switch
        {
            0 => FrameInsertionPoint.BetweenBackgroundAndForeground,
            1 => FrameInsertionPoint.AboveForeground,
            _ => (FrameInsertionPoint)(-1)
        };
        _isFrameConfirmed = false;
        UpdateFrameStatus();
        UpdatePreview();
        CanvasStatusText.Text = Enum.IsDefined(_insertionPoint)
            ? "Posisi layer berubah; konfirmasikan bingkai lagi sebelum ekspor PNG."
            : "Posisi layer ini belum didukung.";
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(CanvasInputSurface);
        if (_interactionMode == InteractionMode.Rectangle)
        {
            _rectangleStart = point;
            e.Pointer.Capture(CanvasInputSurface);
            UpdateDraftOverlay(point);
            e.Handled = true;
            return;
        }

        if (_interactionMode == InteractionMode.Polygon)
        {
            _polygonPoints.Add(ToNormalized(point));
            UpdateDraftOverlay(point);
            FinishPolygonButton.IsEnabled = _polygonPoints.Count >= 3;
            CanvasStatusText.Text = $"{_polygonPoints.Count} titik poligon · tambahkan sudut atau selesaikan.";
            e.Handled = true;
            return;
        }

        if (_isFrameConfirmed && _mask is { IsValid: true } && _photo is not null)
        {
            _panStart = point;
            _panOriginX = _offsetX;
            _panOriginY = _offsetY;
            e.Pointer.Capture(CanvasInputSurface);
            e.Handled = true;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(CanvasInputSurface);
        if (_interactionMode == InteractionMode.Rectangle && _rectangleStart is { } rectangleStart
            && e.Pointer.Captured == CanvasInputSurface)
        {
            SetDraftRectangle(rectangleStart, point);
            UpdateDraftOverlay(point);
            return;
        }

        if (_interactionMode == InteractionMode.Polygon)
        {
            UpdateDraftOverlay(point);
            return;
        }

        if (_panStart is { } panStart && e.Pointer.Captured == CanvasInputSurface && _mask is { IsValid: true })
        {
            var width = Math.Max(0.01, _mask.Points.Max(item => item.X) - _mask.Points.Min(item => item.X));
            var height = Math.Max(0.01, _mask.Points.Max(item => item.Y) - _mask.Points.Min(item => item.Y));
            _offsetX = Math.Clamp(_panOriginX + (point.X - panStart.X) / (CanvasInputSurface.Bounds.Width * width), -2, 2);
            _offsetY = Math.Clamp(_panOriginY + (point.Y - panStart.Y) / (CanvasInputSurface.Bounds.Height * height), -2, 2);
            UpdatePreview();
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(CanvasInputSurface);
        if (_interactionMode == InteractionMode.Rectangle && _rectangleStart is { } rectangleStart)
        {
            SetDraftRectangle(rectangleStart, point);
            _rectangleStart = null;
            _interactionMode = InteractionMode.None;
            DrawingOverlay.Children.Clear();
            _isFrameConfirmed = false;
            ShapeInstructions.Text = "Persegi panjang terbentuk. Konfirmasikan bingkai untuk melanjutkan.";
            CanvasStatusText.Text = _mask is null ? "Frame terlalu kecil; coba tarik area yang lebih besar." : "Persegi panjang siap dikonfirmasi.";
            UpdateFrameStatus();
            UpdatePreview();
        }

        if (e.Pointer.Captured == CanvasInputSurface)
            e.Pointer.Capture(null);
        _panStart = null;
    }

    private void SetDraftRectangle(Point first, Point second)
    {
        try
        {
            _mask = FrameMask.Rectangle(ToNormalized(first), ToNormalized(second));
        }
        catch (ArgumentException)
        {
            _mask = null;
        }
        _isFrameConfirmed = false;
        UpdateFrameStatus();
        UpdatePreview();
    }

    private void UpdateDraftOverlay(Point cursor)
    {
        DrawingOverlay.Children.Clear();
        var points = new List<Point>();
        if (_interactionMode == InteractionMode.Rectangle && _rectangleStart is { } start)
        {
            points.Add(start);
            points.Add(new Point(cursor.X, start.Y));
            points.Add(cursor);
            points.Add(new Point(start.X, cursor.Y));
        }
        else if (_interactionMode == InteractionMode.Polygon)
        {
            points.AddRange(_polygonPoints.Select(point => new Point(point.X * CanvasInputSurface.Bounds.Width, point.Y * CanvasInputSurface.Bounds.Height)));
            if (_polygonPoints.Count > 0)
                points.Add(cursor);
        }

        if (points.Count < 2)
            return;

        var line = new Polyline
        {
            Stroke = Brush.Parse("#D7F06B"),
            StrokeThickness = 2.5
        };
        foreach (var item in points)
            line.Points.Add(item);
        if (_interactionMode == InteractionMode.Rectangle || _polygonPoints.Count >= 3)
            line.Points.Add(points[0]);
        DrawingOverlay.Children.Add(line);

        if (_interactionMode == InteractionMode.Polygon)
        {
            foreach (var item in points.Take(_polygonPoints.Count))
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = Brush.Parse("#D7F06B") };
                Canvas.SetLeft(dot, item.X - dot.Width / 2);
                Canvas.SetTop(dot, item.Y - dot.Height / 2);
                DrawingOverlay.Children.Add(dot);
            }
        }
    }

    private static NormalizedFramePoint ToNormalized(Point point) => new(
        Math.Clamp(point.X / FrameToolPrototypeRenderer.ArtboardWidth, 0, 1),
        Math.Clamp(point.Y / FrameToolPrototypeRenderer.ArtboardHeight, 0, 1));

    private void OnCropControlChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ZoomValueText is null || RotationValueText is null || _suppressControlEvents)
            return;

        ZoomValueText.Text = $"{ZoomSlider.Value:P0}";
        RotationValueText.Text = $"{RotationSlider.Value:0}°";
        UpdatePreview();
    }

    private void OnResetCrop(object? sender, RoutedEventArgs e)
    {
        _offsetX = 0;
        _offsetY = 0;
        _suppressControlEvents = true;
        ZoomSlider.Value = 1;
        RotationSlider.Value = 0;
        _suppressControlEvents = false;
        UpdatePreview();
        CanvasStatusText.Text = "Crop diatur ulang.";
    }

    private void OnChoosePhoto(object? sender, RoutedEventArgs e) => _ = ChoosePhotoAsync();

    private async Task ChoosePhotoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih foto untuk bingkai",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Foto") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp"] },
                FilePickerFileTypes.ImageAll
            ]
        });

        if (files.Count == 0)
            return;
        var path = files[0].Path.IsFile ? files[0].Path.LocalPath : null;
        if (path is null)
        {
            CanvasStatusText.Text = "Pilih foto dari penyimpanan lokal.";
            return;
        }
        LoadPhoto(path);
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        var hasFile = e.DataTransfer.Formats.Contains(DataFormat.File)
            && e.DataTransfer.TryGetFiles()?.Any(item => item.Path.IsFile) == true;
        e.DragEffects = hasFile ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? e.DataTransfer.TryGetFiles()?.FirstOrDefault(item => item.Path.IsFile)?.Path.LocalPath
            : null;
        if (path is not null)
            LoadPhoto(path);
        else
            CanvasStatusText.Text = "Drop satu file foto lokal yang didukung.";
        e.Handled = true;
    }

    private void LoadPhoto(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (!new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            CanvasStatusText.Text = "Format foto tidak didukung. Gunakan JPG, PNG, BMP, atau WebP.";
            return;
        }

        try
        {
            var nextPhoto = SKBitmap.Decode(path);
            if (nextPhoto is null || nextPhoto.Width <= 0 || nextPhoto.Height <= 0)
            {
                nextPhoto?.Dispose();
                CanvasStatusText.Text = "Foto tidak dapat dibuka sebagai gambar.";
                return;
            }

            _photo?.Dispose();
            _photo = nextPhoto;
            PhotoNameText.Text = System.IO.Path.GetFileName(path);
            _offsetX = 0;
            _offsetY = 0;
            OnResetCrop(null, new RoutedEventArgs());
            CanvasStatusText.Text = _isFrameConfirmed
                ? $"{System.IO.Path.GetFileName(path)} dimuat · ditampilkan melalui mask aktif."
                : $"{System.IO.Path.GetFileName(path)} dimuat · konfirmasikan bingkai sebelum ekspor.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CanvasStatusText.Text = $"Foto tidak dapat dibaca: {exception.Message}";
        }
    }

    private async void OnExportPng(object? sender, RoutedEventArgs e)
    {
        if (!_isFrameConfirmed || _mask is not { IsValid: true } || _photo is null || !Enum.IsDefined(_insertionPoint) || _lastRenderedPng is null)
        {
            CanvasStatusText.Text = "Konfirmasikan mask dan posisi layer sebelum ekspor PNG.";
            return;
        }

        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Ekspor PNG pratinjau",
            SuggestedFileName = "bingkai-foto-preview.png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"], MimeTypes = ["image/png"] }]
        });
        if (output is null)
            return;

        if (!output.Path.IsFile)
        {
            CanvasStatusText.Text = "PNG hanya dapat disimpan ke lokasi file lokal.";
            return;
        }

        try
        {
            // Save the exact PNG bytes currently shown by ArtboardImage; no rectangle-only export route exists.
            await File.WriteAllBytesAsync(output.Path.LocalPath, _lastRenderedPng);
            CanvasStatusText.Text = $"PNG preview disimpan · {_mask.ShapeLabel}, {_mask.Points.Count} titik mask. PSD tidak ditulis.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CanvasStatusText.Text = $"PNG gagal disimpan: {exception.Message}";
        }
    }

    private void OnSaveFrame(object? sender, RoutedEventArgs e)
    {
        if (!_isFrameConfirmed || _mask is not { IsValid: true } || !Enum.IsDefined(_insertionPoint))
        {
            CanvasStatusText.Text = "Konfirmasikan mask dan posisi layer sebelum menyimpan bingkai.";
            return;
        }

        var preset = new SavedFramePreset($"Bingkai {SavedFramesList.Items.Count + 1:00}", _mask, _insertionPoint);
        SavedFramesList.Items.Add(preset);
        SavedFramesList.SelectedItem = preset;
        CanvasStatusText.Text = $"{preset.Name} disimpan untuk dipakai ulang selama sesi prototype ini.";
    }

    private void OnUseSavedFrame(object? sender, RoutedEventArgs e)
    {
        if (SavedFramesList.SelectedItem is not SavedFramePreset preset)
            return;

        _suppressControlEvents = true;
        InsertionPointCombo.SelectedIndex = preset.InsertionPoint == FrameInsertionPoint.BetweenBackgroundAndForeground ? 0 : 1;
        _suppressControlEvents = false;
        _mask = preset.Mask;
        _insertionPoint = preset.InsertionPoint;
        _isFrameConfirmed = true;
        _interactionMode = InteractionMode.None;
        DrawingOverlay.Children.Clear();
        UpdateFrameStatus();
        UpdatePreview();
        CanvasStatusText.Text = $"{preset.Name} diterapkan ulang.";
    }

    private void OnResetDemo(object? sender, RoutedEventArgs e)
    {
        _suppressControlEvents = true;
        InsertionPointCombo.SelectedIndex = 0;
        ZoomSlider.Value = 1;
        RotationSlider.Value = 0;
        _suppressControlEvents = false;
        _photo?.Dispose();
        _photo = FrameToolPrototypeRenderer.CreateSyntheticPhoto();
        _mask = FrameMask.ExamplePolygon();
        _insertionPoint = FrameInsertionPoint.BetweenBackgroundAndForeground;
        _isFrameConfirmed = true;
        _interactionMode = InteractionMode.None;
        _rectangleStart = null;
        _polygonPoints.Clear();
        _offsetX = 0;
        _offsetY = 0;
        SavedFramesList.Items.Clear();
        DrawingOverlay.Children.Clear();
        PhotoNameText.Text = "Contoh ilustratif bawaan";
        ShapeInstructions.Text = "Tarik pada artboard untuk membentuk bingkai. Foto contoh sudah terpotong mengikuti poligon.";
        UpdateFrameStatus();
        UpdatePreview();
        CanvasStatusText.Text = "Demo diatur ulang · artboard dan foto sintetis.";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void UpdateFrameStatus()
    {
        var validInsertion = Enum.IsDefined(_insertionPoint);
        ConfirmFrameButton.IsEnabled = !_isFrameConfirmed && _mask is { IsValid: true } && validInsertion;
        FinishPolygonButton.IsEnabled = _interactionMode == InteractionMode.Polygon && _polygonPoints.Count >= 3;
        ExportPngButton.IsEnabled = _isFrameConfirmed && _mask is { IsValid: true } && _photo is not null && validInsertion;

        if (_mask is null)
            FrameStateText.Text = _interactionMode == InteractionMode.Polygon
                ? $"Menggambar poligon · {_polygonPoints.Count} titik"
                : "Gambar area bingkai pada kanvas";
        else
            FrameStateText.Text = _isFrameConfirmed
                ? $"{_mask.ShapeLabel} · terkonfirmasi · {_mask.Points.Count} titik"
                : $"{_mask.ShapeLabel} · menunggu konfirmasi · {_mask.Points.Count} titik";
    }

    private void UpdatePreview()
    {
        if (ArtboardImage is null)
            return;

        var png = _renderer.Render(
            _mask,
            _photo,
            ZoomSlider?.Value ?? 1,
            RotationSlider?.Value ?? 0,
            _offsetX,
            _offsetY,
            _insertionPoint);
        _lastRenderedPng = png;

        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var nextBitmap = new Bitmap(stream);
            var previousBitmap = _artboardBitmap;
            _artboardBitmap = nextBitmap;
            ArtboardImage.Source = nextBitmap;
            previousBitmap?.Dispose();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            CanvasStatusText.Text = $"Preview artboard tidak dapat dibuka: {exception.Message}";
        }

        UpdateFrameStatus();
    }
}

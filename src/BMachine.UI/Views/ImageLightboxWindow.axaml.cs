using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System;
using Avalonia.VisualTree;
using ImageMagick;

namespace BMachine.UI.Views;

public partial class ImageLightboxWindow : Window
{
    private static Avalonia.PixelPoint? _lastWindowPosition;
    private static double? _lastWindowWidth;
    private static double? _lastWindowHeight;

    private bool _isDragging = false;
    private Avalonia.Point _lastDragPoint;
    private Avalonia.Media.ScaleTransform? _scaleTransform;

    public ImageLightboxWindow()
    {
        InitializeComponent();
        
        var transformControl = this.FindControl<Avalonia.Controls.Control>("ImageTransformControl") as Avalonia.Controls.LayoutTransformControl;
        if (transformControl != null)
        {
            _scaleTransform = transformControl.LayoutTransform as Avalonia.Media.ScaleTransform;
        }

        this.Opened += (s, e) =>
        {
            if (Avalonia.Application.Current?.TryFindResource("SolidBackgroundFillColorBaseBrush", null, out var themeBg) == true &&
                themeBg is Avalonia.Media.IBrush brush)
            {
                this.Background = brush;
            }
            else
            {
                var isDark = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
                this.Background = Avalonia.Media.SolidColorBrush.Parse(isDark ? "#1A1A1A" : "#F3F4F6");
            }

            if (_lastWindowPosition.HasValue)
                this.Position = _lastWindowPosition.Value;
            
            if (_lastWindowWidth.HasValue && _lastWindowHeight.HasValue)
            {
                this.Width = _lastWindowWidth.Value;
                this.Height = _lastWindowHeight.Value;
            }
        };

        this.Closing += (s, e) =>
        {
            if (this.WindowState == WindowState.Normal)
            {
                _lastWindowPosition = this.Position;
                _lastWindowWidth = this.Bounds.Width;
                _lastWindowHeight = this.Bounds.Height;
            }
        };

        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        if (scroller != null)
        {
            scroller.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, OnImagePointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // scroller.SizeChanged += (s, e) => UpdateCenterMargin(); - Removed, now using XAML declarative centering
        }
    }

    public ImageLightboxWindow(Bitmap image) : this()
    {
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl != null)
        {
            imgControl.Source = image;
            InitInitialScale(image.Size);
        }
    }

    public ImageLightboxWindow(string imageUrl) : this()
    {
        _ = LoadImageAsync(imageUrl);
    }

    // UpdateCenterMargin is removed because it's now handled declaratively in XAML.

    private void InitInitialScale(Avalonia.Size bitmapSize)
    {
        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        if (scroller != null && _scaleTransform != null && bitmapSize.Width > 0)
        {
            double vw = scroller.Viewport.Width > 0 ? scroller.Viewport.Width : 800;
            double vh = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : 600;

            double sx = vw / bitmapSize.Width;
            double sy = vh / bitmapSize.Height;
            double initialScale = Math.Min(1.0, Math.Min(sx, sy));

            _scaleTransform.ScaleX = initialScale;
            _scaleTransform.ScaleY = initialScale;

            // Margin centering is handled by the Border MinWidth/MinHeight layout in XAML
        }
    }

    private async Task LoadImageAsync(string url)
    {
        var spinner = this.FindControl<Control>("LoadingSpinner");
        var imgControl = this.FindControl<Image>("FullScreenImage");
        
        if (spinner != null) spinner.IsVisible = true;

        try
        {
            var cleanUrl = url;
            string apiKey = "";
            string token = "";

            var matchKey = System.Text.RegularExpressions.Regex.Match(url, @"[?&]key=([^&]+)");
            var matchToken = System.Text.RegularExpressions.Regex.Match(url, @"[?&]token=([^&]+)");

            if (matchKey.Success && matchToken.Success)
            {
                apiKey = matchKey.Groups[1].Value;
                token = matchToken.Groups[1].Value;
            }

            using var client = new HttpClient();
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(token) && url.Contains("trello.com"))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"OAuth oauth_consumer_key=\"{apiKey}\", oauth_token=\"{token}\"");
            }

            var bytes = await client.GetByteArrayAsync(cleanUrl);
            var bitmap = TryDecodeBitmap(bytes);
            
            if (imgControl != null)
            {
                imgControl.Source = bitmap;
                InitInitialScale(bitmap.Size);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lightbox] Error loading image: {ex.Message}");
        }
        finally
        {
            if (spinner != null) spinner.IsVisible = false;
        }
    }

    private void OnBackgroundPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void OnImagePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl == null) return;

        var point = e.GetCurrentPoint(imgControl);
        if (point.Properties.IsRightButtonPressed)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastDragPoint = e.GetPosition(this);
            e.Pointer.Capture(imgControl); 
            e.Handled = true;
        }
    }

    private static Bitmap TryDecodeBitmap(byte[] bytes)
    {
        try
        {
            using var directStream = new MemoryStream(bytes);
            return new Bitmap(directStream);
        }
        catch
        {
            using var magick = new MagickImage(bytes);
            magick.AutoOrient();

            using var output = new MemoryStream();
            magick.Format = MagickFormat.Png;
            magick.Write(output);
            output.Position = 0;
            return new Bitmap(output);
        }
    }

    private void OnImagePointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isDragging) return;

        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        if (scroller == null) return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _lastDragPoint;
        _lastDragPoint = currentPoint;

        double newOffsetX = scroller.Offset.X - delta.X;
        double newOffsetY = scroller.Offset.Y - delta.Y;

        double maxOffsetX = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        double maxOffsetY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);

        newOffsetX = Math.Clamp(newOffsetX, 0, maxOffsetX);
        newOffsetY = Math.Clamp(newOffsetY, 0, maxOffsetY);

        scroller.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);
        e.Handled = true;
    }

    private void OnImagePointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnImagePointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        if (_scaleTransform == null || scroller == null) return;

        bool isZoomModifier = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || 
                              e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta) ||
                              e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);

        if (!isZoomModifier)
        {
            return;
        }

        double zoomFactor = e.Delta.Y > 0 ? 1.15 : (1.0 / 1.15);
        PerformZoom(zoomFactor, e.GetPosition(this.FindControl<Control>("ImageTransformControl")));
        e.Handled = true; 
    }

    private void OnImagePinch(object? sender, Avalonia.Input.PinchEventArgs e)
    {
        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        var transformControl = this.FindControl<Control>("ImageTransformControl");
        if (scroller == null || transformControl == null) return;

        var originTranslated = new Avalonia.Point(e.ScaleOrigin.X + scroller.Offset.X, e.ScaleOrigin.Y + scroller.Offset.Y);
        PerformZoom(e.Scale, originTranslated);
        e.Handled = true;
    }

    private void PerformZoom(double zoomFactor, Avalonia.Point cursorRelativePosition)
    {
        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        var img = this.FindControl<Image>("FullScreenImage");
        if (_scaleTransform == null || scroller == null || img == null) return;
        double imgWidth = img.Bounds.Width;
        double imgHeight = img.Bounds.Height;
        if (imgWidth == 0 || imgHeight == 0)
        {
            if (img.Source is Bitmap bm)
            {
                imgWidth = bm.Size.Width;
                imgHeight = bm.Size.Height;
            }
            else return;
        }

        double vw = scroller.Viewport.Width > 0 ? scroller.Viewport.Width : 800;
        double vh = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : 600;

        double fitScaleX = vw / imgWidth;
        double fitScaleY = vh / imgHeight;
        double minScale = Math.Min(1.0, Math.Min(fitScaleX, fitScaleY));

        double newScaleX = _scaleTransform.ScaleX * zoomFactor;

        if (zoomFactor < 1.0)
        {
            if (_scaleTransform.ScaleX <= minScale + 0.001) return; 
            if (newScaleX < minScale)
            {
                newScaleX = minScale;
                zoomFactor = newScaleX / _scaleTransform.ScaleX; 
            }
        }
        else if (zoomFactor > 1.0 && newScaleX > 20)
        {
            return;
        }

        double newScaleY = _scaleTransform.ScaleY * zoomFactor;

        double oldOffsetX = scroller.Offset.X;
        double oldOffsetY = scroller.Offset.Y;

        _scaleTransform.ScaleX = newScaleX;
        _scaleTransform.ScaleY = newScaleY;

        // Margin update relies on declarative XAML bindings now
        scroller.UpdateLayout();

        double newOffsetX = oldOffsetX + cursorRelativePosition.X * (zoomFactor - 1);
        double newOffsetY = oldOffsetY + cursorRelativePosition.Y * (zoomFactor - 1);

        double maxOffsetX = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        double maxOffsetY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);

        newOffsetX = Math.Clamp(newOffsetX, 0, maxOffsetX);
        newOffsetY = Math.Clamp(newOffsetY, 0, maxOffsetY);

        scroller.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl == null || imgControl.Source == null) return;

        var storageProvider = this.StorageProvider;
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Attachment Image",
            DefaultExtension = "png",
            SuggestedFileName = "Attachment_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            try
            {
                using var stream = await file.OpenWriteAsync();
                var bitmap = (Bitmap)imgControl.Source;
                bitmap.Save(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lightbox] Failed to save image: {ex.Message}");
            }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            Close();
        }
    }
}

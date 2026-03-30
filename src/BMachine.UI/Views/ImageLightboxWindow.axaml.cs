using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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

    private static readonly object _logLock = new object();
    private static readonly string _logPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BMachine", "lightbox.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock (_logLock)
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // ignore logging failures
        }
    }

    private bool _isDragging = false;
    private bool _isClosing;
    private Avalonia.Point _lastDragPoint;
    private Avalonia.Media.ScaleTransform? _scaleTransform;
    private string _apiKey = "";
    private string _token = "";

    public ImageLightboxWindow()
    {
        InitializeComponent();
        Log("ctor");

        // Initialize _scaleTransform from AXAML-defined RenderTransform
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl?.RenderTransform is Avalonia.Media.ScaleTransform st)
            _scaleTransform = st;

        this.Opened += OnWindowOpened;

        this.Closing += (s, e) =>
        {
            _isClosing = true;
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
            // De-risk: no pointer wheel zoom and no viewport size syncing.
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Log("opened");
        if (Avalonia.Application.Current?.TryFindResource("AppBackgroundBrush", null, out var themeBg) == true &&
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

    }

    /// <summary>
    /// Keeps scroll content at least viewport-sized (centers small images) without markup bindings to Viewport
    /// (those bindings can create layout feedback loops and crash on Windows).
    /// </summary>
    private void SyncViewportHostSize()
    {
        var scroller = this.FindControl<ScrollViewer>("ImageScroller");
        var host = this.FindControl<Border>("ViewportHost");
        if (scroller == null || host == null) return;

        double w = scroller.Viewport.Width;
        double h = scroller.Viewport.Height;
        if (w <= 1 || double.IsNaN(w) || double.IsInfinity(w)) w = 400;
        if (h <= 1 || double.IsNaN(h) || double.IsInfinity(h)) h = 300;

        host.MinWidth = w;
        host.MinHeight = h;
    }

    public ImageLightboxWindow(Bitmap image) : this()
    {
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl != null)
        {
            imgControl.Source = image;
        }
    }

    public ImageLightboxWindow(string imageUrl) : this()
    {
        _ = LoadImageAsync(imageUrl);
    }

    public ImageLightboxWindow(string imageUrl, string apiKey, string token) : this()
    {
        _apiKey = apiKey ?? "";
        _token = token ?? "";
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
            Log($"load-start len={(url?.Length ?? 0)}");

            // Download on a background thread to avoid native HTTP crashes on UI thread
            Log("download-bg-start");
            var bytes = await Task.Run(async () =>
            {
                try
                {
                    using var handler = new System.Net.Http.HttpClientHandler
                    {
                        AllowAutoRedirect = true
                    };
                    using var client = new HttpClient(handler);
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BMachine/1.0");
                    
                    // Use OAuth Authorization header for Trello (query params get stripped on redirects)
                    if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_token))
                    {
                        client.DefaultRequestHeaders.Add("Authorization", 
                            $"OAuth oauth_consumer_key=\"{_apiKey}\", oauth_token=\"{_token}\"");
                    }
                    
                    Log("getasync-start");
                    // Use default ResponseContentRead (NOT ResponseHeadersRead which causes native crashes)
                    using var response = await client.GetAsync(url);
                    Log($"getasync-status={response.StatusCode}");
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"http-fail: {(int)response.StatusCode} {response.ReasonPhrase}");
                        return null;
                    }
                    
                    var data = await response.Content.ReadAsByteArrayAsync();
                    Log($"downloaded bytes={data.Length}");
                    return data;
                }
                catch (Exception httpEx)
                {
                    Log($"http-error: {httpEx.Message}");
                    return null;
                }
            });
            
            if (_isClosing || bytes == null || bytes.Length == 0) return;

            // Limit download size
            const int maxDownloadBytes = 20_000_000; // 20MB
            if (bytes.Length > maxDownloadBytes)
            {
                Log($"too-large: {bytes.Length}");
                return;
            }

            Log("decode-start");
            Bitmap bitmap;
            try
            {
                bitmap = await Task.Run(() => TryDecodeBitmap(bytes));
            }
            catch (Exception dex)
            {
                Log($"decode-managed-error: {dex}");
                throw;
            }
            Log($"decode-done px={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosing) return;
                try
                {
                    Log("set-image-start");
                    if (imgControl != null)
                    {
                        imgControl.Source = bitmap;
                        // Auto-fit image to window viewport
                        InitInitialScale(new Avalonia.Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
                    }
                    Log("set-image-ok");
                }
                catch (Exception setEx)
                {
                    Console.WriteLine($"[Lightbox] Set image failed: {setEx.Message}");
                    Log($"set-image-failed: {setEx}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lightbox] Error loading image: {ex.Message}");
            Log($"load-error: {ex}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (spinner != null) spinner.IsVisible = false;
            });
        }
    }

    private void OnBackgroundPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed)
            {
                Close();
                e.Handled = true;
                return;
            }

            // Only allow window drag from the Grid background, not from child controls
            if (point.Properties.IsLeftButtonPressed && e.Source == sender)
            {
                try
                {
                    this.BeginMoveDrag(e);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lightbox] BeginMoveDrag: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lightbox] OnBackgroundPointerPressed: {ex.Message}");
        }
    }

    private void OnImagePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lightbox] OnImagePointerPressed: {ex.Message}");
        }
    }

    private static Bitmap TryDecodeBitmap(byte[] bytes)
    {
        // De-risk decode: try Avalonia direct decode first, then fallback to ImageMagick.
        // If ImageMagick or direct decode triggers a native crash, logs around decode-start/decode-done will show where it happens.
        const int maxBytesForReducedEdge = 12_000_000;
        const int edgeMax = 2048;
        const int edgeReduced = 1536;
        int edge = bytes.Length > maxBytesForReducedEdge ? edgeReduced : edgeMax;

        try
        {
            using var directStream = new MemoryStream(bytes);
            var bmp = new Bitmap(directStream);
            if (bmp.PixelSize.Width > edge || bmp.PixelSize.Height > edge)
            {
                double w = bmp.PixelSize.Width;
                double h = bmp.PixelSize.Height;
                double scale = Math.Min(edge / w, edge / h);
                int nw = Math.Max(1, (int)(w * scale));
                int nh = Math.Max(1, (int)(h * scale));
                var scaled = bmp.CreateScaledBitmap(new PixelSize(nw, nh), BitmapInterpolationMode.HighQuality);
                bmp.Dispose();
                return scaled;
            }
            return bmp;
        }
        catch
        {
            using var magick = new MagickImage(bytes);
            magick.AutoOrient();
            if (magick.Width > edge || magick.Height > edge)
            {
                magick.Resize(new MagickGeometry((uint)edge, (uint)edge) { Greater = true });
            }

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
        try
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
            var imgControl = this.FindControl<Image>("FullScreenImage");
            if (imgControl == null) return;
            PerformZoom(zoomFactor, e.GetPosition(imgControl));
            e.Handled = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lightbox] OnImagePointerWheelChanged: {ex.Message}");
        }
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

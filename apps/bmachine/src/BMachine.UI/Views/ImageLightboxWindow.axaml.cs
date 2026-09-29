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

    private bool _isClosing;
    private string _apiKey = "";
    private string _token = "";
    private bool _isDragging = false;
    private Avalonia.Point _lastDragPoint;

    public ImageLightboxWindow()
    {
        InitializeComponent();
        Log("ctor");

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
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl == null) return;
        var point = e.GetCurrentPoint(imgControl);
        if (point.Properties.IsRightButtonPressed) { Close(); e.Handled = true; return; }
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastDragPoint = e.GetPosition(this);
            e.Pointer.Capture(imgControl);
            e.Handled = true;
        }
    }

    private void OnImagePointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isDragging) return;
        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _lastDragPoint;
        _lastDragPoint = currentPoint;

        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl?.RenderTransform is Avalonia.Media.TransformGroup tg)
        {
            foreach (var t in tg.Children)
            {
                if (t is Avalonia.Media.TranslateTransform trans)
                {
                    trans.X += delta.X;
                    trans.Y += delta.Y;
                }
            }
        }
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
        var imgControl = this.FindControl<Image>("FullScreenImage");
        if (imgControl?.RenderTransform is not Avalonia.Media.TransformGroup tg) return;
        
        Avalonia.Media.ScaleTransform? st = null;
        Avalonia.Media.TranslateTransform? tt = null;
        foreach (var t in tg.Children)
        {
            if (t is Avalonia.Media.ScaleTransform scaleT) st = scaleT;
            if (t is Avalonia.Media.TranslateTransform transT) tt = transT;
        }
        if (st == null || tt == null) return;

        double zoomFactor = e.Delta.Y > 0 ? 1.15 : (1.0 / 1.15);
        double newScaleX = st.ScaleX * zoomFactor;
        
        if (newScaleX < 0.2) newScaleX = 0.2;
        if (newScaleX > 20) newScaleX = 20;
        
        zoomFactor = newScaleX / st.ScaleX;

        var currentPoint = e.GetPosition(imgControl.Parent as Avalonia.Visual ?? this);
        var bounds = imgControl.Bounds;
        var centerX = bounds.X + bounds.Width / 2 + tt.X;
        var centerY = bounds.Y + bounds.Height / 2 + tt.Y;

        double dx = currentPoint.X - centerX;
        double dy = currentPoint.Y - centerY;

        tt.X -= dx * (zoomFactor - 1);
        tt.Y -= dy * (zoomFactor - 1);

        st.ScaleX = newScaleX;
        st.ScaleY = newScaleX;

        e.Handled = true;
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

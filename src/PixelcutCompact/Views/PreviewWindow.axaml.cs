using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using PixelcutCompact.Services;

namespace PixelcutCompact.Views;

public partial class PreviewWindow : Window
{
    private PreviewWindowSettings _settings;
    
    // Store paths for Photoshop/Photopea integration
    private string _originalPath = "";
    private string _resultPath = "";
    

    
    // Embedded JSX Script (for Photoshop)
    private const string JsxScript = @"
#target photoshop
var args = [];
if (typeof arguments !== 'undefined' && arguments.length > 0) {
    for (var i = 0; i < arguments.length; i++) { args.push(arguments[i]); }
} else if ($.args && $.args.length > 0) { args = $.args; }

var tempFile = new File(Folder.temp + '/pixelcut_edit_args.txt');
var pngPath = ''; var jpgPath = '';

if (tempFile.exists) {
    tempFile.open('r');
    pngPath = tempFile.readln();
    jpgPath = tempFile.readln();
    tempFile.close(); tempFile.remove();
}

if (pngPath && jpgPath) {
    try {
        var pngFile = new File(pngPath);
        if (pngFile.exists) {
            var doc = app.open(pngFile);
            if (doc.artLayers.length > 0) { doc.artLayers[0].name = 'Result (PNG)'; }
            var jpgFile = new File(jpgPath);
            if (jpgFile.exists) {
                var jpgDoc = app.open(jpgFile);
                jpgDoc.selection.selectAll();
                jpgDoc.activeLayer.copy();
                jpgDoc.close(SaveOptions.DONOTSAVECHANGES);
                app.activeDocument = doc;
                doc.paste();
                // doc.activeLayer.merge(); // Do NOT merge
                doc.activeLayer = doc.artLayers[0];
            }
        }
        // REMOVED ALERT: alert('Ready for editing!...');
    } catch (e) { alert('Error: ' + e.message); }
} else { alert('No files specified.'); }
";

    public static readonly StyledProperty<double> RotationOriginalProperty =
        AvaloniaProperty.Register<PreviewWindow, double>(nameof(RotationOriginal));

    public double RotationOriginal
    {
        get => GetValue(RotationOriginalProperty);
        set => SetValue(RotationOriginalProperty, value);
    }

    public static readonly StyledProperty<double> RotationResultProperty =
        AvaloniaProperty.Register<PreviewWindow, double>(nameof(RotationResult));

    public double RotationResult
    {
        get => GetValue(RotationResultProperty);
        set => SetValue(RotationResultProperty, value);
    }

    public event EventHandler? Next;
    public event EventHandler? Previous;
    


    private void OnNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Next?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviousClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Previous?.Invoke(this, EventArgs.Empty);
    }

    public PreviewWindow()
    {
        InitializeComponent();
        
        RotationOriginal = 0;
        RotationResult = 0;

        _settings = PreviewWindowSettings.Load();
        if (_settings.X != -1 && _settings.Y != -1)
        {
            Position = new PixelPoint((int)_settings.X, (int)_settings.Y);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        
        Width = _settings.Width;
        Height = _settings.Height;
        

        
        // Find Control and set value
        var zoomControl = this.FindControl<NumericUpDown>("ZoomControl");
        if (zoomControl != null) zoomControl.Value = (decimal)Math.Max(0.2, _settings.Zoom);

        Closing += (s, e) =>
        {
            _settings.X = Position.X;
            _settings.Y = Position.Y;
            _settings.Width = Width;
            _settings.Height = Height;
            var zc = this.FindControl<NumericUpDown>("ZoomControl");
            if (zc != null && zc.Value.HasValue) _settings.Zoom = (double)zc.Value.Value;
        };
    }

    /// <summary>Tampilkan ghost loading overlay langsung, untuk dipanggil sebelum navigasi.</summary>
    public void ShowLoading()
    {
        var overlay = this.FindControl<Grid>("OverlayLoading");
        if (overlay != null) overlay.IsVisible = true;
    }

    public async void LoadImages(string originalPath, string resultPath, string? title = null)
    {
        var overlay = this.FindControl<Grid>("OverlayLoading");
        if (overlay != null) overlay.IsVisible = true;
        
        // Flush UI agar overlay pasti render duluan
        await System.Threading.Tasks.Task.Delay(80);
        
        // Store paths
        _originalPath = originalPath;
        _resultPath = resultPath;
        
        try
        {
            if (!string.IsNullOrEmpty(title))
            {
                 var label = this.FindControl<TextBlock>("TxtTitle");
                 if (label != null) label.Text = title;
                 Title = title;
            }

            var imgOriginal = this.FindControl<Image>("ImgOriginal");
            var imgResult = this.FindControl<Image>("ImgResult");

            // Sembunyikan gambar lama dulu agar tidak "flicker"
            if (imgOriginal != null) imgOriginal.Source = null;
            if (imgResult != null) imgResult.Source = null;

            Bitmap? origBitmap = null;
            Bitmap? resultBitmap = null;

            await System.Threading.Tasks.Task.Run(() => 
            {
                if (File.Exists(originalPath)) 
                    origBitmap = LoadBitmapWithOrientation(originalPath);
                
                if (File.Exists(resultPath)) 
                    resultBitmap = LoadBitmapWithOrientation(resultPath);
            });

            if (imgOriginal != null && origBitmap != null) 
                imgOriginal.Source = origBitmap;
            
            if (imgResult != null && resultBitmap != null) 
                imgResult.Source = resultBitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading preview: {ex.Message}");
        }
        finally
        {
            if (overlay != null) overlay.IsVisible = false;
        }
    }

    // ========================
    // EXISTING FUNCTIONALITY
    // ========================

    private Bitmap LoadBitmapWithOrientation(string path)
    {
        // 1. Check orientation
        int orientation = 1;
        try { orientation = PixelcutCompact.Helpers.ExifHelper.GetOrientation(path); } catch { }

        // 2. Load bitmap efficiently (downscale if too large to save memory on low-spec PCs)
        Bitmap bitmap;
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 1 * 1024 * 1024) // > 1MB
            {
                using (var stream = File.OpenRead(path))
                {
                    // Decode to max 1280 width to save memory
                    bitmap = Bitmap.DecodeToWidth(stream, 1280);
                }
            }
            else
            {
                bitmap = new Bitmap(path);
            }
        }
        catch
        {
            bitmap = new Bitmap(path);
        }

        // 3. If no rotation needed, return
        if (orientation == 1) return bitmap;

        // 4. Transform if needed
        if (orientation == 6 || orientation == 8 || orientation == 3)
        {
             // Calculate new dimensions
             var w = bitmap.PixelSize.Width;
             var h = bitmap.PixelSize.Height;
             
             double angle = 0;
             if (orientation == 6) angle = 90;
             else if (orientation == 8) angle = -90; // 270
             else if (orientation == 3) angle = 180;
             
             var newW = (orientation == 6 || orientation == 8) ? h : w;
             var newH = (orientation == 6 || orientation == 8) ? w : h;

             try {
                 // Create RTB
                 var rtb = new RenderTargetBitmap(new Avalonia.PixelSize(newW, newH));
                 using (var ctx = rtb.CreateDrawingContext())
                 {
                      var matrix = Matrix.CreateTranslation(-w/2.0, -h/2.0) * 
                                   Matrix.CreateRotation(Math.PI * angle / 180.0) *
                                   Matrix.CreateTranslation(newW/2.0, newH/2.0);
                                   
                      using (ctx.PushTransform(matrix))
                      {
                          ctx.DrawImage(bitmap, new Rect(0, 0, w, h));
                      }
                 }
                 bitmap.Dispose(); // Dispose original
                 return rtb;
             }
             catch { return bitmap; } // Fallback
        }
        
        return bitmap;
    }
    
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
    
    private void OnZoomScroll(object? sender, PointerWheelEventArgs e)
    {
        var zoomControl = this.FindControl<NumericUpDown>("ZoomControl");
        if (zoomControl == null || !zoomControl.Value.HasValue) return;
        
        // REQUIRE Ctrl Key for Zoom
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }
    }
    
    private void OnImagePointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        var sourceImg = sender as Image;
        if (sourceImg == null) return;

        var img1 = this.FindControl<Image>("ImgOriginal");
        var img2 = this.FindControl<Image>("ImgResult");
        if (img1 == null || img2 == null) return;

        ApplyZoomToImage(img1, e, sourceImg);
        ApplyZoomToImage(img2, e, sourceImg);

        e.Handled = true;
    }

    private void ApplyZoomToImage(Image targetImg, Avalonia.Input.PointerWheelEventArgs e, Image sourceImg)
    {
        if (targetImg.RenderTransform is not Avalonia.Media.TransformGroup tg) return;

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

        var currentPoint = e.GetPosition(sourceImg.Parent as Avalonia.Visual ?? sourceImg);
        var bounds = targetImg.Bounds;
        
        var centerX = bounds.X + bounds.Width / 2 + tt.X;
        var centerY = bounds.Y + bounds.Height / 2 + tt.Y;

        double dx = currentPoint.X - centerX;
        double dy = currentPoint.Y - centerY;

        tt.X -= dx * (zoomFactor - 1);
        tt.Y -= dy * (zoomFactor - 1);

        st.ScaleX = newScaleX;
        st.ScaleY = newScaleX;

        var zoomControl = this.FindControl<NumericUpDown>("ZoomControl");
        if (zoomControl != null) zoomControl.Value = (decimal)newScaleX;
    }
    
    private void OnRotateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Legacy handler, kept for safety but unused
    }

    private void OnRotateOriginalClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RotationOriginal += 90;
        if (RotationOriginal >= 360) RotationOriginal = 0;
    }

    private void OnRotateResultClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RotationResult += 90;
        if (RotationResult >= 360) RotationResult = 0;
    }

    // --- Hand Mode Logic ---
    private bool _isDragging = false;
    private Avalonia.Point _lastPoint;
    private Image? _targetImage;

    private void OnImagePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Image img)
        {
            var point = e.GetCurrentPoint(img);
            if (point.Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _lastPoint = e.GetPosition(this);
                _targetImage = img;
                
                e.Pointer.Capture(img);
                
                Cursor = new Cursor(Avalonia.Input.StandardCursorType.Hand);
                e.Handled = true;
            }
        }
    }

    private void OnImagePointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isDragging || _targetImage == null) return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _lastPoint;
        _lastPoint = currentPoint;

        var img1 = this.FindControl<Image>("ImgOriginal");
        var img2 = this.FindControl<Image>("ImgResult");

        ApplyPanToImage(img1, delta);
        ApplyPanToImage(img2, delta);

        e.Handled = true;
    }

    private void ApplyPanToImage(Image? img, Avalonia.Point delta)
    {
        if (img?.RenderTransform is Avalonia.Media.TransformGroup tg)
        {
            foreach (var t in tg.Children)
            {
                if (t is Avalonia.Media.TranslateTransform transT)
                {
                    transT.X += delta.X;
                    transT.Y += delta.Y;
                }
            }
        }
    }

    private void OnImagePointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isDragging && _targetImage != null)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            _targetImage = null;
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }

    // --- Photoshop Integration ---
    private async void OnPhotoshopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_resultPath) || string.IsNullOrEmpty(_originalPath))
        {
            Console.WriteLine("No images loaded for Photoshop.");
            return;
        }

        if (string.IsNullOrEmpty(_settings.PhotoshopPath) || !File.Exists(_settings.PhotoshopPath))
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Photoshop.exe",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Photoshop Executable") { Patterns = new[] { "Photoshop.exe" } },
                    new FilePickerFileType("Any Executable") { Patterns = new[] { "*.exe" } }
                }
            });

            if (result == null || result.Count == 0)
            {
                Console.WriteLine("Photoshop selection cancelled.");
                return;
            }

            _settings.PhotoshopPath = result[0].Path.LocalPath;
            _settings.Save();
            
            TrySuppressPhotoshopScriptWarning(_settings.PhotoshopPath);
        }

        // Escape backslashes for Javascript string literals
        var escapedResult = _resultPath.Replace("\\", "\\\\").Replace("'", "\\'");
        var escapedOriginal = _originalPath.Replace("\\", "\\\\").Replace("'", "\\'");

        // Generate dynamic JSX script with embedded paths to avoid temp file race conditions
        var dynamicJsx = $@"
#target photoshop
try {{
    var pngFile = new File('{escapedResult}');
    if (pngFile.exists) {{
        var doc = app.open(pngFile);
        if (doc.artLayers.length > 0) {{ doc.artLayers[0].name = 'Hasil (Masker Transparan)'; }}
        var jpgFile = new File('{escapedOriginal}');
        if (jpgFile.exists) {{
            var jpgDoc = app.open(jpgFile);
            jpgDoc.selection.selectAll();
            jpgDoc.activeLayer.copy();
            jpgDoc.close(SaveOptions.DONOTSAVECHANGES);
            app.activeDocument = doc;
            var pastedLayer = doc.paste();
            pastedLayer.name = 'Referensi Asli (Original)';
            pastedLayer.move(doc, ElementPlacement.PLACEATEND);
            doc.activeLayer = doc.artLayers[0];
        }}
    }} else {{
        alert('File hasil tidak ditemukan:\n' + '{escapedResult}');
    }}
}} catch (e) {{
    alert('Error running Photoshop script:\n' + e.message);
}}
";

        var tempJsxPath = Path.Combine(Path.GetTempPath(), $"pixelcut_open_{Guid.NewGuid().ToString("N").Substring(0, 8)}.jsx");

        try
        {
            await File.WriteAllTextAsync(tempJsxPath, dynamicJsx);

            var psi = new ProcessStartInfo
            {
                FileName = _settings.PhotoshopPath,
                Arguments = $"\"{tempJsxPath}\"",
                UseShellExecute = true
            };
            
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error launching Photoshop: {ex.Message}");
            // Reset path on failure so user is prompted to choose a valid executable on next click
            _settings.PhotoshopPath = "";
            _settings.Save();
        }
    }

    private void TrySuppressPhotoshopScriptWarning(string photoshopExePath)
    {
        try
        {
            var psDir = Path.GetDirectoryName(photoshopExePath);
            if (string.IsNullOrEmpty(psDir)) return;

            var psFolderName = Path.GetFileName(psDir);
            
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsFolder = Path.Combine(appDataPath, "Adobe", psFolderName, $"{psFolderName} Settings");

            if (!Directory.Exists(settingsFolder))
            {
                settingsFolder = Path.Combine(appDataPath, "Adobe", psFolderName);
                if (!Directory.Exists(settingsFolder))
                {
                    Console.WriteLine($"Photoshop settings folder not found: {settingsFolder}");
                    return;
                }
            }

            var configPath = Path.Combine(settingsFolder, "PSUserConfig.txt");
            const string suppressLine = "WarnRunningScripts 0";

            if (File.Exists(configPath))
            {
                var content = File.ReadAllText(configPath);
                if (content.Contains("WarnRunningScripts"))
                {
                    Console.WriteLine("PSUserConfig.txt already configured.");
                    return;
                }
                File.AppendAllText(configPath, Environment.NewLine + suppressLine + Environment.NewLine);
            }
            else
            {
                File.WriteAllText(configPath, suppressLine + Environment.NewLine);
            }

            Console.WriteLine($"Auto-configured: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure PSUserConfig: {ex.Message}");
        }
    }

    private void OnPreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            Previous?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            Next?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}

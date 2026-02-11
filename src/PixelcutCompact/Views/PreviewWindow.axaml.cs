using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.IO;

namespace PixelcutCompact.Views;

public partial class PreviewWindow : Window
{
    private PixelcutCompact.Services.PreviewWindowSettings _settings;
    
    // Store paths for Photoshop integration
    private string _originalPath = "";
    private string _resultPath = "";
    
    // Embedded JSX Script
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

    private void OnNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Next?.Invoke(this, EventArgs.Empty);
    private void OnPreviousClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Previous?.Invoke(this, EventArgs.Empty);

    public PreviewWindow()
    {
        InitializeComponent();
        
        // Use Tunneling to catch Wheel events BEFORE ScrollViewer
        // Use Tunneling to catch Wheel events BEFORE ScrollViewer
        // REMOVED: Now handled in XAML
        // AddHandler(PointerWheelChangedEvent, OnZoomScroll, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        RotationOriginal = 0;
        RotationResult = 0;

        _settings = PixelcutCompact.Services.PreviewWindowSettings.Load();
        if (_settings.X != -1 && _settings.Y != -1)
        {
            Position = new PixelPoint((int)_settings.X, (int)_settings.Y);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        
        Width = _settings.Width;
        Height = _settings.Height;
        
        // Find Control and set value
        var zoomControl = this.FindControl<NumericUpDown>("ZoomControl");
        if (zoomControl != null) zoomControl.Value = (decimal)Math.Max(0.2, _settings.Zoom); // Ensure min 0.2

        Closing += (s, e) =>
        {
            _settings.X = Position.X;
            _settings.Y = Position.Y;
            _settings.Width = Width;
            _settings.Height = Height;
            var zc = this.FindControl<NumericUpDown>("ZoomControl");
            if (zc != null && zc.Value.HasValue) _settings.Zoom = (double)zc.Value.Value;
            
            _settings.Save();
        };
        
        // Sync Scrolling
        // Sync Scrolling & Zoom Handlers (Tunnel to prevent default scroll)
        var sv1 = this.FindControl<ScrollViewer>("SvOriginal");
        var sv2 = this.FindControl<ScrollViewer>("SvResult");
        if (sv1 != null && sv2 != null)
        {
            sv1.AddHandler(PointerWheelChangedEvent, OnZoomScroll, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            sv2.AddHandler(PointerWheelChangedEvent, OnZoomScroll, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            sv1.ScrollChanged += (s, e) => { if (s == sv1) sv2.Offset = sv1.Offset; };
            sv2.ScrollChanged += (s, e) => { if (s == sv2) sv1.Offset = sv2.Offset; };
        }
    }

    public void LoadImages(string originalPath, string resultPath, string? title = null)
    {
        // Store paths for Photoshop
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

            if (imgOriginal != null && File.Exists(originalPath)) 
                imgOriginal.Source = LoadBitmapWithOrientation(originalPath);
            
            if (imgResult != null && File.Exists(resultPath)) 
                imgResult.Source = LoadBitmapWithOrientation(resultPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading preview: {ex.Message}");
        }
    }

    private Bitmap LoadBitmapWithOrientation(string path)
    {
        // 1. Check orientation
        int orientation = 1;
        try { orientation = PixelcutCompact.Helpers.ExifHelper.GetOrientation(path); } catch { }

        // 2. Load full bitmap
        var bitmap = new Bitmap(path);

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
        
        // --- CURSOR FOLLOWING ZOOM LOGIC ---
        // REQUIRE Ctrl Key for Zoom
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Allow default scrolling behavior if Ctrl is not pressed
            return;
        }

        // 1. Get the control being scrolled (ScrollViewer)
        var sv = sender as ScrollViewer;
        if (sv == null) return;
        
        // 2. Get mouse position relative to the content (Image inside LayoutTransformControl)
        var mousePos = e.GetPosition(sv);
        
        decimal oldZoom = zoomControl.Value.Value;
        
        // Smooth Zoom: Use multiplication instead of addition
        decimal factor = e.Delta.Y > 0 ? 1.1m : 0.9m;
        decimal newZoom = oldZoom * factor;

        // Clamp
        newZoom = Math.Clamp(newZoom, zoomControl.Minimum, zoomControl.Maximum);

        if (newZoom == oldZoom) return; // Limit reached
        
        // 3. Calculate new Offset to keep mousePos stable
        // Formula: NewOffset = (OldOffset + MousePos) * (NewZoom / OldZoom) - MousePos
        
        double scaleFactor = (double)(newZoom / oldZoom);
        
        double newOffsetX = (sv.Offset.X + mousePos.X) * scaleFactor - mousePos.X;
        double newOffsetY = (sv.Offset.Y + mousePos.Y) * scaleFactor - mousePos.Y;
        
        // Apply Zoom
        zoomControl.Value = newZoom;
        
        // Apply Offset (must wait for layout update or force it? LayoutTransform happens immediately on property change)
        // However, ScrollViewer extent size changes after layout.
        // We can try setting offset immediately in a dispatcher to happen after measure.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
             sv.Offset = new Vector(newOffsetX, newOffsetY);
             
             // Sync will happen via ScrollChanged, but we should sync manually here to be snappy
             var otherSv = sv.Name == "SvOriginal" 
                ? this.FindControl<ScrollViewer>("SvResult") 
                : this.FindControl<ScrollViewer>("SvOriginal");
             
             if (otherSv != null)
             {
                 otherSv.Offset = sv.Offset;
             }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        
        e.Handled = true;
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
    private Point _lastPoint;
    private ScrollViewer? _targetScroller;

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ScrollViewer sv && e.GetCurrentPoint(sv).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPoint = e.GetPosition(sv);
            _targetScroller = sv;
            
            // Capture pointer to track outside bounds
            e.Pointer.Capture(sv);
            
            Cursor = new Cursor(StandardCursorType.Hand);
        }
    }

    private void OnImagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _targetScroller == null) return;

        var currentPoint = e.GetPosition(_targetScroller);
        var delta = _lastPoint - currentPoint; // Movement delta

        // Update Offset
        _targetScroller.Offset = new Vector(
            _targetScroller.Offset.X + delta.X,
            _targetScroller.Offset.Y + delta.Y
        );

        // SYNC Logic: Update the OTHER scrollviewer to match
        var otherScroller = _targetScroller.Name == "SvOriginal" 
            ? this.FindControl<ScrollViewer>("SvResult") 
            : this.FindControl<ScrollViewer>("SvOriginal");
            
        if (otherScroller != null)
        {
            otherScroller.Offset = _targetScroller.Offset;
        }

        _lastPoint = e.GetPosition(_targetScroller); // Re-read position relative to control (which hasn't moved, content moved)
        // Wait, if content moves underneath, GetPosition might change relative to content?
        // No, GetPosition(sv) is relative to ScrollViewer viewport top-left usually. 
        // If I scroll, the content moves, but capture point relative to viewport stays same if mouse doesn't move. 
        // But mouse MOVED.
        // Actually, for Panning: NewPosition - OldPosition = Distance Moved.
        // We want to Scroll by that Distance.
        // When we change Offset, the visual changes. 
        // Logic: 
        // 1. Mouse at 100,100.
        // 2. Mouse moves to 90, 100 (Left drag 10px).
        // 3. Delta = 100 - 90 = +10.
        // 4. We want to View MORE Right content, so we ADD 10 to Offset.
        // Correct.
        
        // Note: For smooth panning we update _lastPoint to currentPoint.
        _lastPoint = e.GetPosition(_targetScroller); // Must update to current so next delta is correct.
    }

    private void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _targetScroller != null)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            _targetScroller = null;
            Cursor = Cursor.Default;
        }
    }

    // --- Photoshop Integration ---
    private async void OnPhotoshopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Check if we have paths
        if (string.IsNullOrEmpty(_resultPath) || string.IsNullOrEmpty(_originalPath))
        {
            Console.WriteLine("No images loaded for Photoshop.");
            return;
        }

        // Check if Photoshop path is set
        if (string.IsNullOrEmpty(_settings.PhotoshopPath) || !File.Exists(_settings.PhotoshopPath))
        {
            // Ask user to select Photoshop.exe
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
            
            // Auto-suppress script warning
            TrySuppressPhotoshopScriptWarning(_settings.PhotoshopPath);
        }

        // Write temp file with paths for JSX script (Arg passing)
        var tempArgsPath = Path.Combine(Path.GetTempPath(), "pixelcut_edit_args.txt");
        await File.WriteAllTextAsync(tempArgsPath, $"{_resultPath}\n{_originalPath}");

        // Write JSX script to temp file
        var tempJsxPath = Path.Combine(Path.GetTempPath(), "open_for_edit.jsx");
        await File.WriteAllTextAsync(tempJsxPath, JsxScript);

        try
        {
            // Launch Photoshop with the JSX script
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
        }
    }

    /// <summary>
    /// Automatically creates/modifies PSUserConfig.txt to suppress JSX script warnings.
    /// </summary>
    private void TrySuppressPhotoshopScriptWarning(string photoshopExePath)
    {
        try
        {
            // Extract Photoshop folder name from path (e.g., "Adobe Photoshop 2024")
            var psDir = Path.GetDirectoryName(photoshopExePath);
            if (string.IsNullOrEmpty(psDir)) return;

            var psFolderName = Path.GetFileName(psDir); // e.g., "Adobe Photoshop 2024"
            
            // Build settings folder path
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsFolder = Path.Combine(appDataPath, "Adobe", psFolderName, $"{psFolderName} Settings");

            if (!Directory.Exists(settingsFolder))
            {
                // Try without " Settings" suffix (some versions)
                settingsFolder = Path.Combine(appDataPath, "Adobe", psFolderName);
                if (!Directory.Exists(settingsFolder))
                {
                    Console.WriteLine($"Photoshop settings folder not found: {settingsFolder}");
                    return;
                }
            }

            var configPath = Path.Combine(settingsFolder, "PSUserConfig.txt");
            const string suppressLine = "WarnRunningScripts 0";

            // Check if already configured
            if (File.Exists(configPath))
            {
                var content = File.ReadAllText(configPath);
                if (content.Contains("WarnRunningScripts"))
                {
                    Console.WriteLine("PSUserConfig.txt already configured.");
                    return;
                }
                // Append to existing file
                File.AppendAllText(configPath, Environment.NewLine + suppressLine + Environment.NewLine);
            }
            else
            {
                // Create new file
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

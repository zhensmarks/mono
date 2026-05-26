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
    
    // Photopea state
    private PhotopeaLocalServer? _photopeaServer;
    private NativeWebView? _webView;
    private bool _isPhotopeaMode = false;
    private string _photopeaSaveFormat = "png";
    private bool _autoAdvanceOnSave = false;
    private System.Timers.Timer? _toastTimer;
    private bool _hideAds = false;
    
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
    
    /// <summary>Dipanggil saat user klik "Next" di Photopea mode — agar ViewModel bisa memberikan file berikutnya.</summary>
    public event EventHandler? PhotopeaNextRequested;
    /// <summary>Dipanggil saat user klik "Previous" di Photopea mode.</summary>
    public event EventHandler? PhotopeaPreviousRequested;

    /// <summary>Dipanggil saat file berhasil disimpan oleh Photopea.</summary>
    public event Action<string>? FileSaved;

    private void OnNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isPhotopeaMode)
        {
            PhotopeaNextRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Next?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviousClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isPhotopeaMode)
        {
            PhotopeaPreviousRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Previous?.Invoke(this, EventArgs.Empty);
        }
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
        
        // Restore Photopea settings
        _photopeaSaveFormat = _settings.PhotopeaSaveFormat ?? "png";
        _autoAdvanceOnSave = _settings.AutoAdvanceOnSave;
        
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
            _settings.PhotopeaSaveFormat = _photopeaSaveFormat;
            _settings.AutoAdvanceOnSave = _autoAdvanceOnSave;
            
            _settings.Save();
            
            // Cleanup Photopea resources
            CleanupPhotopea();
        };
        
        // Restore UI controls after template is applied
        Opened += (s, e) =>
        {
            var cmbFormat = this.FindControl<ComboBox>("CmbSaveFormat");
            if (cmbFormat != null)
            {
                cmbFormat.SelectedIndex = _photopeaSaveFormat == "psd" ? 1 : 0;
            }
            
            var chkAuto = this.FindControl<CheckBox>("ChkAutoAdvance");
            if (chkAuto != null)
            {
                chkAuto.IsChecked = _autoAdvanceOnSave;
            }
        };
    }

    public void LoadImages(string originalPath, string resultPath, string? title = null)
    {
        // Store paths for Photoshop/Photopea
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

    /// <summary>
    /// Memuat gambar baru ke Photopea saat dalam mode edit (untuk navigasi Next/Previous).
    /// </summary>
    public void LoadImagesInPhotopeaMode(string originalPath, string resultPath, string? title = null)
    {
        _originalPath = originalPath;
        _resultPath = resultPath;
        
        if (!string.IsNullOrEmpty(title))
        {
            var label = this.FindControl<TextBlock>("TxtTitle");
            if (label != null) label.Text = title;
            Title = title;
        }
        
        if (_isPhotopeaMode && _photopeaServer != null && _webView != null)
        {
            // Update server files
            _photopeaServer.UpdateFiles(originalPath, resultPath);
            
            // Reload Photopea with new files
            var photopeaUrl = BuildPhotopeaUrl();
            _webView.Source = new Uri(photopeaUrl);
            
            // Show loading overlay briefly
            var loadingOverlay = this.FindControl<Border>("PhotopeaLoading");
            if (loadingOverlay != null) loadingOverlay.IsVisible = true;
        }
    }

    // ========================
    // PHOTOPEA INTEGRATION
    // ========================

    private void OnPhotopeaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_resultPath) || string.IsNullOrEmpty(_originalPath))
        {
            Console.WriteLine("No images loaded for Photopea.");
            return;
        }

        if (!File.Exists(_resultPath))
        {
            Console.WriteLine($"Result file not found: {_resultPath}");
            return;
        }

        EnterPhotopeaMode();
    }

    private async System.Threading.Tasks.Task<bool> CheckIfAdsBlockedAsync()
    {
        try
        {
            using (var cts = new System.Threading.CancellationTokenSource(1000))
            {
                var hostEntry = await System.Net.Dns.GetHostEntryAsync("pagead2.googlesyndication.com", cts.Token);
                foreach (var ip in hostEntry.AddressList)
                {
                    if (System.Net.IPAddress.IsLoopback(ip) || ip.ToString() == "0.0.0.0")
                    {
                        return true; // Sinkholed (AdBlocker active)
                    }
                }
                return false;
            }
        }
        catch
        {
            return true; // DNS failed, likely offline or blocked
        }
    }

    private async void EnterPhotopeaMode()
    {
        try
        {
            // 1. Start local server
            _photopeaServer = new PhotopeaLocalServer();
            _photopeaServer.Start(_originalPath, _resultPath, _photopeaSaveFormat);
            _photopeaServer.FileSaved += OnPhotopeaFileSaved;
            _photopeaServer.SaveStarted += OnPhotopeaSaveStarted;

            // 2. Build Photopea URL
            var photopeaUrl = BuildPhotopeaUrl();

            // 3. Detect if ads are blocked to prevent cutting off the layers panel
            bool adsBlocked = await CheckIfAdsBlockedAsync();
            _hideAds = !adsBlocked;
            Thickness webViewMargin = _hideAds ? new Thickness(0, 0, -300, 0) : new Thickness(0, 0, 0, 0);
            
            Console.WriteLine($"[Photopea] Ad check: adsBlocked={adsBlocked}. HideAds={_hideAds}. Setting margin to {webViewMargin}");

            // 4. Create and add WebView
            // Negative right margin extends WebView 300px beyond container.
            // Since container has ClipToBounds="True", the right-side ad panel is clipped.
            // If ads are blocked (e.g. offline or DNS sinkhole), margin is set to 0 to prevent cutting off the layers panel.
            var container = this.FindControl<Border>("WebViewContainer");
            if (container != null)
            {
                _webView = new NativeWebView();
                _webView.NavigationCompleted += OnWebViewNavigationCompleted;
                _webView.Margin = webViewMargin;
                _webView.Source = new Uri(photopeaUrl);
                container.Child = _webView;
            }

            // 5. Toggle UI states
            _isPhotopeaMode = true;
            
            var normalPanel = this.FindControl<Grid>("PanelNormalPreview");
            var editorPanel = this.FindControl<Grid>("PanelPhotopeaEditor");
            var controlsPanel = this.FindControl<StackPanel>("PanelPhotopeaControls");
            var loadingOverlay = this.FindControl<Border>("PhotopeaLoading");
            var btnPhotopea = this.FindControl<Button>("BtnPhotopea");
            
            var chkHideAds = this.FindControl<CheckBox>("ChkHideAds");
            if (chkHideAds != null)
            {
                chkHideAds.IsChecked = _hideAds;
            }
            
            if (normalPanel != null) normalPanel.IsVisible = false;
            if (editorPanel != null) editorPanel.IsVisible = true;
            if (controlsPanel != null) controlsPanel.IsVisible = true;
            if (loadingOverlay != null) loadingOverlay.IsVisible = true;
            if (btnPhotopea != null) btnPhotopea.IsEnabled = false;
            
            Console.WriteLine($"[Photopea] Entered edit mode. Server: http://localhost:{_photopeaServer.Port}/");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Photopea] Error entering edit mode: {ex.Message}");
            ExitPhotopeaMode();
        }
    }

    private void ExitPhotopeaMode()
    {
        _isPhotopeaMode = false;
        
        // Toggle UI states back
        var normalPanel = this.FindControl<Grid>("PanelNormalPreview");
        var editorPanel = this.FindControl<Grid>("PanelPhotopeaEditor");
        var controlsPanel = this.FindControl<StackPanel>("PanelPhotopeaControls");
        var btnPhotopea = this.FindControl<Button>("BtnPhotopea");
        
        if (normalPanel != null) normalPanel.IsVisible = true;
        if (editorPanel != null) editorPanel.IsVisible = false;
        if (controlsPanel != null) controlsPanel.IsVisible = false;
        if (btnPhotopea != null) btnPhotopea.IsEnabled = true;
        
        // Cleanup WebView
        CleanupPhotopea();
        
        // Refresh preview images (they might have been edited)
        RefreshPreviewImages();
    }

    private void CleanupPhotopea()
    {
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
            var container = this.FindControl<Border>("WebViewContainer");
            if (container != null) container.Child = null;
            _webView = null;
        }
        
        if (_photopeaServer != null)
        {
            _photopeaServer.FileSaved -= OnPhotopeaFileSaved;
            _photopeaServer.SaveStarted -= OnPhotopeaSaveStarted;
            _photopeaServer.Dispose();
            _photopeaServer = null;
        }

        // Restore header icons
        var imgHeader = this.FindControl<PathIcon>("ImgHeaderIcon");
        var imgLoading = this.FindControl<Control>("ImgLoadingIcon");
        if (imgHeader != null) imgHeader.IsVisible = true;
        if (imgLoading != null) imgLoading.IsVisible = false;
    }

    private void RefreshPreviewImages()
    {
        try
        {
            var imgOriginal = this.FindControl<Image>("ImgOriginal");
            var imgResult = this.FindControl<Image>("ImgResult");

            if (imgOriginal != null && File.Exists(_originalPath))
                imgOriginal.Source = LoadBitmapWithOrientation(_originalPath);
            
            if (imgResult != null && File.Exists(_resultPath))
                imgResult.Source = LoadBitmapWithOrientation(_resultPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Photopea] Error refreshing preview: {ex.Message}");
        }
    }

    private string BuildPhotopeaUrl()
    {
        var port = _photopeaServer?.Port ?? 49152;
        
        // Generate the intermediary HTML page and set it on the server
        var editorHtml = BuildEditorHtml(port);
        _photopeaServer?.SetEditorHtml(editorHtml);
        
        // WebView navigates to our local server which serves the HTML wrapper with a cache-buster query parameter
        return $"http://localhost:{port}/editor.html?t={DateTime.Now.Ticks}";
    }

    private string BuildEditorHtml(int port)
    {
        // Determine theme
        int theme = 1; // default dark
        if (Avalonia.Application.Current != null)
        {
            var themeVariant = Avalonia.Application.Current.RequestedThemeVariant;
            theme = (themeVariant == Avalonia.Styling.ThemeVariant.Light) ? 0 : 1;
        }

        var format = _photopeaSaveFormat == "psd" ? "psd" : "png";

        // Photopea config JSON
        // PENTING: Urutan file dibalik!
        //   - original.jpg dimuat PERTAMA → documents[0]
        //   - result.png dimuat KEDUA → documents[1]
        // Setelah duplicate dari result.png ke documents[0]:
        //   - artLayers[0] = result PNG (ATAS) — baru diduplikasi, otomatis di atas
        //   - artLayers[1] = original JPG (BAWAH) — sudah ada sejak awal
        // Tidak perlu move() sama sekali!
        var photopeaConfig = new
        {
            files = new[]
            {
                $"http://localhost:{port}/original.jpg?t={DateTime.Now.Ticks}",
                $"http://localhost:{port}/result.png?t={DateTime.Now.Ticks}"
            },
            environment = new
            {
                theme = theme,
                lang = "id",
                customIO = new { save = "app.echoToOE('save-requested');" }
            },
            script = BuildPhotopeaScript()
        };

        var configJson = JsonSerializer.Serialize(photopeaConfig);
        var encodedConfig = Uri.EscapeDataString(configJson);
        var photopeaUrl = $"https://www.photopea.com/#{encodedConfig}";

        // HTML page: iframe loads Photopea, parent page catches postMessage ArrayBuffer
        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<title>Photopea Editor</title>
<style>
  * {{ margin: 0; padding: 0; box-sizing: border-box; }}
  body {{ background: #1e1e1e; overflow: hidden; position: relative; }}
  iframe {{ 
    width: 100vw; 
    height: 100vh; 
    border: none; 
    display: block;
  }}
  #toast {{
    position: absolute;
    top: -100px;
    left: 50%;
    transform: translateX(-50%);
    background: rgba(24, 160, 90, 0.95);
    color: white;
    padding: 12px 24px;
    border-radius: 8px;
    font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
    font-size: 14px;
    font-weight: 600;
    box-shadow: 0 10px 25px rgba(0,0,0,0.35);
    display: flex;
    align-items: center;
    gap: 8px;
    z-index: 99999;
    transition: all 0.4s cubic-bezier(0.175, 0.885, 0.32, 1.275);
    backdrop-filter: blur(8px);
    border: 1px solid rgba(255,255,255,0.1);
  }}
</style>
</head>
<body>
<div id=""toast"">
  <span style=""font-size: 16px;"">✓</span>
  <span>Perubahan berhasil disimpan</span>
</div>
<iframe id=""photopea"" src=""{photopeaUrl}"" allow=""cross-origin-isolated""></iframe>
<script>
function showToast() {{
    var toast = document.getElementById('toast');
    toast.style.top = '20px';
    setTimeout(function() {{
        toast.style.top = '-100px';
    }}, 2500);
}}

var pendingPngSave = false;

// Listen for postMessage from Photopea iframe.
// When user presses Ctrl+S, Photopea runs our customIO.save script: app.echoToOE('save-requested')
// We receive 'save-requested' and fetch the current format dynamically from the local server.
window.addEventListener('message', function(e) {{
    if (e.data === 'save-requested') {{
        fetch('http://localhost:{port}/format')
            .then(function(r) {{ return r.json(); }})
            .then(function(j) {{
                var currentFormat = (j && j.format) ? j.format : 'png';
                var iframe = document.getElementById('photopea');
                if (iframe && iframe.contentWindow) {{
                    if (currentFormat === 'psd') {{
                        pendingPngSave = true;
                        iframe.contentWindow.postMessage(""app.activeDocument.saveToOE('psd');"", ""*"");
                    }} else {{
                        pendingPngSave = false;
                        iframe.contentWindow.postMessage(""app.activeDocument.saveToOE('png');"", ""*"");
                    }}
                }}
            }})
            .catch(function(err) {{
                pendingPngSave = false;
                var iframe = document.getElementById('photopea');
                if (iframe && iframe.contentWindow) {{
                    iframe.contentWindow.postMessage(""app.activeDocument.saveToOE('png');"", ""*"");
                }}
            }});
    }} else if (e.data instanceof ArrayBuffer) {{
        console.log('Received file from Photopea: ' + e.data.byteLength + ' bytes');
        
        fetch('http://localhost:{port}/save', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/octet-stream' }},
            body: new Uint8Array(e.data)
        }}).then(function(r) {{
            return r.json();
        }}).then(function(j) {{
            if (j && j.saved) {{
                console.log('File saved successfully!');
                if (pendingPngSave) {{
                    pendingPngSave = false;
                    var iframe = document.getElementById('photopea');
                    if (iframe && iframe.contentWindow) {{
                        iframe.contentWindow.postMessage(""app.activeDocument.saveToOE('png');"", ""*"");
                    }}
                }} else {{
                    showToast();
                }}
            }} else {{
                console.error('Save response unexpected:', j);
            }}
        }}).catch(function(err) {{
            console.error('Save error:', err);
        }});
    }}
}});
</script>
</body>
</html>";
    }

    private string BuildPhotopeaScript()
    {
        // Script yang berjalan di dalam Photopea setelah semua file dimuat.
        //
        // Urutan file di config: [original.jpg, result.png]
        // Sehingga: documents[0] = original, documents[1] = result
        //
        // Setelah duplicate result layer ke documents[0]:
        //   artLayers[0] = result PNG (ATAS) ← baru diduplikasi, otomatis di paling atas
        //   artLayers[1] = original JPG (BAWAH) ← sudah ada sejak awal
        // TIDAK perlu move()! Urutan sudah benar secara alami.
        return @"
if (app.documents.length >= 2) {
    var originalDoc = app.documents[0];
    var resultDoc = app.documents[1];
    
    // Duplicate result layer into original document (result goes on top automatically)
    app.activeDocument = resultDoc;
    resultDoc.artLayers[0].duplicate(originalDoc);
    
    // Close result tab (no longer needed)
    resultDoc.close(SaveOptions.DONOTSAVECHANGES);
    
    // Now originalDoc has 2 layers:
    //   artLayers[0] = result PNG (TOP) — just duplicated, automatically placed on top
    //   artLayers[1] = original JPG (BOTTOM) — was already there
    app.activeDocument = originalDoc;
    
    // Rename layers
    originalDoc.artLayers[0].name = 'Hasil (Masker Transparan)';
    originalDoc.artLayers[1].name = 'Referensi Asli (Original)';
    
    // Both at 100% opacity, both unlocked
    originalDoc.artLayers[0].opacity = 100;
    originalDoc.artLayers[1].opacity = 100;
    
    // Select result layer (top) as active for editing
    originalDoc.activeLayer = originalDoc.artLayers[0];
} else if (app.documents.length == 1) {
    var doc = app.activeDocument;
    if (doc.artLayers.length >= 1) {
        doc.artLayers[0].name = 'Hasil (Masker Transparan)';
    }
    doc.activeLayer = doc.artLayers[0];
}
";
    }

    private void OnWebViewNavigationCompleted(object? sender, Avalonia.Controls.WebViewNavigationCompletedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var loadingOverlay = this.FindControl<Border>("PhotopeaLoading");
            if (loadingOverlay != null) loadingOverlay.IsVisible = false;
        });
    }

    private void OnPhotopeaSaveStarted()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var imgHeader = this.FindControl<PathIcon>("ImgHeaderIcon");
            var imgLoading = this.FindControl<Control>("ImgLoadingIcon");
            if (imgHeader != null) imgHeader.IsVisible = false;
            if (imgLoading != null) imgLoading.IsVisible = true;
        });
    }

    private void OnPhotopeaFileSaved(string savedPath)
    {
        Console.WriteLine($"[Photopea] File saved callback: {savedPath}");
        
        Dispatcher.UIThread.Post(() =>
        {
            // Restore header icon
            var imgHeader = this.FindControl<PathIcon>("ImgHeaderIcon");
            var imgLoading = this.FindControl<Control>("ImgLoadingIcon");
            if (imgHeader != null) imgHeader.IsVisible = true;
            if (imgLoading != null) imgLoading.IsVisible = false;

            // Refresh preview images
            RefreshPreviewImages();
            
            // Show premium save toast notification
            ShowSaveToast();
            
            // Invoke the event for MainWindowViewModel or other subscribers
            FileSaved?.Invoke(savedPath);
            
            // Auto-advance to next image if enabled
            if (_autoAdvanceOnSave)
            {
                // Trigger next navigation
                if (_isPhotopeaMode)
                {
                    PhotopeaNextRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        });
    }

    private void ShowSaveToast()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var nav = this.FindControl<StackPanel>("PanelNavigation");
            var toast = this.FindControl<Border>("ToastNotification");
            
            if (nav != null) nav.IsVisible = false;
            if (toast != null) toast.IsVisible = true;
            
            _toastTimer?.Stop();
            _toastTimer?.Dispose();
            
            _toastTimer = new System.Timers.Timer(2500);
            _toastTimer.AutoReset = false;
            _toastTimer.Elapsed += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (nav != null) nav.IsVisible = true;
                    if (toast != null) toast.IsVisible = false;
                });
            };
            _toastTimer.Start();
        });
    }

    private void OnExitPhotopeaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ExitPhotopeaMode();
    }

    private void OnSaveFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cmb = sender as ComboBox;
        if (cmb?.SelectedItem is ComboBoxItem item)
        {
            var format = item.Tag?.ToString()?.ToLowerInvariant() ?? "png";
            _photopeaSaveFormat = format;
            if (_photopeaServer != null)
            {
                _photopeaServer.SaveFormat = format;
            }
        }
    }

    private void OnAutoAdvanceChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var chk = sender as CheckBox;
        _autoAdvanceOnSave = chk?.IsChecked ?? false;
    }

    private void OnHideAdsChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var chk = sender as CheckBox;
        _hideAds = chk?.IsChecked ?? false;
        
        if (_webView != null)
        {
            _webView.Margin = _hideAds ? new Thickness(0, 0, -300, 0) : new Thickness(0, 0, 0, 0);
            Console.WriteLine($"[Photopea] Margin toggled by user. HideAds={_hideAds}, Margin={_webView.Margin}");
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
            if (_isPhotopeaMode)
                PhotopeaPreviousRequested?.Invoke(this, EventArgs.Empty);
            else
                Previous?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (_isPhotopeaMode)
                PhotopeaNextRequested?.Invoke(this, EventArgs.Empty);
            else
                Next?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isPhotopeaMode)
        {
            ExitPhotopeaMode();
            e.Handled = true;
        }
    }
}

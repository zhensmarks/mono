using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PixelcutCompact.Services;

/// <summary>
/// Server HTTP lokal ringan berbasis HttpListener.
/// Menyajikan file gambar (original + result) untuk Photopea,
/// dan menerima hasil edit via POST /save saat user Ctrl+S.
/// </summary>
public class PhotopeaLocalServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    private string _originalPath = "";
    private string _resultPath = "";
    private string _saveFormat = "png"; // default: png | psd
    private string _editorHtml = ""; // HTML halaman perantara untuk iframe Photopea

    public string SaveFormat
    {
        get => _saveFormat;
        set
        {
            _saveFormat = value?.ToLowerInvariant() ?? "png";
            Console.WriteLine($"[PhotopeaLocalServer] Save format updated to: {_saveFormat}");
        }
    }

    public int Port { get; private set; }
    public bool IsRunning => _listener?.IsListening ?? false;

    /// <summary>
    /// Dipanggil saat Photopea mengirim file hasil edit (Ctrl+S).
    /// Parameter: path file yang berhasil disimpan.
    /// </summary>
    public event Action<string>? FileSaved;

    /// <summary>
    /// Dipanggil saat proses penyimpanan dimulai (Ctrl+S ditekan).
    /// </summary>
    public event Action? SaveStarted;

    /// <summary>
    /// Memulai server pada port yang tersedia.
    /// </summary>
    public void Start(string originalPath, string resultPath, string saveFormat = "png")
    {
        _originalPath = originalPath;
        _resultPath = resultPath;
        _saveFormat = saveFormat.ToLowerInvariant();

        // Cari port yang tersedia antara 49152-65535 (dynamic/private range)
        var rng = new Random();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int port = rng.Next(49152, 65000);
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                Port = port;
                break;
            }
            catch
            {
                _listener?.Close();
                _listener = null;
            }
        }

        if (_listener == null || !_listener.IsListening)
            throw new Exception("Tidak dapat menemukan port yang tersedia untuk server Photopea lokal.");

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ListenLoop(_cts.Token));

        Console.WriteLine($"[PhotopeaLocalServer] Berjalan pada http://localhost:{Port}/");
    }

    /// <summary>
    /// Memperbarui file yang disajikan (untuk navigasi Next/Previous tanpa restart server).
    /// </summary>
    public void UpdateFiles(string originalPath, string resultPath)
    {
        _originalPath = originalPath;
        _resultPath = resultPath;
    }

    /// <summary>
    /// Set HTML halaman perantara yang berisi iframe Photopea.
    /// </summary>
    public void SetEditorHtml(string html)
    {
        _editorHtml = html;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        Console.WriteLine("[PhotopeaLocalServer] Dihentikan.");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhotopeaLocalServer] Error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers — Photopea membutuhkan ini untuk mengakses file dari localhost
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            // Handle CORS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";

            switch (path)
            {
                case "/original":
                case "/original.jpg":
                case "/original.jpeg":
                    ServeFile(response, _originalPath);
                    break;

                case "/result":
                case "/result.png":
                    ServeFile(response, _resultPath);
                    break;

                case "/save":
                    if (request.HttpMethod == "POST")
                    {
                        HandleSave(request, response);
                    }
                    else
                    {
                        response.StatusCode = 405;
                        response.Close();
                    }
                    break;

                case "/editor":
                case "/editor.html":
                    ServeEditorHtml(response);
                    break;

                case "/status":
                    // Health check endpoint
                    var statusBytes = System.Text.Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    response.ContentType = "application/json";
                    response.ContentLength64 = statusBytes.Length;
                    response.OutputStream.Write(statusBytes, 0, statusBytes.Length);
                    response.Close();
                    break;

                case "/format":
                    // Dynamic format query endpoint for the photopea wrapper HTML
                    var formatJson = $"{{\"format\":\"{_saveFormat}\"}}";
                    var formatBytes = System.Text.Encoding.UTF8.GetBytes(formatJson);
                    response.ContentType = "application/json";
                    response.ContentLength64 = formatBytes.Length;
                    response.OutputStream.Write(formatBytes, 0, formatBytes.Length);
                    response.Close();

                    // Trigger save started notification
                    SaveStarted?.Invoke();
                    break;

                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PhotopeaLocalServer] HandleRequest error: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    private void ServeFile(HttpListenerResponse response, string filePath)
    {
        if (!File.Exists(filePath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        response.ContentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".psd" => "application/octet-stream",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        var fileBytes = File.ReadAllBytes(filePath);
        response.ContentLength64 = fileBytes.Length;
        response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
        response.Close();
    }

    private void ServeEditorHtml(HttpListenerResponse response)
    {
        if (string.IsNullOrEmpty(_editorHtml))
        {
            response.StatusCode = 500;
            response.Close();
            return;
        }

        var htmlBytes = System.Text.Encoding.UTF8.GetBytes(_editorHtml);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = htmlBytes.Length;
        response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
        response.Close();
    }

    private void HandleSave(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            Console.WriteLine($"[PhotopeaLocalServer] Save request: ContentType={request.ContentType}, ContentLength={request.ContentLength64}, Method={request.HttpMethod}");

            // Read the full request body using buffered loop (more robust than CopyTo)
            using var ms = new MemoryStream();
            byte[] buffer = new byte[65536]; // 64KB buffer
            int bytesRead;
            while ((bytesRead = request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
            }
            var data = ms.ToArray();

            Console.WriteLine($"[PhotopeaLocalServer] Received {data.Length} bytes");

            if (data.Length == 0)
            {
                Console.WriteLine("[PhotopeaLocalServer] WARNING: Empty data received!");
                response.StatusCode = 400;
                response.Close();
                return;
            }

            // Log first bytes for debugging (PNG magic: 137 80 78 71 = 0x89 0x50 0x4E 0x47)
            if (data.Length >= 8)
            {
                Console.WriteLine($"[PhotopeaLocalServer] First 8 bytes: {BitConverter.ToString(data, 0, 8)}");
                
                // Validate PNG header
                bool isPng = data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
                // Validate PSD header ("8BPS")
                bool isPsd = data[0] == 0x38 && data[1] == 0x42 && data[2] == 0x50 && data[3] == 0x53;
                
                Console.WriteLine($"[PhotopeaLocalServer] File type detected: isPng={isPng}, isPsd={isPsd}");
            }

            // Tentukan path output berdasarkan signature file data
            string savePath;
            bool hasPsdSignature = data.Length >= 4 && data[0] == 0x38 && data[1] == 0x42 && data[2] == 0x50 && data[3] == 0x53;

            if (hasPsdSignature)
            {
                var dir = Path.GetDirectoryName(_resultPath) ?? "";
                var name = Path.GetFileNameWithoutExtension(_resultPath);
                savePath = Path.Combine(dir, $"{name}.psd");
                Console.WriteLine($"[PhotopeaLocalServer] Menyimpan data PSD ke: {savePath}");
            }
            else
            {
                // PNG atau fallback: timpa file result yang ada
                savePath = _resultPath;
                Console.WriteLine($"[PhotopeaLocalServer] Menyimpan data PNG ke: {savePath}");
            }

            // Write file atomically: write to temp first, then move
            var tempPath = savePath + ".tmp";
            File.WriteAllBytes(tempPath, data);
            
            // Verify temp file was written correctly
            var verifyInfo = new FileInfo(tempPath);
            if (verifyInfo.Length != data.Length)
            {
                Console.WriteLine($"[PhotopeaLocalServer] ERROR: Written file size mismatch! Expected {data.Length}, got {verifyInfo.Length}");
                File.Delete(tempPath);
                response.StatusCode = 500;
                response.Close();
                return;
            }
            
            // Replace original with temp
            if (File.Exists(savePath))
                File.Delete(savePath);
            File.Move(tempPath, savePath);

            Console.WriteLine($"[PhotopeaLocalServer] File disimpan: {savePath} ({data.Length} bytes)");

            // Kirim respons sukses
            var okBytes = System.Text.Encoding.UTF8.GetBytes("{\"saved\":true}");
            response.ContentType = "application/json";
            response.ContentLength64 = okBytes.Length;
            response.OutputStream.Write(okBytes, 0, okBytes.Length);
            response.Close();

            // Notifikasi ke UI
            FileSaved?.Invoke(savePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PhotopeaLocalServer] Save error: {ex.Message}\n{ex.StackTrace}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }
}

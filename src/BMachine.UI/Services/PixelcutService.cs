using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

public class PixelcutService
{
    // Configurable from UI
    public string? ApiKey { get; set; }

    public PixelcutService()
    {
    }

    public async Task InitializeAsync()
    {
        // No async init needed anymore
        await Task.CompletedTask;
    }

    public async Task ProcessImageAsync(PixelcutFileItem item, string jobType)
    {
        string endpoint = jobType == "upscale" 
            ? "https://api.developer.pixelcut.ai/v1/upscale" 
            : "https://api.developer.pixelcut.ai/v1/remove-background";

        // Direct request with single attempt
        await ExecuteRequestAsync(item, endpoint, jobType, CancellationToken.None);
    }

    public async Task ProcessImageAsync(PixelcutFileItem item, string jobType, CancellationToken ct)
    {
        string endpoint = jobType == "upscale" 
            ? "https://api.developer.pixelcut.ai/v1/upscale" 
            : "https://api.developer.pixelcut.ai/v1/remove-background";

        // Direct request with single attempt
        await ExecuteRequestAsync(item, endpoint, jobType, ct);
    }

    private async Task ExecuteRequestAsync(PixelcutFileItem item, string url, string job, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10); // Increased from 2 to 10 mins for slow connections
        
        // Headers
        AddHeaders(client, job);

        using var content = new MultipartFormDataContent();
        
        // Read File
        byte[] fileBytes = await File.ReadAllBytesAsync(item.FilePath, ct);
        using var fileContent = new ByteArrayContent(fileBytes);
        
        string ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
        string mimeType = (ext == ".png") ? "image/png" : "image/jpeg";
        string fileName = (ext == ".png") ? "image.png" : "image.jpg";

        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        content.Add(fileContent, "image", fileName);

        // Parameters
        if (job == "upscale")
        {
            content.Add(new StringContent("2"), "scale");
            
            // Request same format as input if possible (jpg or png)
            if (ext == ".jpg" || ext == ".jpeg")
            {
                content.Add(new StringContent("jpg"), "format");
            }
            else
            {
                 content.Add(new StringContent("png"), "format");
            }
        }
        else // remove_bg
        {
            content.Add(new StringContent("png"), "format");
            content.Add(new StringContent("v1"), "model");
        }

        var response = await client.PostAsync(url, content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception(MapErrorToFriendlyMessage(response.StatusCode, errorBody));
        }

        // Save Result
        byte[] resultBytes = await response.Content.ReadAsByteArrayAsync();
        
        // Save to file
        string resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes);
    }

    private void AddHeaders(HttpClient client, string job)
    {
        if (!string.IsNullOrEmpty(ApiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", ApiKey);
        }
    }

    private string MapErrorToFriendlyMessage(HttpStatusCode statusCode, string? content = null)
    {
        if (!string.IsNullOrEmpty(content))
        {
            if (content.Contains("insufficient", StringComparison.OrdinalIgnoreCase) || 
                content.Contains("credit", StringComparison.OrdinalIgnoreCase))
                return "Kredit Habis (Isi saldo API Anda)";
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "API Key Salah (Cek Pengaturan)",
            HttpStatusCode.PaymentRequired => "Kredit Habis (Isi saldo API Anda)",
            HttpStatusCode.Forbidden => "Akses Ditolak (API Key tidak diizinkan)",
            HttpStatusCode.TooManyRequests => "Terlalu Cepat (Tunggu 1 menit)",
            HttpStatusCode.InternalServerError => "Server Pixa Sibuk (Coba lagi nanti)",
            HttpStatusCode.BadGateway => "Server Pixa Gangguan (Coba lagi nanti)",
            HttpStatusCode.ServiceUnavailable => "Server Pixa Down (Coba lagi nanti)",
            _ => $"Gagal (HTTP {(int)statusCode})"
        };
    }

    public async Task<string> GetCreditsAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return null;

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            AddHeaders(client, "credits");

            var response = await client.GetAsync("https://api.developer.pixelcut.ai/v1/credits");
            var json = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("credits_remaining", out var creditsElement) || 
                        root.TryGetProperty("creditsRemaining", out creditsElement) ||
                        root.TryGetProperty("credits", out creditsElement))
                    {
                        return creditsElement.GetDouble().ToString("N0");
                    }
                    return "Format Data Berubah";
                }
                catch
                {
                    return "Gagal Membaca Data";
                }
            }
            else
            {
                return MapErrorToFriendlyMessage(response.StatusCode, json);
            }
        }
        catch (Exception ex)
        {
            if (ex is TaskCanceledException || ex is HttpRequestException)
                return "Koneksi Gagal (Cek Internet)";
                
            return "Error Koneksi";
        }
    }

    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale")
        {
             // Match input extension
             var ext = Path.GetExtension(input);
             // Default to png if no extension
             if (string.IsNullOrEmpty(ext)) ext = ".png";
             return Path.Combine(dir, $"{name}_up{ext}");
        }
        return Path.Combine(dir, $"{name}.png");
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Threading;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services;

public class PixelcutService
{
    // Configurable from UI
    public string? ManualProxy { get; set; }

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
            ? "https://api2.pixelcut.app/image/upscale/v1" 
            : "https://api2.pixelcut.app/image/matte/v1";

        // Direct request with single attempt (or manual proxy)
        await ExecuteRequestAsync(item, endpoint, jobType, ManualProxy, CancellationToken.None);
    }

    public async Task ProcessImageAsync(PixelcutFileItem item, string jobType, CancellationToken ct)
    {
        string endpoint = jobType == "upscale" 
            ? "https://api2.pixelcut.app/image/upscale/v1" 
            : "https://api2.pixelcut.app/image/matte/v1";

        // Direct request with single attempt (or manual proxy)
        await ExecuteRequestAsync(item, endpoint, jobType, ManualProxy, ct);
    }

    private async Task ExecuteRequestAsync(PixelcutFileItem item, string url, string job, string? proxy, CancellationToken ct)
    {
        using var handler = new HttpClientHandler();
        
        // Configure Proxy
        if (!string.IsNullOrEmpty(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(10); // Increased from 2 to 10 mins for slow connections
        
        // Headers
        AddHeaders(client, job);

        using var content = new MultipartFormDataContent();
        
        // Read File
        byte[] fileBytes = await File.ReadAllBytesAsync(item.FilePath, ct);
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg"); // or generic
        content.Add(fileContent, "image", "image.jpg");

        // Parameters
        if (job == "upscale")
        {
            content.Add(new StringContent("2"), "scale");
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
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new Exception("Rate Limit (429)");
            
            throw new Exception($"HTTP {response.StatusCode}");
        }

        // Save Result
        byte[] resultBytes = await response.Content.ReadAsByteArrayAsync();
        
        // Save to file
        string resultPath = GetResultPath(item.FilePath, job);
        await File.WriteAllBytesAsync(resultPath, resultBytes);
    }

    private void AddHeaders(HttpClient client, string job)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://create.pixelcut.ai");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://create.pixelcut.ai/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-client-version", "web");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-platform", "web");
    }

    private string GetResultPath(string input, string job)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        
        if (job == "upscale") return Path.Combine(dir, $"{name}_up.png");
        return Path.Combine(dir, $"{name}.png");
    }
}

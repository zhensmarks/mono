using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdatesAsync();
}

public class UpdateService : IUpdateService
{
    private const string GITHUB_OWNER = "zhensmarks";
    private const string GITHUB_REPO = "BMachine.v2";
    private const string USER_AGENT = "BMachine-App";

    public async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        var result = new UpdateInfo
        {
            IsUpdateAvailable = false,
            CurrentVersion = GetCurrentVersion(),
            LatestVersion = GetCurrentVersion() // Default to current
        };

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(USER_AGENT, result.CurrentVersion));
            
            // GitHub API requires User-Agent
            var url = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("tag_name", out var tagElement))
                {
                    string tagName = tagElement.GetString() ?? "";
                    // Clean valid tag (e.g. "v2.0.1" -> "2.0.1")
                    string versionStr = tagName.TrimStart('v', 'V');
                    
                    result.LatestVersion = tagName;
                    result.DownloadUrl = root.GetProperty("html_url").GetString() ?? "";
                    
                    // Simple Version Comparison
                    if (Version.TryParse(versionStr, out Version? remoteVersion) && 
                        Version.TryParse(result.CurrentVersion, out Version? localVersion))
                    {
                        Console.WriteLine($"[UpdateService] Checking Update: Local='{result.CurrentVersion}' (parsed {localVersion}), Remote='{versionStr}' (parsed {remoteVersion})");
                        
                        if (remoteVersion > localVersion)
                        {
                            result.IsUpdateAvailable = true;
                            result.ReleaseNotes = root.GetProperty("body").GetString() ?? "";
                            Console.WriteLine("[UpdateService] Update FOUND.");
                        }
                        else
                        {
                            Console.WriteLine("[UpdateService] No update found (Remote <= Local).");
                        }
                    }
                    else
                    {
                        // String fallback comparison if Version parse fails (e.g. 2.0.1-beta)
                        if (!string.Equals(versionStr, result.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            // Caveat: Lexical comparison isn't perfect for SemVer, but works for simple cases
                            // result.IsUpdateAvailable = true; 
                            // Better not to alert false positives on complex tags.
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] Error checking updates: {ex.Message}");
        }
        
        return result;
    }

    private string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        Console.WriteLine($"[UpdateService] Using Assembly: {assembly.GetName().Name}, Version: {assembly.GetName().Version}");
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace BMachine.UI.Services;

/// <summary>
/// Lightweight JPG thumbnail provider for the Master browser.
/// Thumbnails are decoded downscaled on a background thread and cached to disk
/// so they are not decoded again on subsequent loads.
/// </summary>
public static class ThumbnailCacheService
{
    private const int DefaultMaxEdge = 96;

    // In-memory cache keyed by cache key (path + write ticks) so the same
    // bitmap instance is reused while the app is running.
    private static readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
    private static readonly ConcurrentDictionary<string, byte> _failedKeys = new();
    private static readonly System.Threading.SemaphoreSlim _decodeGate = new(4, 4);

    private static readonly string _cacheDir = BuildCacheDir();

    private static string BuildCacheDir()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.GetTempPath();
            var dir = Path.Combine(baseDir, "BMachine", "ThumbCache");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    private static readonly string[] _jpgExtensions = { ".jpg", ".jpeg" };

    /// <summary>
    /// Finds a JPG/JPEG sitting next to the given file with the same base name.
    /// Returns null when there is no matching preview.
    /// </summary>
    public static string? ResolvePairJpg(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(baseName)) return null;

            foreach (var ext in _jpgExtensions)
            {
                var candidate = Path.Combine(dir, baseName + ext);
                if (File.Exists(candidate)) return candidate;
            }

            // Case-insensitive fallback: scan the directory for a name match.
            var baseLower = baseName.ToLowerInvariant();
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(_jpgExtensions, ext) < 0) continue;
                if (Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == baseLower)
                    return f;
            }
        }
        catch
        {
            // Ignore access errors and treat as "no preview".
        }

        return null;
    }

    /// <summary>
    /// Returns a small thumbnail bitmap for the given JPG path, or null on failure.
    /// Safe to call from any thread; decoding happens on a background thread.
    /// </summary>
    public static async Task<Bitmap?> GetThumbnailAsync(string jpgPath, int maxEdge = DefaultMaxEdge)
    {
        if (string.IsNullOrEmpty(jpgPath) || !File.Exists(jpgPath)) return null;

        string key = BuildKey(jpgPath);
        if (_failedKeys.ContainsKey(key)) return null;

        if (_memoryCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        // Limit how many JPGs are decoded at once so a folder with many files
        // does not saturate the thread pool or stall the UI.
        await _decodeGate.WaitAsync();
        try
        {
            return await Task.Run(() => LoadOrCreate(jpgPath, key, maxEdge));
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private static string BuildKey(string jpgPath)
    {
        long ticks = 0;
        try { ticks = File.GetLastWriteTimeUtc(jpgPath).Ticks; }
        catch { }
        return $"{jpgPath}|{ticks}";
    }

    private static Bitmap? LoadOrCreate(string jpgPath, string key, int maxEdge)
    {
        try
        {
            var cacheFile = Path.Combine(_cacheDir, HashKey(key) + ".png");

            // Disk cache hit and still newer than the source -> load it.
            if (File.Exists(cacheFile))
            {
                try
                {
                    var srcTime = File.GetLastWriteTimeUtc(jpgPath);
                    var cacheTime = File.GetLastWriteTimeUtc(cacheFile);
                    if (cacheTime >= srcTime)
                    {
                        using var fs = File.OpenRead(cacheFile);
                        var bmp = new Bitmap(fs);
                        _memoryCache[key] = bmp;
                        return bmp;
                    }
                }
                catch
                {
                    // Corrupt cache entry; fall through and rebuild.
                }
            }

            // Decode downscaled directly from the source (avoids full-size decode).
            Bitmap? thumb = null;
            try
            {
                using var stream = File.OpenRead(jpgPath);
                thumb = Bitmap.DecodeToWidth(stream, maxEdge, BitmapInterpolationMode.MediumQuality);
            }
            catch
            {
                // Fallback: decode full size then only keep a small copy.
                try
                {
                    using var stream = File.OpenRead(jpgPath);
                    thumb = new Bitmap(stream);
                }
                catch
                {
                    thumb = null;
                }
            }

            if (thumb == null)
            {
                _failedKeys[key] = 1;
                return null;
            }

            // Persist to the disk cache for the next run.
            try
            {
                using var outStream = File.Create(cacheFile);
                thumb.Save(outStream);
            }
            catch
            {
                // Disk cache is best-effort only.
            }

            _memoryCache[key] = thumb;
            return thumb;
        }
        catch
        {
            _failedKeys[key] = 1;
            return null;
        }
    }

    private static string HashKey(string key)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

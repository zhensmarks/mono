using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BMachine.UI.Services;

/// <summary>
/// Menulis context JSON ke file temp dengan nama unik per pemanggilan,
/// sehingga dua pemicu berurutan tidak saling mengunci file yang sama.
/// Pembaca script (mis. replace.jsx) memakai file bmachine_context_*.json terbaru.
/// </summary>
public static class BmachineContextService
{
    private const string FilePrefix = "bmachine_context_";

    public static async Task<string> WriteContextAsync(
        string json,
        Action<string>? log = null,
        bool alsoWriteFixedName = false)
    {
        var fileName = $"{FilePrefix}{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
        var path = Path.Combine(Path.GetTempPath(), fileName);

        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            await sw.WriteAsync(json);
        }

        if (alsoWriteFixedName)
        {
            var fixedPath = Path.Combine(Path.GetTempPath(), "bmachine_context.json");
            try
            {
                using (var ffs = new FileStream(fixedPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var fsw = new StreamWriter(ffs, new UTF8Encoding(false)))
                {
                    await fsw.WriteAsync(json);
                }
            }
            catch
            {
                // Abaikan; nama unik sudah cukup bagi pembaca yang diperbarui.
            }
        }

        TryCleanupStaleFiles();

        log?.Invoke(path);
        return path;
    }

    /// <summary>Bersihkan file context lama (lebih dari satu hari).</summary>
    private static void TryCleanupStaleFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);
            var stale = Directory.GetFiles(Path.GetTempPath(), FilePrefix + "*.json")
                .Where(f =>
                {
                    try
                    {
                        return File.GetLastWriteTimeUtc(f) < cutoff;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
            foreach (var file in stale)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // File masih dipakai proses lain; biarkan.
                }
            }
        }
        catch
        {
            // Aman: pembersihan hanya usaha terbaik.
        }
    }
}
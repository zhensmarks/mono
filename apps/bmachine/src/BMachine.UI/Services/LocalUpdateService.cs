using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

/// <summary>
/// Mode update lokal (LAN). Alih-alih menembak GitHub, aplikasi membaca
/// <c>manifest.json</c> dari folder update yang dibagi di jaringan (default
/// <c>\\ABENG\mono\updates</c>), lalu menyalin seluruh payload (exe + Scripts\Master +
/// Scripts\Action + Assets) ke folder instalasi lewat helper PowerShell setelah
/// aplikasi ditutup, kemudian membuka ulang aplikasi.
///
/// Format manifest:
/// {
///   "version": "8.4.0",
///   "releasedAt": "2026-10-30T14:00:00+07:00",
///   "notes": ["Checklist Trello auto-refresh", "Tombol refresh di panel checklist"],
///   "payload": "payload"
/// }
/// </summary>
public static class LocalUpdateService
{
    /// <summary>Folder update default: server NAS kantor (bisa diakses semua PC tanpa share lokal).</summary>
    public const string DefaultFolder = @"\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";

    /// <summary>Cadangan kalau UNC tidak resolve tapi drive U: sudah dimapping.</summary>
    public const string DriveLetterFolder = @"U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";

    public const string ManifestFileName = "manifest.json";

    /// <summary>Batas tunggu baca manifest per kandidat folder.</summary>
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(4);

    public static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    /// <summary>Folder yang ditampilkan di Settings: env var (opsional) → isian user → default NAS.</summary>
    public static string ResolveFolder(string? configured)
    {
        var candidates = ResolveCandidates(configured);
        return candidates.Count > 0 ? candidates[0] : DefaultFolder;
    }

    /// <summary>
    /// Urutan folder yang dicoba saat cek update:
    /// env var → isian user di Settings → share NAS (UNC) → drive U: (kalau dimapping).
    /// </summary>
    public static IReadOnlyList<string> ResolveCandidates(string? configured)
    {
        var list = new List<string>();

        void Add(string? path)
        {
            var value = (path ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value)) return;
            // Tolak path relatif/sampah: user tidak boleh bikin app kehilangan sumber update.
            if (!Path.IsPathRooted(value)) return;
            if (list.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(value);
        }

        Add(Environment.GetEnvironmentVariable("BMACHINE_UPDATE_DIR"));
        Add(configured);
        Add(DefaultFolder);
        Add(DriveLetterFolder);

        return list;
    }

    /// <summary>
    /// Baca manifest dari folder update dan bandingkan dengan versi aplikasi.
    /// Tidak pernah melempar exception: kegagalan akses dikembalikan sebagai status.
    /// </summary>
    public static async Task<(UpdateInfo Info, string Status)> CheckAsync(string? folder, bool allowFallback = true)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = GetCurrentVersion(),
            LatestVersion = GetCurrentVersion(),
            IsLocalSource = true
        };

        var errors = new List<string>();

        // Coba berurutan: setting/env → share NAS (UNC) → drive U:.
        var candidates = allowFallback
            ? ResolveCandidates(folder)
            : new[] { ResolveFolder(folder) };

        foreach (var candidate in candidates)
        {
            UpdateInfo? manifest = null;
            string error = "";

            var readTask = Task.Run(() =>
            {
                var ok = TryReadManifest(candidate, out var parsed, out var parseError);
                manifest = parsed;
                error = parseError;
                return ok;
            });

            var finished = await Task.WhenAny(readTask, Task.Delay(ManifestTimeout)) == readTask;

            if (!finished)
            {
                errors.Add($"tidak merespons: {candidate}");
                continue;
            }

            if (!readTask.Result || manifest == null)
            {
                if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
                continue;
            }

            var parsedManifest = manifest;
            info.SourceFolder = candidate;
            info.LatestVersion = parsedManifest.LatestVersion;
            info.Notes = parsedManifest.Notes;
            info.ReleaseNotes = parsedManifest.ReleaseNotes;
            info.PayloadFolder = parsedManifest.PayloadFolder;
            info.DownloadUrl = parsedManifest.PayloadFolder;
            info.Sha256 = parsedManifest.Sha256;
            info.SizeBytes = parsedManifest.SizeBytes;

            var current = ParseVersion(info.CurrentVersion);
            var latest = ParseVersion(parsedManifest.LatestVersion);

            if (latest != null && current != null)
            {
                info.IsUpdateAvailable = latest > current;
            }
            else
            {
                info.IsUpdateAvailable = !string.Equals(
                    parsedManifest.LatestVersion.TrimStart('v', 'V'),
                    info.CurrentVersion,
                    StringComparison.OrdinalIgnoreCase);
            }

            var status = info.IsUpdateAvailable
                ? $"Versi baru v{parsedManifest.LatestVersion} tersedia."
                : $"Sudah versi terbaru (v{info.CurrentVersion}).";

            return (info, status);
        }

        info.SourceFolder = ResolveFolder(folder);
        var detail = errors.Count > 0 ? string.Join(" | ", errors) : "manifest.json tidak ditemukan";
        return (info, $"Folder update tidak bisa dibaca ({detail})");
    }

    private static bool TryReadManifest(string folder, out UpdateInfo? info, out string error)
    {
        info = null;
        error = "";

        try
        {
            var path = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(path))
            {
                error = $"manifest.json tidak ditemukan di {folder}";
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (!root.TryGetProperty("version", out var versionEl) || versionEl.ValueKind != JsonValueKind.String)
            {
                error = "manifest.json tidak punya field 'version'.";
                return false;
            }

            var payloadName = root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.String
                ? payloadEl.GetString() ?? "payload"
                : "payload";

            var result = new UpdateInfo
            {
                IsLocalSource = true,
                SourceFolder = folder,
                LatestVersion = (versionEl.GetString() ?? "").Trim().TrimStart('v', 'V'),
                PayloadFolder = Path.Combine(folder, payloadName),
                Sha256 = root.TryGetProperty("sha256", out var shaEl) && shaEl.ValueKind == JsonValueKind.String
                    ? (shaEl.GetString() ?? "").Trim()
                    : ""
            };

            if (root.TryGetProperty("notes", out var notesEl))
            {
                if (notesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var note in notesEl.EnumerateArray())
                    {
                        if (note.ValueKind != JsonValueKind.String) continue;
                        var text = (note.GetString() ?? "").Trim();
                        if (!string.IsNullOrEmpty(text)) result.Notes.Add(text);
                    }
                }
                else if (notesEl.ValueKind == JsonValueKind.String)
                {
                    foreach (var line in (notesEl.GetString() ?? "").Split('\n'))
                    {
                        var text = line.Trim().TrimStart('-', '*', '•').Trim();
                        if (!string.IsNullOrEmpty(text)) result.Notes.Add(text);
                    }
                }
            }

            if (root.TryGetProperty("sizeBytes", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
            {
                result.SizeBytes = sizeEl.TryGetInt64(out var size) ? size : 0;
            }

            info = result;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Gagal membaca manifest: {ex.Message}";
            return false;
        }
    }

    private static Version? ParseVersion(string value)
    {
        var clean = (value ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Terapkan update: tulis helper PowerShell yang menunggu proses ini keluar,
    /// menyalin payload ke folder instalasi, lalu membuka aplikasi lagi.
    /// Pemanggil bertanggung jawab menutup aplikasi setelah method ini sukses.
    /// </summary>
    public static async Task<(bool Ok, string Message)> ApplyAsync(UpdateInfo info)
    {
        if (info == null || !info.IsLocalSource || string.IsNullOrWhiteSpace(info.PayloadFolder))
            return (false, "Sumber update tidak valid.");

        var payload = info.PayloadFolder;
        if (!Directory.Exists(payload))
            return (false, $"Folder payload tidak ditemukan: {payload}");

        var payloadExe = Path.Combine(payload, "BMachine.App.exe");
        if (!File.Exists(payloadExe))
            return (false, "Payload tidak berisi BMachine.App.exe.");

        // Verifikasi isi payload: kalau hash tidak cocok dengan manifest, update dibatalkan.
        // Ini yang bikin folder update aman walau ada yang mencoba menaruh exe lain.
        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            var actual = await ComputeSha256Async(payloadExe);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                return (false, "Payload tidak cocok dengan manifest (SHA256 beda). Update dibatalkan.");
        }

        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var script = BuildApplyScript(payload, installDir, Environment.ProcessId);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"bmachine-update-{Guid.NewGuid():N}.ps1");

        try
        {
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
            return (true, "Update disiapkan. Aplikasi akan menutup lalu terbuka lagi dengan versi baru.");
        }
        catch (Exception ex)
        {
            return (false, $"Gagal menyiapkan update: {ex.Message}");
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static string BuildApplyScript(string payload, string installDir, int processId)
    {
        static string Q(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        var log = Path.Combine(Path.GetTempPath(), "bmachine-update.log");

        return $$"""
$ErrorActionPreference = 'Continue'
$log = {{Q(log)}}
function Write-Log([string]$m) {
    try { Add-Content -LiteralPath $log -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) } catch { }
}

$install = {{Q(installDir)}}
$payload = {{Q(payload)}}
$exe     = Join-Path $install 'BMachine.App.exe'

Write-Log '--- mulai update ---'
try { Wait-Process -Id {{processId}} -Timeout 180 -ErrorAction SilentlyContinue } catch { }
Start-Sleep -Milliseconds 800

if (Test-Path -LiteralPath $exe) {
    try { Copy-Item -LiteralPath $exe -Destination ($exe + '.bak') -Force }
    catch { Write-Log ('backup gagal: ' + $_.Exception.Message) }
}

$copied = $true
try {
    Copy-Item -Path (Join-Path $payload '*') -Destination $install -Recurse -Force -ErrorAction Stop
    Write-Log 'payload tersalin'
} catch {
    $copied = $false
    Write-Log ('salin payload gagal: ' + $_.Exception.Message)
}

if (-not $copied -and (Test-Path -LiteralPath ($exe + '.bak'))) {
    try { Copy-Item -LiteralPath ($exe + '.bak') -Destination $exe -Force; Write-Log 'exe lama dipulihkan' } catch { }
}

Write-Log 'membuka aplikasi'
Start-Process -FilePath $exe
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
""";
    }
}

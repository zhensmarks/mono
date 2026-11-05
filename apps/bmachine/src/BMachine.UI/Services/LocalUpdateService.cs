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
    /// <summary>Folder update default (channel STABLE): server NAS kantor.</summary>
    public const string DefaultFolder = @"\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";

    /// <summary>Folder update channel EXPERIMENTAL: saudara dari DefaultFolder.</summary>
    public const string ExperimentalFolder = @"\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine\experimental";

    /// <summary>Cadangan channel stable kalau UNC tidak resolve tapi drive U: sudah dimapping.</summary>
    public const string DriveLetterFolder = @"U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";

    /// <summary>Cadangan channel experimental (drive U:).</summary>
    public const string DriveLetterExperimentalFolder = @"U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine\experimental";

    public const string ManifestFileName = "manifest.json";

    /// <summary>Channel update: "stable" (default) atau "experimental".</summary>
    public const string ChannelStable = "stable";
    public const string ChannelExperimental = "experimental";

    /// <summary>
    /// Folder update yang dicoba untuk sebuah channel (urutan: env var override →
    /// UNC default channel → drive U: cadangan channel). Isian user (configured)
    /// tetap paling dulu dicoba kalau tidak kosong.
    /// </summary>
    public static IReadOnlyList<string> ResolveChannelCandidates(string channel, string? configured)
    {
        var isExp = string.Equals(channel, ChannelExperimental, StringComparison.OrdinalIgnoreCase);
        var list = new List<string>();

        void Add(string? path)
        {
            var value = (path ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!Path.IsPathRooted(value)) return;
            if (list.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(value);
        }

        Add(Environment.GetEnvironmentVariable("BMACHINE_UPDATE_DIR"));
        Add(configured);
        Add(isExp ? ExperimentalFolder : DefaultFolder);
        Add(isExp ? DriveLetterExperimentalFolder : DriveLetterFolder);
        return list;
    }

    /// <summary>Folder yang ditampilkan di Settings: env var (opsional) → isian user → folder channel default.</summary>
    public static string ResolveFolder(string? configured, string channel = ChannelStable)
    {
        var candidates = ResolveChannelCandidates(channel, configured);
        return candidates.Count > 0 ? candidates[0] : DefaultFolder;
    }


    /// <summary>Riwayat catatan rilis semua versi (opsional; dipakai saat user tertinggal beberapa versi).</summary>
    public const string ChangelogFileName = "changelog.json";

    /// <summary>Batas tunggu baca manifest per kandidat folder.</summary>
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(4);

    public static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    /// <summary>Cara cek update (channel stable, backward-compat): env → user → NAS UNC → drive U:.</summary>
    public static IReadOnlyList<string> ResolveCandidates(string? configured)
    {
        return ResolveChannelCandidates(ChannelStable, configured);
    }

    /// <summary>
    /// Baca manifest dari folder update dan bandingkan dengan versi aplikasi.
    /// Tidak pernah melempar exception: kegagalan akses dikembalikan sebagai status.
    /// Parameter <c>channel</c> menentukan folder mana yang dicoba (stable/experimental).
    /// </summary>
    public static async Task<(UpdateInfo Info, string Status)> CheckAsync(string? folder, bool allowFallback = true, string channel = ChannelStable)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = GetCurrentVersion(),
            LatestVersion = GetCurrentVersion(),
            IsLocalSource = true,
            Channel = channel
        };

        var errors = new List<string>();

        // Coba berurutan sesuai channel: env var → setting user → folder channel (UNC) → drive U:.
        var candidates = allowFallback
            ? ResolveChannelCandidates(channel, folder)
            : new[] { ResolveFolder(folder, channel) };

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
            info.Changelog = parsedManifest.Changelog;
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

        info.SourceFolder = ResolveFolder(folder, channel);
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

            result.Notes = ReadNotes(root);

            if (root.TryGetProperty("sizeBytes", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
            {
                result.SizeBytes = sizeEl.TryGetInt64(out var size) ? size : 0;
            }

            // Komponen update (opsional). Manifest lama tanpa field ini = Components kosong
            // → helper menjalankan update apa adanya (perilaku lama).
            result.Components = ReadComponents(root);

            // Changelog versi yang belum dipasang (opsional): dipakai dialog update untuk
            // menggabungkan catatan rilis kalau user tertinggal beberapa versi.
            result.Changelog = ReadChangelog(folder, result.LatestVersion);

            info = result;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Gagal membaca manifest: {ex.Message}";
            return false;
        }
    }

    /// <summary>Baca array/string "notes" jadi daftar poin (dipakai manifest maupun changelog).</summary>
    private static List<string> ReadNotes(JsonElement parent)
    {
        var notes = new List<string>();
        if (!parent.TryGetProperty("notes", out var notesEl)) return notes;

        if (notesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var note in notesEl.EnumerateArray())
            {
                if (note.ValueKind != JsonValueKind.String) continue;
                var text = (note.GetString() ?? "").Trim();
                if (!string.IsNullOrEmpty(text)) notes.Add(text);
            }
        }
        else if (notesEl.ValueKind == JsonValueKind.String)
        {
            foreach (var line in (notesEl.GetString() ?? "").Split('\n'))
            {
                var text = line.Trim().TrimStart('-', '*', '•').Trim();
                if (!string.IsNullOrEmpty(text)) notes.Add(text);
            }
        }

        return notes;
    }

    /// <summary>
    /// Baca changelog.json (opsional) dan ambil versi yang LEBIH BARU dari versi app tapi
    /// tidak melewati versi manifest (payloadnya belum ada). Urut naik: versi terlama dulu,
    /// supaya dialog update bisa menampilkan "------- vX -------" dari yang paling lama.
    /// Folder tanpa changelog.json atau file rusak = daftar kosong (pakai notes manifest).
    /// </summary>
    private static List<UpdateChangelogSection> ReadChangelog(string folder, string latestVersion)
    {
        var sections = new List<UpdateChangelogSection>();

        try
        {
            var path = Path.Combine(folder, ChangelogFileName);
            if (!File.Exists(path)) return sections;

            var current = ParseVersion(GetCurrentVersion());
            var latest = ParseVersion(latestVersion);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return sections;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var versionText = entry.TryGetProperty("version", out var versionEl) &&
                                  versionEl.ValueKind == JsonValueKind.String
                    ? (versionEl.GetString() ?? "").Trim().TrimStart('v', 'V')
                    : "";
                var parsed = ParseVersion(versionText);
                if (parsed == null) continue;
                if (current != null && parsed <= current) continue;   // sudah dipasang user
                if (latest != null && parsed > latest) continue;      // belum ada payload-nya

                var notes = ReadNotes(entry);
                if (notes.Count == 0) continue;

                sections.Add(new UpdateChangelogSection { Version = versionText, Notes = notes });
            }

            sections.Sort((a, b) =>
            {
                var va = ParseVersion(a.Version);
                var vb = ParseVersion(b.Version);
                if (va == null || vb == null) return string.CompareOrdinal(a.Version, b.Version);
                return va.CompareTo(vb);
            });
        }
        catch { /* changelog opsional: rusak/tidak terbaca = pakai catatan manifest */ }

        return sections;
    }

    private static List<ManifestComponentInfo> ReadComponents(JsonElement root)
    {
        var list = new List<ManifestComponentInfo>();
        if (!root.TryGetProperty("components", out var compEl) || compEl.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in compEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var comp = new ManifestComponentInfo();
            if (el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) comp.Name = n.GetString() ?? "";
            if (el.TryGetProperty("changed", out var c) && c.ValueKind == JsonValueKind.True) comp.Changed = true;
            if (el.TryGetProperty("included", out var i) && i.ValueKind == JsonValueKind.False) comp.Included = false;
            if (el.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Array)
            {
                foreach (var pathEl in p.EnumerateArray())
                    if (pathEl.ValueKind == JsonValueKind.String)
                    {
                        var v = pathEl.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) comp.Paths.Add(v);
                    }
            }
            if (!string.IsNullOrWhiteSpace(comp.Name)) list.Add(comp);
        }
        return list;
    }

    private static Version? ParseVersion(string value)
    {
        var clean = (value ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Terapkan update: tulis helper PowerShell yang menunggu proses ini keluar,
    /// menyalin payload ke folder instalasi, lalu membuka aplikasi lagi.
    /// Parameter <c>selectedComponents</c> = daftar nama komponen yang di-check user
    /// di dialog update (mis. ["Aplikasi","Master","Action"]). Kalau kosong / null
    /// (manifest lama tanpa components), helper menyalin payload apa adanya.
    /// Pemanggil bertanggung jawab menutup aplikasi setelah method ini sukses.
    /// </summary>
    public static async Task<(bool Ok, string Message)> ApplyAsync(UpdateInfo info, IEnumerable<string>? selectedComponents = null)
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
        var script = BuildApplyScript(payload, installDir, Environment.ProcessId, selectedComponents, info.Components);
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

    private static string BuildApplyScript(
        string payload,
        string installDir,
        int processId,
        IEnumerable<string>? selectedComponents,
        List<BMachine.UI.Models.ManifestComponentInfo> manifestComponents)
    {
        static string Q(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        var log = Path.Combine(Path.GetTempPath(), "bmachine-update.log");

        // Filter komponen: kalau user memilih subset, hanya salin komponen itu.
        // Kalau manifest tidak punya field components, salin semua (perilaku lama).
        var selectedNames = selectedComponents?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        bool useFilter = manifestComponents != null && manifestComponents.Count > 0 && selectedNames != null && selectedNames.Count > 0;

        string filterBlock;
        if (useFilter)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                selected = selectedNames,
                components = manifestComponents.Select(c => new { name = c.Name, changed = c.Changed, included = c.Included, paths = c.Paths })
            });
            filterBlock =
                "$componentsJson = " + Q(json) + "\n" +
                "$components = ($componentsJson | ConvertFrom-Json).components\n" +
                "$selectedSet = @(($componentsJson | ConvertFrom-Json).selected)\n" +
                "function Get-SelectedComp([string]$name) {\n" +
                "    $c = $components | Where-Object { $_.name -eq $name } | Select-Object -First 1\n" +
                "    if ($c -and $selectedSet -contains $c.name) { return $c }\n" +
                "    return $null\n" +
                "}\n";
        }
        else filterBlock = "$componentsJson = $null\n";

        var sb = new System.Text.StringBuilder();
        sb.Append("$ErrorActionPreference = 'Continue'\n");
        sb.Append($"$log = {Q(log)}\n");
        sb.Append("function Write-Log([string]$m) {\n");
        sb.Append("    try { Add-Content -LiteralPath $log -Value ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) } catch { }\n");
        sb.Append("}\n\n");
        sb.Append($"$install = {Q(installDir)}\n");
        sb.Append($"$payload = {Q(payload)}\n");
        sb.Append("$exe     = Join-Path $install 'BMachine.App.exe'\n\n");
        sb.Append(filterBlock);
        sb.Append("\n");

        sb.Append("Write-Log '--- mulai update ---'\n");
        sb.Append($"try {{ Wait-Process -Id {processId} -Timeout 180 -ErrorAction SilentlyContinue }} catch {{ }}\n");
        sb.Append("Start-Sleep -Milliseconds 800\n\n");

        // Komponen APLIKASI
        sb.Append("# === Komponen APLIKASI (BMachine.App.exe + Assets) ===\n");
        sb.Append("$appComp = Get-SelectedComp 'Aplikasi'\n");
        sb.Append("$copyApp = if ($componentsJson) { $appComp -ne $null } else { $true }\n");
        sb.Append("if ($copyApp) {\n");
        sb.Append("    if (Test-Path -LiteralPath $exe) {\n");
        sb.Append("        try { Copy-Item -LiteralPath $exe -Destination ($exe + '.bak') -Force } catch { Write-Log ('backup gagal: ' + $_.Exception.Message) }\n");
        sb.Append("    }\n");
        sb.Append("    $removeList = Join-Path $payload 'remove-on-update.txt'\n");
        sb.Append("    if (Test-Path -LiteralPath $removeList) {\n");
        sb.Append("        foreach ($rel in (Get-Content -LiteralPath $removeList)) {\n");
        sb.Append("            $rel = $rel.Trim()\n");
        sb.Append("            if ($rel -eq '' -or $rel.StartsWith('#')) { continue }\n");
        sb.Append("            $target = [IO.Path]::Combine($install, $rel.Replace('/', [IO.Path]::DirectorySeparatorChar))\n");
        sb.Append("            if (Test-Path -LiteralPath $target) {\n");
        sb.Append("                try { Remove-Item -LiteralPath $target -Force -ErrorAction Stop; Write-Log ('dihapus: ' + $rel) }\n");
        sb.Append("                catch { Write-Log ('gagal hapus ' + $rel + ': ' + $_.Exception.Message) }\n");
        sb.Append("            }\n");
        sb.Append("        }\n");
        sb.Append("    }\n");
        sb.Append("    try {\n");
        sb.Append("        Copy-Item -Path (Join-Path $payload 'BMachine.App.exe') -Destination $install -Force\n");
        sb.Append("        $assetsDir = Join-Path $payload 'Assets'\n");
        sb.Append("        if (Test-Path $assetsDir) {\n");
        sb.Append("            Copy-Item -Path $assetsDir -Destination (Join-Path $install 'Assets') -Recurse -Force\n");
        sb.Append("        }\n");
        sb.Append("        Write-Log 'komponen Aplikasi tersalin'\n");
        sb.Append("    } catch { Write-Log ('gagal salin Aplikasi: ' + $_.Exception.Message) }\n");
        sb.Append("} else { Write-Log 'komponen Aplikasi dilewati (user tidak pilih; file lama di-skip, tidak dihapus)'; if (Test-Path -LiteralPath $exe) { try { Remove-Item -LiteralPath ($exe + '.bak') -Force -ErrorAction SilentlyContinue } catch { } } }\n\n");

        // Komponen MASTER
        sb.Append("# === Komponen MASTER (Scripts\\Master) ===\n");
        sb.Append("$masterComp = Get-SelectedComp 'Master'\n");
        sb.Append("$copyMaster = if ($componentsJson) { $masterComp -ne $null } else { $true }\n");
        sb.Append("if ($copyMaster) {\n");
        sb.Append("    $masterDir = Join-Path $payload 'Scripts\\Master'\n");
        sb.Append("    if (Test-Path $masterDir) {\n");
        sb.Append("        $masterDst = Join-Path $install 'Scripts\\Master'\n");
        sb.Append("        if (-not (Test-Path $masterDst)) { New-Item -ItemType Directory -Path $masterDst -Force | Out-Null }\n");
        sb.Append("        try {\n");
        sb.Append("            Get-ChildItem -Path $masterDir -Recurse -Force | ForEach-Object {\n");
        sb.Append("                $rel = $_.FullName.Substring($masterDir.Length).TrimStart('\\')\n");
        sb.Append("                $target = Join-Path $masterDst $rel\n");
        sb.Append("                if ($_.PSIsContainer) {\n");
        sb.Append("                    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }\n");
        sb.Append("                } else {\n");
        sb.Append("                    if (-not (Test-Path (Split-Path $target))) { New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null }\n");
        sb.Append("                    Copy-Item -LiteralPath $_.FullName -Destination $target -Force\n");
        sb.Append("                }\n");
        sb.Append("            }\n");
        sb.Append("            Write-Log 'komponen Master tersalin'\n");
        sb.Append("        } catch { Write-Log ('gagal salin Master: ' + $_.Exception.Message) }\n");
        sb.Append("    }\n");
        sb.Append("} else { Write-Log 'komponen Master dilewati (user tidak pilih; modifikasi user aman, folder lama tetap)' }\n\n");

        // Komponen ACTION
        sb.Append("# === Komponen ACTION (Scripts\\Action) ===\n");
        sb.Append("$actionComp = Get-SelectedComp 'Action'\n");
        sb.Append("$copyAction = if ($componentsJson) { $actionComp -ne $null } else { $true }\n");
        sb.Append("if ($copyAction) {\n");
        sb.Append("    $actionDir = Join-Path $payload 'Scripts\\Action'\n");
        sb.Append("    if (Test-Path $actionDir) {\n");
        sb.Append("        $actionDst = Join-Path $install 'Scripts\\Action'\n");
        sb.Append("        if (-not (Test-Path $actionDst)) { New-Item -ItemType Directory -Path $actionDst -Force | Out-Null }\n");
        sb.Append("        try {\n");
        sb.Append("            Get-ChildItem -Path $actionDir -Recurse -Force | ForEach-Object {\n");
        sb.Append("                $rel = $_.FullName.Substring($actionDir.Length).TrimStart('\\')\n");
        sb.Append("                $target = Join-Path $actionDst $rel\n");
        sb.Append("                if ($_.PSIsContainer) {\n");
        sb.Append("                    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }\n");
        sb.Append("                } else {\n");
        sb.Append("                    if (-not (Test-Path (Split-Path $target))) { New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null }\n");
        sb.Append("                    Copy-Item -LiteralPath $_.FullName -Destination $target -Force\n");
        sb.Append("                }\n");
        sb.Append("            }\n");
        sb.Append("            Write-Log 'komponen Action tersalin'\n");
        sb.Append("        } catch { Write-Log ('gagal salin Action: ' + $_.Exception.Message) }\n");
        sb.Append("    }\n");
        sb.Append("} else { Write-Log 'komponen Action dilewati (user tidak pilih; modifikasi user aman, folder lama tetap)' }\n\n");

        // Perilaku lama: salin payload apa adanya
        sb.Append("if (-not $componentsJson) {\n");
        sb.Append("    # Mode lama: tanpa filter komponen, salin seluruh payload\n");
        sb.Append("    try {\n");
        sb.Append("        Copy-Item -Path (Join-Path $payload '*') -Destination $install -Force\n");
        sb.Append("        Write-Log 'payload tersalin (mode lama)'\n");
        sb.Append("    } catch {\n");
        sb.Append("        if (Test-Path -LiteralPath ($exe + '.bak')) {\n");
        sb.Append("            try { Copy-Item -LiteralPath ($exe + '.bak') -Destination $exe -Force; Write-Log 'exe lama dipulihkan' } catch { }\n");
        sb.Append("        }\n");
        sb.Append("    }\n");
        sb.Append("}\n\n");

        sb.Append("Write-Log 'membuka aplikasi'\n");
        sb.Append("Start-Process -FilePath $exe\n");
        sb.Append("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\n");

        return sb.ToString();
    }
}

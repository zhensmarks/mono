using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Abeng.BugReporter;

/// <summary>
/// Kirim bug report / crash ke ProjectBot (HTTP lokal).
///
/// Alur: app -> POST http://127.0.0.1:21478/bug -> ProjectBot simpan ke inbox
///       + kirim notifikasi ke topik Telegram "Project & Bug".
///
/// Kelas ini sengaja dibuat tahan banting: setiap kegagalan jaringan ditelan
/// diam-diam ( lihat <see cref="SendAsync"/> ). Membuat laporan tidak boleh
/// pernah menjatuhkan aplikasi yang sedang crash.
/// </summary>
public sealed class BugReporter
{
    /// <summary>Port default ProjectBot bug bridge (lihat ProjectBot/lib/httpbug.js).</summary>
    public const int DefaultPort = 21478;

    private static readonly HttpClient _client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    // Case-insensitive + camelCase. Alasan dua arah:
    //   - DESERIALIZE: server balas "ok"/"id" (camelCase), properti C# PascalCase.
    //   - SERIALIZE  : server validasi field "project"/"summary" (camelCase),
    //     padahal properti C# PascalCase. Tanpa camelCase -> 400 "field wajib".
    private static readonly JsonSerializerOptions BugJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _endpoint;
    private readonly string _project;

    /// <param name="project">Nama project terdaftar di config ProjectBot
    /// (mis. "BMachine.v2", "PixelcutCompact").</param>
    /// <param name="port">Port bug bridge ProjectBot. Default 21478.</param>
    public BugReporter(string project, int port = DefaultPort)
    {
        _project = string.IsNullOrWhiteSpace(project)
            ? "Unknown"
            : project.Trim();
        _endpoint = $"http://127.0.0.1:{port}/bug";
    }

    /// <summary>
    /// Kirim bug. Selalu return (tidak lempar). Kembalikan true kalau ProjectBot
    /// menerima (balasan { ok: true }).
    /// </summary>
    public async Task<bool> SendAsync(string summary, string text,
        string? appVersion = null, string severity = "sedang",
        string source = "manual")
    {
        try
        {
            var payload = new BugPayload
            {
                Project = _project,
                Summary = string.IsNullOrWhiteSpace(summary)
                    ? "(tidak ada judul)"
                    : summary.Trim(),
                Text = string.IsNullOrWhiteSpace(text) ? summary?.Trim() ?? "" : text.Trim(),
                AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim(),
                Severity = severity,
                Source = source == "crash" ? "crash" : "manual",
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, BugJsonOptions), Encoding.UTF8, "application/json");

            using var resp = await _client.PostAsync(_endpoint, content);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BridgeResponse>(body,
                BugJsonOptions);
            return result?.Ok == true;
        }
        catch
        {
            // Kegagalan jaringan (ProjectBot mati / port tutup / timeout)
            // TIDAK boleh menjatuhkan app. false = tidak terkirim, panggilan
            // harus fallback ke file lokal (lihat ReportCrash).
            return false;
        }
    }

    /// <summary>
    /// Laporkan exception: kirim kalau ProjectBot bisa dihubungi, dan selalu
    /// simpan ke file lokal sebagai cadangan (-crash.log / crash_report.txt).
    /// </summary>
    public void ReportCrash(Exception ex, string? appVersion = null,
        string context = "unhandled")
    {
        // 1) selalu simpan cadangan lokal dulu (cepat & sync)
        try
        {
            var logPath = GetLocalCrashLogPath();
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context.ToUpper()}:\n{ex}\n\n";
            System.IO.File.AppendAllText(logPath, msg);
        }
        catch { /* gagal simpan lokal pun tidak apa-apa */ }

        // 2) coba kirim ke ProjectBot. Tidak di-await: handler crash harus
        //    selesai cepat supaya proses bisa keluar / dump tidak tertunda.
        _ = SendAsync(
            summary: FirstLine(ex) is { } fl && fl.Length > 0 ? fl : $"{context}: {ex.GetType().Name}",
            text: $"Context: {context}\n\n{ex}",
            appVersion: appVersion,
            severity: "tinggi",
            source: "crash");
    }

    /// <summary>Cek apakah ProjectBot bug bridge bisa dihubungi (untuk UI: indikator).</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var resp = await _client.GetAsync(_endpoint);
            // Endpoint POST-only: 404 berarti servernya hidup tapi salah method.
            // 400/405 juga berarti server merespons -> bridge tersedia.
            return (int)resp.StatusCode == 404 || (int)resp.StatusCode == 405 || resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- helper ---

    private static string? FirstLine(Exception ex)
    {
        var msg = ex.Message?.Trim() ?? string.Empty;
        var nl = msg.IndexOfAny(new[] { '\r', '\n' });
        var line = nl > 0 ? msg.Substring(0, nl) : msg;
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private static string GetLocalCrashLogPath()
    {
        // Pakai %LOCALAPPDATA% &lt;Project&gt; \crash.log; fallback folder app.
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BMachine");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "crash.log");
        }
        catch
        {
            return "crash.log";
        }
    }

    private sealed class BugPayload
    {
        public string Project { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Text { get; set; } = "";
        public string? AppVersion { get; set; }
        public string Severity { get; set; } = "sedang";
        public string Source { get; set; } = "manual";
    }

    private sealed class BridgeResponse
    {
        public bool Ok { get; set; }
        public int Id { get; set; }
        public bool Forwarded { get; set; }
    }
}

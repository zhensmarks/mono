using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BMachine.UI.Models.MantraData;
namespace BMachine.UI.Services.MantraData;

public class PhotoshopBridgeService
{
    private readonly MantraDataSettings _settings;

    public PhotoshopBridgeService(MantraDataSettings settings)
    {
        _settings = settings;
    }

    public async Task<ProcessReport> RunProcessAsync(string dataJsonPath, string psdDir, string fotoDir, IProgress<string>? progress = null, string operation = "full", IReadOnlyList<string>? fields = null)
    {
        var tempDir = Path.GetTempPath();
        var cfgPath = Path.Combine(tempDir, "yb_process_config.json");
        var reportPath = Path.Combine(tempDir, "yb_process_report.json");
        var pointerPath = Path.Combine(tempDir, "YB_PROCESS_CONFIG.txt");

        // Delete old report if exists
        if (File.Exists(reportPath)) File.Delete(reportPath);

        // 1. Write Config
        var config = new
        {
            data_json = dataJsonPath,
            psd_dir = psdDir,
            foto_dir = fotoDir,
            report_json = reportPath,
            operation,
            fields = fields ?? Array.Empty<string>()
        };
        File.WriteAllText(cfgPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(pointerPath, cfgPath);

        // 2. Locate JSX script
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var jsxPath = Path.Combine(appDir, "Scripts", "bdater_process.jsx");
        if (!File.Exists(jsxPath))
        {
            // Fallback for dev environment or project directory
            jsxPath = Path.Combine(appDir, "..", "..", "..", "..", "..", "Scripts", "bdater_process.jsx");
            if (!File.Exists(jsxPath))
            {
                jsxPath = Path.Combine(appDir, "..", "..", "..", "Scripts", "bdater_process.jsx");
            }
            if (File.Exists(jsxPath))
            {
                jsxPath = Path.GetFullPath(jsxPath);
            }
        }

        progress?.Report("Menghubungi Adobe Photoshop...");

        // 3. Dispatch via COM on a dedicated STA thread (COM Photoshop.Application requires STA)
        bool comSuccess = false;
        Exception? comException = null;
        var staThread = new Thread(() =>
        {
            try
            {
                var psType = Type.GetTypeFromProgID("Photoshop.Application");
                if (psType != null)
                {
                    dynamic? psApp = Activator.CreateInstance(psType);
                    if (psApp != null)
                    {
                        psApp.BringToFront();
                        psApp.DoJavaScriptFile(jsxPath);
                        comSuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                comException = ex;
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        staThread.Join();

        if (!comSuccess && comException != null)
            progress?.Report($"COM Dispatch warning: {comException.Message}. Mencoba via CLI...");

        if (!comSuccess)
        {
            if (!File.Exists(_settings.PhotoshopExePath))
            {
                throw new FileNotFoundException($"Photoshop.exe tidak ditemukan di: {_settings.PhotoshopExePath}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = _settings.PhotoshopExePath,
                Arguments = $"\"{jsxPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        // 4. Poll for report with timeout (2 hours)
        progress?.Report("Photoshop sedang memproses layout PSD...");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 7200)
        {
            if (File.Exists(reportPath))
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(reportPath);
                    if (raw.Contains("\"done\": true") || raw.Contains("\"done\":true"))
                    {
                        var rep = JsonSerializer.Deserialize<ProcessReport>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (rep != null) return rep;
                    }
                }
                catch { }
            }
            await Task.Delay(1000);
        }

        throw new TimeoutException("Waktu tunggu proses Photoshop melebihi batas waktu.");
    }
}



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelcutCompact.Services;

public struct InstallProgressInfo
{
    public double Percentage { get; set; }
    public string Message { get; set; }
}

public struct RembgVersionInfo
{
    public string CurrentVersion { get; set; }
    public string LatestVersion { get; set; }
    public bool HasUpdate { get; set; }
}

public class RembgResourceManager
{
    private const string PythonUrl = "https://www.python.org/ftp/python/3.12.7/python-3.12.7-embed-amd64.zip";
    private const string PipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private const string PyPiApiUrl = "https://pypi.org/pypi/rembg/json";
    
    public string ResourcesDirectory { get; }
    public string PythonExecutablePath { get; }
    public string PythonVersionFile { get; }

    public RembgResourceManager()
    {
        ResourcesDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "Rembg");
        PythonExecutablePath = Path.Combine(ResourcesDirectory, "python.exe");
        PythonVersionFile = Path.Combine(ResourcesDirectory, ".version");
    }

    /// <summary>Get current installed rembg version from version file.</summary>
    public string GetInstalledVersion()
    {
        if (File.Exists(PythonVersionFile))
        {
            try { return File.ReadAllText(PythonVersionFile).Trim(); }
            catch { }
        }
        return "unknown";
    }

    /// <summary>Check PyPI for latest rembg version.</summary>
    public async Task<RembgVersionInfo> CheckForUpdatesAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var response = await client.GetStringAsync(PyPiApiUrl, ct);
            using var doc = JsonDocument.Parse(response);
            var latestVersion = doc.RootElement.GetProperty("info").GetProperty("version").GetString() ?? "unknown";
            var currentVersion = GetInstalledVersion();
            
            return new RembgVersionInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                HasUpdate = currentVersion != "unknown" && currentVersion != latestVersion
            };
        }
        catch
        {
            return new RembgVersionInfo { CurrentVersion = GetInstalledVersion(), LatestVersion = "unknown", HasUpdate = false };
        }
    }

    /// <summary>Get list of available rembg models from GitHub (model catalog).</summary>
    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct)
    {
        var models = new List<string>
        {
            "u2net", "u2netp", "u2net_human_seg", "u2net_cloth_seg",
            "isnet-general-use", "isnet-anime", "sam", "birefnet-general",
            "birefnet-portrait", "birefnet-dis", "birefnet-hd", "birefnet-anime",
            "bria-rmbg-1.4"
        };
        return await Task.FromResult(models);
    }

    /// <summary>Check which models are already downloaded.</summary>
    public Dictionary<string, bool> GetDownloadedModels()
    {
        var modelCacheDir = Path.Combine(ResourcesDirectory, "Lib", "site-packages", "rembg", "data");
        var result = new Dictionary<string, bool>();
        
        var models = new[] { "u2net", "u2netp", "u2net_human_seg", "u2net_cloth_seg",
            "isnet-general-use", "isnet-anime", "sam", "birefnet-general",
            "birefnet-portrait", "birefnet-dis", "birefnet-hd", "birefnet-anime", "bria-rmbg-1.4" };
        
        foreach (var model in models)
        {
            var modelFile = Path.Combine(modelCacheDir, $"{model}.onnx");
            result[model] = File.Exists(modelFile);
        }
        
        return result;
    }

    public bool IsInstalled()
    {
        // Pengecekan apakah Python sudah ada dan rembg sudah terinstall
        return File.Exists(PythonExecutablePath) && Directory.Exists(Path.Combine(ResourcesDirectory, "Lib", "site-packages", "rembg"));
    }

    public async Task DownloadAndInstallAsync(IProgress<InstallProgressInfo>? progress, CancellationToken ct, List<string>? selectedModels = null)
    {
        if (Directory.Exists(ResourcesDirectory))
        {
            try { Directory.Delete(ResourcesDirectory, true); } catch { }
        }
        Directory.CreateDirectory(ResourcesDirectory);

        string tempZipPath = Path.Combine(Path.GetTempPath(), $"python_embed_{Guid.NewGuid():N}.zip");

        try
        {
            // 1. Download Python Embedded
            progress?.Report(new InstallProgressInfo { Percentage = 5, Message = "Mendownload Python (0 MB)..." });
            using var client = new HttpClient();
            using var response = await client.GetAsync(PythonUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1 && progress != null;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalRead += bytesRead;

                if (canReportProgress)
                {
                    double percentage = 5d + ((double)totalRead / totalBytes * 15d); // Max 20%
                    string mbRead = (totalRead / 1048576.0).ToString("0.0");
                    string mbTotal = (totalBytes / 1048576.0).ToString("0.0");
                    progress?.Report(new InstallProgressInfo { Percentage = percentage, Message = $"Mendownload Python ({mbRead} / {mbTotal} MB)..." }); 
                }
            }
            fileStream.Close();

            // 2. Ekstrak Python
            progress?.Report(new InstallProgressInfo { Percentage = 25, Message = "Mengekstrak Python..." }); 
            ZipFile.ExtractToDirectory(tempZipPath, ResourcesDirectory, true);

            // 3. Modifikasi file _pth agar pip berfungsi
            progress?.Report(new InstallProgressInfo { Percentage = 30, Message = "Mengkonfigurasi Python..." });
            string pthFile = Path.Combine(ResourcesDirectory, "python312._pth");
            if (File.Exists(pthFile))
            {
                var lines = File.ReadAllLines(pthFile);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("#import site")) lines[i] = "import site";
                }
                File.WriteAllLines(pthFile, lines);
            }

            // 4. Download get-pip.py
            progress?.Report(new InstallProgressInfo { Percentage = 35, Message = "Mendownload installer PIP..." });
            string getPipPath = Path.Combine(ResourcesDirectory, "get-pip.py");
            var pipBytes = await client.GetByteArrayAsync(PipUrl, ct);
            await File.WriteAllBytesAsync(getPipPath, pipBytes, ct);

            // 5. Install pip
            progress?.Report(new InstallProgressInfo { Percentage = 40, Message = "Menginstal PIP..." });
            await RunProcessAsync(PythonExecutablePath, "get-pip.py --no-warn-script-location", ResourcesDirectory, 
                msg => progress?.Report(new InstallProgressInfo { Percentage = 45, Message = $"PIP: {msg}" }), ct);

            // 6. Install rembg & onnxruntime-gpu
            progress?.Report(new InstallProgressInfo { Percentage = 50, Message = "Menginstal paket GPU & REMBG (Ini membutuhkan waktu)..." });
            
            var rembgResult = await RunProcessAsync(PythonExecutablePath, "-m pip install onnxruntime-gpu \"rembg[cli]\" --no-warn-script-location", ResourcesDirectory,
                msg => progress?.Report(new InstallProgressInfo { Percentage = 75, Message = $"Install: {msg}" }), ct);
            if (!rembgResult) throw new Exception("Instalasi paket REMBG gagal.");

            // 7. Save installed version
            try
            {
                var versionOutput = new System.Text.StringBuilder();
                await RunProcessAsync(PythonExecutablePath, "-m pip show rembg", ResourcesDirectory,
                    msg => {
                        if (msg.StartsWith("Version:")) versionOutput.Append(msg.Replace("Version:", "").Trim());
                    }, ct);
                
                var version = versionOutput.ToString();
                if (string.IsNullOrWhiteSpace(version)) version = "1.0.0"; // fallback
                await File.WriteAllTextAsync(PythonVersionFile, version, ct);
            }
            catch { }

            progress?.Report(new InstallProgressInfo { Percentage = 100, Message = "Terpasang" }); // Selesai
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);
            }
            catch { }
        }
    }

    internal static async Task<bool> RunProcessAsync(string fileName, string args, string cwd, Action<string>? onOutput, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        Exception? outputCallbackException = null;
        var callbackExceptionLock = new object();
        void ForwardOutput(object? sender, DataReceivedEventArgs eventArgs)
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data) || onOutput == null) return;
            try
            {
                onOutput(eventArgs.Data);
            }
            catch (Exception ex)
            {
                // Keep draining both redirected streams even if a UI/progress callback fails.
                lock (callbackExceptionLock)
                {
                    outputCallbackException ??= ex;
                }
            }
        }

        process.OutputDataReceived += ForwardOutput;
        process.ErrorDataReceived += ForwardOutput;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // WaitForExitAsync waits for the process and the redirected output readers to finish.
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process may exit between HasExited and Kill.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed (Exit: {process.ExitCode}): {fileName} {args}");
        }

        lock (callbackExceptionLock)
        {
            if (outputCallbackException != null)
            {
                throw new InvalidOperationException("A process output callback failed.", outputCallbackException);
            }
        }

        return true;
    }

    public void Uninstall()
    {
        if (Directory.Exists(ResourcesDirectory))
        {
            try
            {
                Directory.Delete(ResourcesDirectory, true);
            }
            catch { }
        }
    }
}

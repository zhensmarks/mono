using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMachine.Core.Platform;

public class WindowsPlatformService : IPlatformService
{
    public void RevealFileInExplorer(string filePath)
    {
        Process.Start("explorer", $"/select,\"{filePath}\"");
    }

    public void OpenFolder(string folderPath)
    {
        Process.Start("explorer", folderPath);
    }

    public void OpenDateTimeSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true });
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public IEnumerable<string> GetPhotoshopSearchPaths()
    {
        return new[]
        {
            @"C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe",
            @"C:\Program Files\Adobe\Adobe Photoshop 2023\Photoshop.exe",
            @"C:\Program Files\Adobe\Adobe Photoshop 2022\Photoshop.exe",
            @"C:\Program Files\Adobe\Adobe Photoshop 2021\Photoshop.exe",
            @"C:\Program Files\Adobe\Adobe Photoshop 2020\Photoshop.exe"
        };
    }

    public void RunPythonScript(string scriptPath, bool gui)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true // Let OS handle .pyw -> pythonw or .py -> python
        };
        Process.Start(startInfo);
    }

    public async Task RunPythonScriptAsync(
        string scriptPath, 
        IEnumerable<string> args, 
        Dictionary<string, string>? envVars = null, 
        Action<string>? onOutput = null, 
        Action<string>? onError = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python", // Assume python is in PATH
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath) ?? ""
        };

        if (envVars != null)
        {
            foreach (var kvp in envVars)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }
        
        // Ensure UTF-8
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        // Add script path as first argument (if calling python executable)
        // Wait, current logic in BatchViewModel passes script via `args`? 
        // No, BatchVM calls `RunPythonProcess(args)` where args usually include the script.
        // But here we accept `scriptPath`. So we should prepend it.
        // Wait, `scriptPath` IS the script file.
        // So we run: python "scriptPath" arg1 arg2 ...
        
        // Add script path first
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        
        process.OutputDataReceived += (s, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) onError?.Invoke(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
    }

    public void RunJsxInPhotoshop(string jsxPath, string photoshopPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = photoshopPath,
            Arguments = $"-r \"{jsxPath}\"",
            UseShellExecute = false
        };
        Process.Start(startInfo);
    }

    public string GetAppDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = System.IO.Path.Combine(appData, "BMachine");
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
        return path;
    }
}

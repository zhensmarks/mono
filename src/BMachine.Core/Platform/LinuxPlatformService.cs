using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMachine.Core.Platform;

public class LinuxPlatformService : IPlatformService
{
    public void RevealFileInExplorer(string filePath)
    {
        // xdg-open opens the file, but to reveal...
        // Dolphin: --select
        // Nautilus: (no standard select arg that works everywhere)
        // Fallback: Just open the folder
        
        string folder = Path.GetDirectoryName(filePath) ?? "/";
        Process.Start("xdg-open", folder);
    }

    public void OpenFolder(string folderPath)
    {
        Process.Start("xdg-open", folderPath);
    }

    public void OpenDateTimeSettings()
    {
        // Best effort for GNOME
        try { Process.Start("gnome-control-center", "datetime"); } catch { }
    }

    public void OpenUrl(string url)
    {
        Process.Start("xdg-open", url);
    }

    public IEnumerable<string> GetPhotoshopSearchPaths()
    {
        return Array.Empty<string>(); // Photoshop not native on Linux
    }

    public void RunPythonScript(string scriptPath, bool gui)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
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
            FileName = "python3", 
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
        
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

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
        // Not supported
    }

    public string GetAppDataDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = System.IO.Path.Combine(home, ".config", "BMachine");
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
        return path;
    }

    public void OpenWithDefaultApp(string fileOrFolderPath)
    {
        Process.Start("xdg-open", $"\"{fileOrFolderPath}\"");
    }

    public void OpenWithDialog(string filePath)
    {
        // Linux: no universal "choose application" dialog; fall back to xdg-open
        try { Process.Start("xdg-open", $"\"{filePath}\""); } catch { }
    }

    public bool MoveToRecycleBin(string fileOrFolderPath)
    {
        if (string.IsNullOrEmpty(fileOrFolderPath) || (!System.IO.File.Exists(fileOrFolderPath) && !System.IO.Directory.Exists(fileOrFolderPath)))
            return false;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var trashFiles = System.IO.Path.Combine(home, ".local", "share", "Trash", "files");
        var trashInfo = System.IO.Path.Combine(home, ".local", "share", "Trash", "info");
        try
        {
            if (!System.IO.Directory.Exists(trashFiles))
                System.IO.Directory.CreateDirectory(trashFiles);
            if (!System.IO.Directory.Exists(trashInfo))
                System.IO.Directory.CreateDirectory(trashInfo);
        }
        catch
        {
            return false;
        }
        var name = System.IO.Path.GetFileName(fileOrFolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var dest = System.IO.Path.Combine(trashFiles, name);
        if (System.IO.File.Exists(dest) || System.IO.Directory.Exists(dest))
            dest = System.IO.Path.Combine(trashFiles, $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}");
        try
        {
            if (System.IO.Directory.Exists(fileOrFolderPath))
                System.IO.Directory.Move(fileOrFolderPath, dest);
            else
                System.IO.File.Move(fileOrFolderPath, dest);
            // Optional: write .trashinfo for XDG Trash spec (restore with original path)
            var baseName = System.IO.Path.GetFileName(dest);
            var infoPath = System.IO.Path.Combine(trashInfo, baseName + ".trashinfo");
            var origPath = fileOrFolderPath.Replace("\\", "/");
            var content = $"[Trash Info]\nPath={Uri.EscapeDataString(origPath)}\nDeletionDate={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}\n";
            System.IO.File.WriteAllText(infoPath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

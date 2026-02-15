using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMachine.Core.Platform;

public class MacPlatformService : IPlatformService
{
    public void RevealFileInExplorer(string filePath)
    {
        Process.Start("open", $"-R \"{filePath}\"");
    }

    public void OpenFolder(string folderPath)
    {
        Process.Start("open", $"\"{folderPath}\"");
    }

    public void OpenDateTimeSettings()
    {
        Process.Start("open", "/System/Library/PreferencePanes/DateAndTime.prefPane");
    }

    public void OpenUrl(string url)
    {
        Process.Start("open", url);
    }

    public IEnumerable<string> GetPhotoshopSearchPaths()
    {
        return new[]
        {
            "/Applications/Adobe Photoshop 2024/Adobe Photoshop 2024.app",
            "/Applications/Adobe Photoshop 2023/Adobe Photoshop 2023.app",
            "/Applications/Adobe Photoshop 2022/Adobe Photoshop 2022.app",
            "/Applications/Adobe Photoshop 2021/Adobe Photoshop 2021.app"
        };
    }

    public void RunPythonScript(string scriptPath, bool gui)
    {
        // macOS doesn't have native association for .pyw usually, so we run python3 explicitly
        // If gui is true, we could use python3 and it handles GUI fine (Tkinter etc)
        // or pythonw if installed, but python3 is safer default
        
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
        // On macOS, we use osascript to tell Photoshop to run the script
        // photoshopPath is expected to be the .app path, but for osascript we just need the app name key
        // However, if we want to support specific versions, we might need to be precise.
        // Simplest: "tell application <Name>"
        
        string appName = Path.GetFileNameWithoutExtension(photoshopPath); // e.g. "Adobe Photoshop 2024"
        if (string.IsNullOrEmpty(appName)) appName = "Adobe Photoshop 2024"; // Fallback

        // Escape double quotes in path
        string safeJsxPath = jsxPath.Replace("\"", "\\\"");
        
        string appleScript = $"-e 'tell application \"{appName}\" to do javascript (read file \"{safeJsxPath}\")'";
        
        Process.Start("osascript", appleScript);
    }

    public string GetAppDataDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = System.IO.Path.Combine(home, "Library", "Application Support", "BMachine");
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
        return path;
    }

    public void OpenWithDefaultApp(string fileOrFolderPath)
    {
        Process.Start("open", $"\"{fileOrFolderPath}\"");
    }

    public void OpenWithDialog(string filePath)
    {
        // macOS: use "open -a" with a simple osascript to invoke the "choose application" dialog
        try
        {
            var script = $"set theApp to (choose application with prompt \"Open with…\") \n " +
                         $"do shell script \"open -a \" & quoted form of (POSIX path of (path to theApp)) & \" \" & quoted form of \"{filePath.Replace("\"", "\\\"")}\"";
            Process.Start("osascript", $"-e '{script}'");
        }
        catch { }
    }

    public bool MoveToRecycleBin(string fileOrFolderPath)
    {
        if (string.IsNullOrEmpty(fileOrFolderPath) || (!System.IO.File.Exists(fileOrFolderPath) && !System.IO.Directory.Exists(fileOrFolderPath)))
            return false;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var trash = System.IO.Path.Combine(home, ".Trash");
        if (!System.IO.Directory.Exists(trash))
            return false;
        var name = System.IO.Path.GetFileName(fileOrFolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var dest = System.IO.Path.Combine(trash, name);
        if (System.IO.File.Exists(dest) || System.IO.Directory.Exists(dest))
            dest = System.IO.Path.Combine(trash, $"{name} {DateTime.UtcNow:yyyy-MM-dd HHmmss}");
        try
        {
            if (System.IO.Directory.Exists(fileOrFolderPath))
                System.IO.Directory.Move(fileOrFolderPath, dest);
            else
                System.IO.File.Move(fileOrFolderPath, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

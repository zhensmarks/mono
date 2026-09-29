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
        // On macOS, `open -a` does NOT trigger JSX execution in Photoshop.
        // The reliable approach is AppleScript via `osascript`, which tells Photoshop
        // to execute the script using `do javascript`.
        
        Console.WriteLine($"[MacPlatformService] RunJsxInPhotoshop called");
        Console.WriteLine($"  photoshopPath (raw): {photoshopPath}");
        Console.WriteLine($"  jsxPath: {jsxPath}");
        
        // Auto-correct the path (strip quotes, find .app bundle if needed)
        photoshopPath = ResolvePhotoshopAppPath(photoshopPath);
        Console.WriteLine($"  photoshopPath (resolved): {photoshopPath}");
        
        // Extract the app name from the .app path for AppleScript
        // e.g. "/Applications/Adobe Photoshop 2020/Adobe Photoshop 2020.app" -> "Adobe Photoshop 2020"
        string appName = System.IO.Path.GetFileNameWithoutExtension(photoshopPath);
        Console.WriteLine($"  appName: {appName}");
        
        // Escape single quotes in jsxPath for AppleScript
        string escapedJsxPath = jsxPath.Replace("'", "'\\''");
        
        // AppleScript command:
        // 1. First try `do javascript` which runs the JSX code directly
        // 2. Uses `POSIX file` to properly reference the file on macOS
        string appleScript = $"tell application \"{appName}\" to do javascript file \"{jsxPath}\"";
        
        Console.WriteLine($"  AppleScript: {appleScript}");
        
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(appleScript);
        
        try
        {
            var process = Process.Start(psi);
            if (process != null)
            {
                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"[MacPlatformService] osascript stderr: {e.Data}");
                };
                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"[MacPlatformService] osascript stdout: {e.Data}");
                };
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                Console.WriteLine($"[MacPlatformService] osascript process started (PID: {process.Id})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacPlatformService] ERROR launching Photoshop via osascript: {ex.Message}");
            
            // Fallback: try `open -a` as a last resort (opens the file with Photoshop)
            Console.WriteLine($"[MacPlatformService] Falling back to 'open -a'...");
            try
            {
                var fallbackPsi = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
                fallbackPsi.ArgumentList.Add("-a");
                fallbackPsi.ArgumentList.Add(photoshopPath);
                fallbackPsi.ArgumentList.Add(jsxPath);
                Process.Start(fallbackPsi);
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine($"[MacPlatformService] Fallback 'open -a' also failed: {fallbackEx.Message}");
            }
        }
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

    public string SilencePhotoshopWarnings(string photoshopPath)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var prefsDir = Path.Combine(home, "Library", "Preferences");
            if (!Directory.Exists(prefsDir))
                return "~/Library/Preferences not found.";

            var psFolders = Directory.GetDirectories(prefsDir, "Adobe Photoshop * Settings", SearchOption.TopDirectoryOnly);
            if (psFolders.Length == 0)
                return "Photoshop settings folder not found. Open Photoshop once first.";

            int updated = 0;
            foreach (var folder in psFolders)
            {
                WriteWarnRunningScripts(Path.Combine(folder, "PSUserConfig.txt"));
                updated++;
            }
            return $"Done! Updated {updated} settings folder(s). Restart Photoshop to apply.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    /// <summary>
    /// Auto-corrects a Photoshop path on macOS.
    /// Handles: embedded quotes, parent folder instead of .app, trailing slashes.
    /// </summary>
    private string ResolvePhotoshopAppPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        
        // 1. Strip any embedded literal quotes (the database may store "path" with quotes)
        path = path.Trim().Trim('"').Trim();
        
        // 2. Strip trailing slashes
        path = path.TrimEnd('/');
        
        // 3. If it already ends with .app and the directory exists, it's correct
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
        {
            return path;
        }
        
        // 4. If it's a directory but NOT a .app bundle, search for .app bundles inside
        if (Directory.Exists(path))
        {
            try
            {
                var appBundles = Directory.GetDirectories(path, "*.app");
                if (appBundles.Length > 0)
                {
                    // Prefer one containing "Photoshop" in the name
                    var photoshopApp = appBundles.FirstOrDefault(p => 
                        p.Contains("Photoshop", StringComparison.OrdinalIgnoreCase)) ?? appBundles[0];
                    Console.WriteLine($"[MacPlatformService] Auto-corrected path: {path} -> {photoshopApp}");
                    return photoshopApp;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MacPlatformService] Error scanning for .app bundles: {ex.Message}");
            }
        }
        
        // 5. Return as-is if we can't resolve
        return path;
    }

    public bool IsExecutableValid(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        
        // Auto-correct the path (strip quotes, find .app bundle if needed)
        path = ResolvePhotoshopAppPath(path);
        
        // On macOS, .app is a directory but treated as an executable bundle
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
        {
            return true;
        }

        return File.Exists(path);
    }

    private static void WriteWarnRunningScripts(string configFile)
    {
        const string key = "WarnRunningScripts";
        const string entry = "WarnRunningScripts 0";
        if (System.IO.File.Exists(configFile))
        {
            var lines = System.IO.File.ReadAllLines(configFile).ToList();
            int idx = lines.FindIndex(l => l.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = entry; else lines.Add(entry);
            System.IO.File.WriteAllLines(configFile, lines);
        }
        else
        {
            System.IO.File.WriteAllText(configFile, entry + "\n");
        }
    }
}

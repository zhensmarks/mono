using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMachine.Core.Platform;

public interface IPlatformService
{
    /// <summary>
    /// Reveal a file in the system file manager (Explorer, Finder, Nautilus).
    /// </summary>
    void RevealFileInExplorer(string filePath);

    /// <summary>
    /// Open a folder in the system file manager.
    /// </summary>
    void OpenFolder(string folderPath);

    /// <summary>
    /// Open the system Date/Time settings.
    /// </summary>
    void OpenDateTimeSettings();

    /// <summary>
    /// Open a URL in the default browser.
    /// </summary>
    void OpenUrl(string url);

    /// <summary>
    /// Get a list of potential Photoshop executable paths for this platform.
    /// </summary>
    IEnumerable<string> GetPhotoshopSearchPaths();

    /// <summary>
    /// Run a Python script.
    /// </summary>
    /// <param name="scriptPath">Full path to the script.</param>
    /// <param name="gui">If true, run as a GUI script (pythonw on Windows).</param>
    void RunPythonScript(string scriptPath, bool gui);

    /// <summary>
    /// Run a Python script asynchronously with arguments, environment variables, and output redirection.
    /// </summary>
    Task RunPythonScriptAsync(
        string scriptPath, 
        IEnumerable<string> args, 
        Dictionary<string, string>? envVars = null, 
        Action<string>? onOutput = null, 
        Action<string>? onError = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Run a JSX script in Photoshop.
    /// </summary>
    /// <param name="jsxPath">Full path to .jsx file.</param>
    /// <param name="photoshopPath">Full path to Photoshop executable/app.</param>
    void RunJsxInPhotoshop(string jsxPath, string photoshopPath);

    /// <summary>
    /// Get the platform-specific application data directory.
    /// e.g. %AppData%\BMachine (Windows), ~/Library/Application Support/BMachine (macOS), ~/.config/BMachine (Linux)
    /// </summary>
    string GetAppDataDirectory();

    /// <summary>
    /// Open a file or folder with the system default application (cross-platform).
    /// Windows: ShellExecute; macOS: open; Linux: xdg-open.
    /// </summary>
    void OpenWithDefaultApp(string fileOrFolderPath);

    /// <summary>
    /// Move a file or folder to the system recycle bin / trash (cross-platform).
    /// Windows: Recycle Bin; macOS: Trash; Linux: XDG Trash.
    /// Returns true if successful.
    /// </summary>
    bool MoveToRecycleBin(string fileOrFolderPath);

    /// <summary>
    /// Open the OS "Open with…" dialog for a file, allowing the user to pick an application.
    /// Windows: rundll32 shell32.dll,OpenAs_RunDLL; macOS: open -a (Finder choose-app); Linux: xdg-open fallback.
    /// </summary>
    void OpenWithDialog(string filePath);
}

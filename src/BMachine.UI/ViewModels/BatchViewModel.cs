using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BMachine.SDK;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.UI.Messages;
using BMachine.UI.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BMachine.UI.ViewModels;

/// <summary>
/// ViewModel for the Batch Master feature - manages source folders and output paths for batch processing.
/// </summary>
public partial class BatchViewModel : ObservableObject
{
    private readonly IDatabase? _database;

    /// <summary>
    /// Collection of source folder items dropped by the user.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolders))]
    [NotifyPropertyChangedFor(nameof(ShowDropZone))]
    private ObservableCollection<BatchFolderRoot> _sourceFolders = new();
    
    // Store current process to kill it later
    private System.Diagnostics.Process? _currentProcess;

    public BatchViewModel(IDatabase? database, Services.IProcessLogService? logService)
    {
        _database = database;
        _logService = logService;
        
        // Register for Stop Message
        WeakReferenceMessenger.Default.Register<StopProcessMessage>(this, (r, m) =>
        {
            if (m.Value) KillProcess();
        });

        // Register for Script Order Updates
        WeakReferenceMessenger.Default.Register<ScriptOrderChangedMessage>(this, (r, m) =>
        {
            LoadScripts();
        });

        _ = LoadOutputBasePathAsync();
        
        // Ensure HasFolders updates when items are added/removed
        SourceFolders.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasFolders));
            OnPropertyChanged(nameof(ShowDropZone));
        };
    }

    private void KillProcess()
    {
        try 
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.Kill(true); // Kill process tree (wrapper + python)
                _logService?.AddLog("[WARNING] Proses dihentikan oleh User.");
                _currentProcess = null;
            }
            IsProcessing = false;
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Error killing process: {ex.Message}");
        }
    }

    /// <summary>
    /// Output base path from Settings (Configs.Master.LocalOutput).
    /// </summary>
    [ObservableProperty]
    private string _outputBasePath = "";

    /// <summary>
    /// Whether there are folders in the list.
    /// </summary>
    public bool HasFolders => SourceFolders.Count > 0;

    /// <summary>
    /// Whether to show the drop zone (inverse of HasFolders).
    /// </summary>
    public bool ShowDropZone => !HasFolders;

    private readonly Services.IProcessLogService? _logService;

    /// <summary>
    /// Add folders from drag-drop operation.
    /// </summary>
    public void AddFolders(string[] paths)
    {
        // 1. Dispose existing items to clean up watchers
        if (SourceFolders != null)
        {
            foreach (var item in SourceFolders)
            {
                try { item.Dispose(); } catch {}
            }
        }

        // 2. Create new list safely
        var newCollection = new ObservableCollection<BatchFolderRoot>();

        foreach (var path in paths)
        {
            try 
            {
                if (Directory.Exists(path))
                {
                    // Ensure path doesn't have trailing slash for consistent name extraction
                    var effectivePath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var folderName = Path.GetFileName(effectivePath);
                    
                    // Smart Path Prediction
                    var relativePath = GetRelativePathFromMonth(effectivePath);
                    
                    // If folder is "PILIHAN", we point Output to its parent (Project Folder)
                    if (folderName.Equals("PILIHAN", System.StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        relativePath = Path.GetDirectoryName(relativePath) ?? relativePath;
                    }

                    var outputPath = string.IsNullOrEmpty(OutputBasePath) 
                        ? "" 
                        : Path.Combine(OutputBasePath, relativePath);

                    var parentName = new DirectoryInfo(path).Parent?.Name ?? "";
                    string displayName; 
                    string outputHeader; 
                    
                    if (folderName.Equals("PILIHAN", System.StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = $"...\\{parentName}";
                        outputHeader = parentName;
                    }
                    else
                    {
                        displayName = string.IsNullOrEmpty(parentName) ? folderName : $"{folderName}\\{parentName}";
                        outputHeader = folderName;
                    }

                    var item = new BatchFolderRoot
                    {
                        SourcePath = path,
                        FolderName = folderName,
                        DisplayName = displayName, 
                        OutputHeader = outputHeader,
                        OutputPath = outputPath,
                    };
                    
                    // Populate Source Root
                    item.RefreshSource();

                    // Populate Output Root
                    item.SetupOutputWatcher();

                    newCollection.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing path {path}: {ex.Message}");
            }
        }

        // 3. Atomic Assignment on UI Thread
        SourceFolders = newCollection;
        
        // Ensure manual notification if needed (though ObservableProperty handles it)
        OnPropertyChanged(nameof(SourceFolders));
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(ShowDropZone));
    }


    /// <summary>
    /// Refresh output folders - check if they exist after script execution.
    /// </summary>
    [RelayCommand]
    private void RefreshOutputFolders()
    {
        foreach (var item in SourceFolders)
        {
            item.RefreshOutput();
        }
    }
    
    /// <summary>
    /// Mimics the 'get_relative_path_from_month' logic from Python scripts.
    /// Scans up the directory tree for a pattern like "02 AGUSTUS 2025".
    /// </summary>
    private string GetRelativePathFromMonth(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var parts = new System.Collections.Generic.List<string>();
            var current = dir;
            bool monthFound = false;

            // Regex for "DD MONTH YYYY" (e.g., 02 AGUSTUS 2025)
            // Python: ^\d{2}\s+\w+\s+\d{4}$
            var regex = new System.Text.RegularExpressions.Regex(@"^\d{2}\s+\w+\s+\d{4}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Walk up to find the month folder
            // We walk up at most 5 levels to avoid infinite loops or going too far
            var temp = dir;
            for (int i = 0; i < 5; i++)
            {
                if (temp == null) break;
                if (regex.IsMatch(temp.Name))
                {
                    monthFound = true;
                    // Found the root.
                    // Now we need the relative path from this Month folder (inclusive) down to 'path'
                     // Actually, the script includes the month folder itself in the structure.
                     // The logic is: OutputBase + Month/Sub/Target
                     break;
                }
                temp = temp.Parent;
            }

            if (monthFound && temp != null)
            {
                // Calculate relative path from temp (Month Folder) to dir (Source)
                // We want: MonthFolder\Sub\Source
                return Path.GetRelativePath(temp.Parent!.FullName, path);
            }
        }
        catch { }

        // Fallback: Just use the folder name (or mimic script parent fallback if strict)
        // Script fallback: os.path.basename(os.path.dirname(pilihan_path)) -> strict parent name?
        // Let's stick to FolderName for safety to avoid dumping into root.
        return Path.GetFileName(path);
    }

    /// <summary>
    /// Clear all folders and return to drop zone state.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        SourceFolders.Clear();
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(ShowDropZone));
    }

    /// <summary>
    /// Remove a specific folder item.
    /// </summary>
    [RelayCommand]
    private void RemoveFolder(BatchFolderRoot item)
    {
        if (SourceFolders.Contains(item))
        {
            SourceFolders.Remove(item);
            OnPropertyChanged(nameof(HasFolders));
            OnPropertyChanged(nameof(ShowDropZone));
        }
    }

    /// <summary>
    /// Get the first output path for script execution.
    /// </summary>
    public string? GetFirstOutputPath() => SourceFolders.FirstOrDefault()?.OutputPath;

    // --- SCRIPT EXECUTION CONTROLS ---
    
    // Event to request file/folder picker from View
    public event Func<Task<string?>>? RequestMasterPathBrowse;

    /// <summary>
    /// The selected Master Template Folder path (e.g. D:\MASTER).
    /// </summary>
    [ObservableProperty]
    private string _masterTemplatePath = "";

    [RelayCommand]
    private async Task BrowseMaster()
    {
        if (RequestMasterPathBrowse != null)
        {
            var path = await RequestMasterPathBrowse.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                MasterTemplatePath = path;
                // Save to DB (Last Used Template Path)
                if (_database != null)
                {
                    await _database.SetAsync("Configs.Master.LastTemplatePath", path);
                }
            }
        }
    }
    
    public class BatchScriptOption
    {
        public string Name { get; set; } = "";
        public string OriginalName { get; set; } = ""; // Original Filename
        public string Path { get; set; } = "";
        public Avalonia.Media.StreamGeometry? IconGeometry { get; set; }
    }

    [ObservableProperty]
    private ObservableCollection<BatchScriptOption> _masterScriptOptions = new();

    // Redundant but useful for ItemsControl binding specifically (if separate sorting needed later)
    public ObservableCollection<BatchScriptOption> MasterScriptOrderList => MasterScriptOptions;

    [ObservableProperty]
    private ObservableCollection<BatchScriptOption> _actionScriptOptions = new();

    // Store selected PATH
    [ObservableProperty]
    private string _selectedMasterScript = "";

    [ObservableProperty]
    private string _selectedActionScript = "";
    
    // Helper objects for AutoCompleteBox binding
    [ObservableProperty]
    private BatchScriptOption? _selectedMasterOption;

    [ObservableProperty]
    private BatchScriptOption? _selectedActionOption;

    partial void OnSelectedMasterOptionChanged(BatchScriptOption? value)
    {
        if (value != null) SelectedMasterScript = value.Path;
    }

    partial void OnSelectedActionOptionChanged(BatchScriptOption? value)
    {
        if (value != null) SelectedActionScript = value.Path;
    }

    private Dictionary<string, ScriptConfig> _scriptAliases = new();
    private string _metadataPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");

    private void LoadMetadata()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);
                
                // Load Dictionary
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    _scriptAliases = new(); 
                }
            }
        }
        catch 
        { 
            _scriptAliases = new(); 
        }
    }

    private void LoadScripts()
    {
        try
        {
            LoadMetadata();
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
            
            // Override with Custom Path if set
            if (!string.IsNullOrEmpty(CustomScriptsPath) && Directory.Exists(CustomScriptsPath))
            {
                baseDir = CustomScriptsPath;
            }
            
            // 1. Master Scripts
            var masterDir = Path.Combine(baseDir, "Master");
            if (Directory.Exists(masterDir))
            {
                var pyFiles = Directory.GetFiles(masterDir, "*.*")
                    .Where(f => f.EndsWith(".py"));
                
                var list = new List<(BatchScriptOption Option, int Order)>();
                foreach(var f in pyFiles)
                {
                    var fname = Path.GetFileName(f);
                    string display = Path.GetFileNameWithoutExtension(fname);
                    int order = 9999;
                    Avalonia.Media.StreamGeometry? iconGeom = null;

                    if (_scriptAliases.ContainsKey(fname))
                    {
                        var config = _scriptAliases[fname];
                        display = config.Name;
                        order = config.Order;
                        
                        // Resolve Icon
                        if (!string.IsNullOrEmpty(config.IconKey))
                        {
                            if (Avalonia.Application.Current!.TryGetResource(config.IconKey, null, out var res) && res is Avalonia.Media.StreamGeometry g)
                            {
                                iconGeom = g;
                            }
                        }
                    }

                    // Fallback to generic icon if null
                    if (iconGeom == null)
                    {
                         // Try generic fallback based on name? Or just leave null to let View handle it
                         // View handles null with Failover generic icon
                    }

                    list.Add((new BatchScriptOption 
                    { 
                        Name = display, 
                        OriginalName = fname,
                        Path = f, 
                        IconGeometry = iconGeom 
                    }, order));
                }
                
                // Sort: Order, then Alphabetical
                var sortedList = list.OrderBy(x => x.Order).ThenBy(x => x.Option.Name).Select(x => x.Option).ToList();

                MasterScriptOptions = new ObservableCollection<BatchScriptOption>(sortedList);
                OnPropertyChanged(nameof(MasterScriptOrderList));
                
                // Set initial selection logic
                if (MasterScriptOptions.Count > 0)
                {
                    // Try to restore previous selection or default
                    var toSelect = MasterScriptOptions.FirstOrDefault(x => x.Path == SelectedMasterScript) ?? MasterScriptOptions[0];
                    SelectedMasterOption = toSelect;
                }
            }

            // 2. Action Scripts (JSX + PYW)
            // (Similar logic if we were keeping Actions, but UI removed them. Keeping Logic won't hurt)
             var actionList = new List<(BatchScriptOption Option, int Order)>();
            
            // A. Load JSX from Action folder
            var actionDir = Path.Combine(baseDir, "Action");
            if (Directory.Exists(actionDir))
            {
                var jsxFiles = Directory.GetFiles(actionDir, "*.jsx");
                foreach(var f in jsxFiles)
                {
                    var fname = Path.GetFileName(f);
                    string display = Path.GetFileNameWithoutExtension(fname);
                    int order = 9999;

                    if (_scriptAliases.ContainsKey(fname))
                    {
                        var config = _scriptAliases[fname];
                        display = config.Name;
                        order = config.Order;
                    }
                    
                    actionList.Add((new BatchScriptOption { Name = display, Path = f }, order));
                }
            }
             // B. Load PYW from Root Scripts folder
            var pywFiles = Directory.GetFiles(baseDir, "*.pyw");
            foreach (var f in pywFiles)
            {
                var fname = Path.GetFileName(f);
                string display = Path.GetFileNameWithoutExtension(fname);
                int order = 9999;

                if (_scriptAliases.ContainsKey(fname))
                {
                    var config = _scriptAliases[fname];
                    display = config.Name;
                    order = config.Order;
                }

                actionList.Add((new BatchScriptOption { Name = display, Path = f }, order));
            }
            // Return sorted list
            var sortedActionList = actionList.OrderBy(x => x.Order).ThenBy(x => x.Option.Name).Select(x => x.Option).ToList();

            ActionScriptOptions = new ObservableCollection<BatchScriptOption>(sortedActionList);
            
            if (ActionScriptOptions.Count > 0)
            {
                 var toSelect = ActionScriptOptions.FirstOrDefault(x => x.Path == SelectedActionScript) ?? ActionScriptOptions[0];
                 SelectedActionOption = toSelect;
            }
        }
        catch { }
    }

    /// <summary>
    /// Execute a specific Master script directly from the UI button.
    /// </summary>
    [RelayCommand]
    private async Task ExecuteSpecificMaster(BatchScriptOption option)
    {
        if (option == null) return;
        SelectedMasterScript = option.Path; // Set selection
        await ExecuteMaster(); // Execute standard logic
    }

    [RelayCommand]
    private async Task ExecuteMaster()
    {
        if (string.IsNullOrEmpty(SelectedMasterScript) || !HasFolders) return;
        
        IsProcessing = true;
        
        var scriptName = Path.GetFileName(SelectedMasterScript);
        _logService?.AddLog($"[INFO] Memulai Batch Master: {scriptName}");

        // Broadcast Start
        WeakReferenceMessenger.Default.Send(new ProcessStatusMessage(true, scriptName));

        try
        {
            // --- Determine Master Paths based on Script Type ---
            string masterPrimary = "";
            string masterSecondary = "";
            string okeBasePath = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? ""; // Fetch OKE BASE
            string userName = await _database.GetAsync<string>("User.Name") ?? "USER"; // Fetch User Name

            string lowerScript = scriptName.ToLower();

            if (_database != null)
            {
                if (lowerScript.Contains("wisuda"))
                {
                    // Wisuda: 10RP (Primary), 8R (Secondary)
                    masterPrimary = await _database.GetAsync<string>("Configs.Master.Wisuda10RP") ?? "";
                    masterSecondary = await _database.GetAsync<string>("Configs.Master.Wisuda8R") ?? "";
                }
                else if (lowerScript.Contains("manasik"))
                {
                    // Manasik: 10RP (Primary), 8R (Secondary)
                    masterPrimary = await _database.GetAsync<string>("Configs.Master.Manasik10RP") ?? "";
                    masterSecondary = await _database.GetAsync<string>("Configs.Master.Manasik8R") ?? "";
                }
                else if (lowerScript.Contains("profesi"))
                {
                     // Profesi: Profesi (Primary), Sporty (Secondary)
                     masterPrimary = await _database.GetAsync<string>("Configs.Master.Profesi") ?? "";
                     masterSecondary = await _database.GetAsync<string>("Configs.Master.Sporty") ?? "";
                }
                else if (lowerScript.Contains("pasfoto") || lowerScript.Contains("pas_foto"))
                {
                     // Pas Foto
                     masterPrimary = await _database.GetAsync<string>("Configs.Master.PasFoto") ?? "";
                }
                else
                {
                    // Default / Generic: Use manually Browsed Path if available
                    masterPrimary = MasterTemplatePath;
                }
            }
            
            // Fallback if DB lookup empty but Browse button used (Manual override or generic script)
            if (string.IsNullOrEmpty(masterPrimary) && !string.IsNullOrEmpty(MasterTemplatePath))
            {
                masterPrimary = MasterTemplatePath;
            }

            // Validation
            if (string.IsNullOrEmpty(masterPrimary))
            {
                _logService?.AddLog($"[ERROR] Master Template belum diset untuk '{scriptName}' di Settings > Paths, dan belum dipilih manual.");
                IsProcessing = false;
                return;
            }
            
            _logService?.AddLog($"[INFO] Master 1: {masterPrimary}");
            if (!string.IsNullOrEmpty(masterSecondary)) _logService?.AddLog($"[INFO] Master 2: {masterSecondary}");


            // ALL Root Folders in the list are processed (Implicit selection)
            var rootsToProcess = SourceFolders.ToList(); // All source folders
            
            if (rootsToProcess.Count == 0)
            {
                _logService?.AddLog("[WARNING] Tidak ada folder untuk diproses.");
                IsProcessing = false;
                WeakReferenceMessenger.Default.Send(new ProcessStatusMessage(false));
                return;
            }
            
            _logService?.AddLog($"[INFO] Memproses {rootsToProcess.Count} folder...");

            foreach (var item in rootsToProcess)
            {
                if (string.IsNullOrEmpty(item.SourcePath)) continue;

                _logService?.AddLog($"[INFO] Memproses: {item.DisplayName}...");
                
                // Construct Arguments
                // python batch_wrapper.py --target <script> --pilihan <source> --master <template> --master2 <template2> --output <output>
                
                // Helper to Trim Trailing Slash which causes issue with closing quote logic if passed raw
                // Although ArgumentList handles it, it's safer to be clean.
                string CleanPath(string p) => string.IsNullOrEmpty(p) ? "" : p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var args = new List<string>
                {
                    "Scripts/batch_wrapper.py",
                    "--target", scriptName,
                    "--pilihan", CleanPath(item.SourcePath), 
                    "--master", CleanPath(masterPrimary),
                    "--master2", CleanPath(masterSecondary),
                    "--output", CleanPath(OutputBasePath), // Pass Base Path only
                    "--okebase", CleanPath(okeBasePath)  // Pass OKE Base
                };

                // Pass User Name via Environment Variable
                var envVars = new Dictionary<string, string>
                {
                    { "BMACHINE_USER_NAME", userName }
                };

                await RunPythonProcess(args, envVars);
                
                // Refresh Output
                item.RefreshOutput();
            }
            
            _logService?.AddLog("[SUCCESS] Batch Selesai.");
        }
        catch (Exception ex)
        {
             _logService?.AddLog($"[ERROR] Batch Gagal: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            WeakReferenceMessenger.Default.Send(new ProcessStatusMessage(false));
        }
    }
    
    private async Task RunPythonProcess(List<string> args, Dictionary<string, string>? envVars = null)
    {
        try 
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            
            // Add Environment Variables
            if (envVars != null)
            {
                foreach (var kvp in envVars)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            // Use ArgumentList for robust argument passing (handles spaces and quotes automatically)
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // DO NOT set startInfo.Arguments if using ArgumentList

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            _currentProcess = process; // Capture reference for Kill
            
            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _logService?.AddLog(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _logService?.AddLog($"[ERROR] {e.Data}"); };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[ERROR] Failed to start python: {ex.Message}");
        }
    }
    
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isUseInputFolder = false;
    [ObservableProperty] private bool _isUseOutputFolder = false;

    [RelayCommand]
    private async Task ExecuteAction()
    {
        if (string.IsNullOrEmpty(SelectedActionScript)) return;

        IsProcessing = true;
        _logService?.AddLog($"[INFO] Menjalankan Action Script: {Path.GetFileName(SelectedActionScript)}");

        try
        {
            // 1. Write Context to Temp File (for script to read if supported)
            var context = new { 
                SourceFolders = SourceFolders.Select(x => new { x.SourcePath, x.OutputPath }).ToList(),
                OutputBasePath = OutputBasePath,
                UseInput = IsUseInputFolder,
                UseOutput = IsUseOutputFolder,
                MasterTemplatePath = MasterTemplatePath
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(context, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            _logService?.AddLog($"[DEBUG] Context Payload:\n{json}"); // DEBUG LOG
            
            var tempPath = Path.Combine(Path.GetTempPath(), "bmachine_context.json");
            await File.WriteAllTextAsync(tempPath, json);
            
            _logService?.AddLog($"[INFO] Context written to: {tempPath}");

            // 2. Run Photoshop
            // Assume Photoshop is in PATH or just use 'start' via Process
            // Command: photoshop.exe -r "path/to/script.jsx"
            
            var startInfo = new System.Diagnostics.ProcessStartInfo();

            if (SelectedActionScript.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase))
            {
                 // Run Python GUI Script direclty
                 startInfo.FileName = SelectedActionScript;
                 startInfo.UseShellExecute = true; // Let OS handle .pyw association (pythonw)
                 
                 _logService?.AddLog($"[INFO] Launching Python Script: {Path.GetFileName(SelectedActionScript)}");
            }
            else
            {
                // Default: Photoshop Action (.jsx)
                startInfo.FileName = "photoshop";
                startInfo.Arguments = $"-r \"{SelectedActionScript}\"";
                startInfo.UseShellExecute = true; // Find in PATH
                
                 _logService?.AddLog($"[INFO] Launching Photoshop...");
            }
            System.Diagnostics.Process.Start(startInfo);
            
            _logService?.AddLog("[SUCCESS] Script sent to Photoshop.");
        }
        catch (Exception ex)
        {
             _logService?.AddLog($"[ERROR] Action Launch Failed: {ex.Message}");
             _logService?.AddLog("[HINT] Pastikan Photoshop terinstall dan ada di SYSTEM PATH.");
        }
        finally
        {
            IsProcessing = false;
        }
    }
    
    /// <summary>
    /// Open dialog to browse for Master Template folder.
    /// </summary>
    [RelayCommand]
    private async Task BrowseMasterTemplate()
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow : null);
            
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Master Template Folder",
            AllowMultiple = false
        });

        if (folders.Count == 1)
        {
            MasterTemplatePath = folders[0].Path.LocalPath;
             // Save to DB
            if (_database != null)
                await _database.SetAsync("Configs.Master.LastTemplatePath", MasterTemplatePath);
                
            _logService?.AddLog($"[INFO] Master Template set: {MasterTemplatePath}");
        }
    }

    /// <summary>
    /// Load settings from database.
    /// </summary>
    private async Task LoadOutputBasePathAsync()
    {
        if (_database != null)
        {
            OutputBasePath = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";
            MasterTemplatePath = await _database.GetAsync<string>("Configs.Master.LastTemplatePath") ?? "";
            
            // Load custom scripts path
            var customScripts = await _database.GetAsync<string>("Configs.System.ScriptsPath");
            if (!string.IsNullOrEmpty(customScripts) && Directory.Exists(customScripts))
            {
                CustomScriptsPath = customScripts;
            }
        }
        LoadScripts();
    }
    
    [ObservableProperty]
    private string _customScriptsPath = "";
    
    [RelayCommand]
    private async Task BrowseScriptsFolder()
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow : null);
            
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Scripts Root Folder",
            AllowMultiple = false
        });

        if (folders.Count == 1)
        {
            CustomScriptsPath = folders[0].Path.LocalPath;
            if (_database != null)
                await _database.SetAsync("Configs.System.ScriptsPath", CustomScriptsPath);
            
            _logService?.AddLog($"[INFO] Scripts Path updated: {CustomScriptsPath}");
            LoadScripts(); // Reload immediately
        }
    }
}



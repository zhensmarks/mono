using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls; // Added
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using BMachine.SDK;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using BMachine.UI.Messages;

namespace BMachine.UI.ViewModels;

public partial class FloatingItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = ""; // Resource Key
    public string FilePath { get; set; } = "";
    public string Category { get; set; } = "Apps"; // Master, Action, Others
    public IRelayCommand? ActionCommand { get; set; }
}

public partial class FloatingWidgetViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    private readonly Services.IProcessLogService? _logService;
    private readonly Action<string> _launchAction;
    private DispatcherTimer _scanTimer;

    [ObservableProperty] private bool _isVisible = true; // Default
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _showMasterMenu;
    [ObservableProperty] private bool _showActionMenu;
    [ObservableProperty] private bool _showOthersMenu;
    
    [ObservableProperty] private ObservableCollection<FloatingItem> _masterItems = new();
    [ObservableProperty] private ObservableCollection<FloatingItem> _actionItems = new();

    [ObservableProperty] private ObservableCollection<FloatingItem> _otherItems = new();
    
    // UI Settings
    [ObservableProperty] private double _orbButtonWidth = 160;
    [ObservableProperty] private double _orbButtonHeight = 36;
    
    // Custom Accent Color
    [ObservableProperty] private Avalonia.Media.IBrush _accentColor = Avalonia.Media.Brushes.Blue; // Default Fallback

    public FloatingWidgetViewModel(IDatabase? database, Action<string> launchAction, Services.IProcessLogService? logService = null)
    {
        _database = database;
        _launchAction = launchAction;
        _logService = logService;
        
        // Initial Color Load
        _ = LoadAccentColor();
        
        // Subscribe to toggle message
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<FloatingWidgetMessage>(this, (r, m) =>
        {
             IsVisible = m.IsVisible;
        });
        
        // Subscribe to Color Change
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<OrbColorChangedMessage>(this, (r, m) =>
        {
             AccentColor = m.Value;
        });
        
        // Subscribe to Speed Change
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<OrbSpeedChangedMessage>(this, (r, m) =>
        {
             OrbSpeedIndex = m.Value;
        });
        
        // Subscribe to Breathing Toggle
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<OrbBreathingToggledMessage>(this, (r, m) =>
        {
             IsOrbBreathing = m.Value;
        });
        
        // Subscribe to Button Size Change
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<Messages.OrbButtonSizeChangedMessage>(this, (r, m) =>
        {
             OrbButtonWidth = m.Value.Width;
             OrbButtonHeight = m.Value.Height;
             // View subscribes to PropertyChanged of these, so it should rebuild automatically
        });
        
        _ = LoadOrbSpeed();
        _ = LoadOrbBreathing();
        _ = LoadButtonSize(); // Load initial size
        
        LoadMetadata();
        LoadScripts();
        LoadOthers();
        
        // Auto-refresh scripts every 5 seconds (optional)
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _scanTimer.Tick += (s, e) => { LoadMetadata(); LoadScripts(); LoadOthers(); };
        _scanTimer.Start();
        
        // Listen for Refresh Signal
        WeakReferenceMessenger.Default.Register<RefreshScriptsMessage>(this, (r, m) => 
        {
            LoadMetadata();
            LoadScripts();
            LoadOthers();
        });
    }

    [ObservableProperty] private int _orbSpeedIndex = 1;
    [ObservableProperty] private bool _isOrbBreathing = true;

    private async Task LoadOrbBreathing()
    {
        if (_database == null) return;
        try
        {
             var val = await _database.GetAsync<string>("Settings.Orb.Breathing");
             IsOrbBreathing = string.IsNullOrEmpty(val) || bool.Parse(val);
        }
        catch {}
    }

    [ObservableProperty] private double _orbExpandedWidth = 440; // Default
    [ObservableProperty] private double _orbExpandedHeight = 210; // Default

    private async Task LoadButtonSize()
    {
        if (_database == null) return;
        try
        {
            var wStr = await _database.GetAsync<string>("Settings.Orb.ButtonWidth");
            if (double.TryParse(wStr, out double w)) OrbButtonWidth = w;
            
            var hStr = await _database.GetAsync<string>("Settings.Orb.ButtonHeight");
            if (double.TryParse(hStr, out double h)) OrbButtonHeight = h;

            // Load Expanded Size
            var ewStr = await _database.GetAsync<string>("Settings.Orb.ExpandedWidth");
            if (double.TryParse(ewStr, out double ew)) OrbExpandedWidth = ew;
            
            var ehStr = await _database.GetAsync<string>("Settings.Orb.ExpandedHeight");
            if (double.TryParse(ehStr, out double eh)) OrbExpandedHeight = eh;
        }
        catch {}
    }

    public async Task SaveExpandedSize(double width, double height)
    {
        if (_database == null) return;
        OrbExpandedWidth = width;
        OrbExpandedHeight = height;
        try
        {
            await _database.SetAsync("Settings.Orb.ExpandedWidth", width.ToString());
            await _database.SetAsync("Settings.Orb.ExpandedHeight", height.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FloatingWidgetVM] Error saving expanded size: {ex.Message}");
        }
    }

    private async Task LoadOrbSpeed()
    {
        if (_database == null) return;
        try
        {
            var speedStr = await _database.GetAsync<string>("Settings.Orb.Speed");
            if (int.TryParse(speedStr, out int speed))
            {
                OrbSpeedIndex = speed;
            }
        }
        catch {}
    }

    private async Task LoadAccentColor()
    {
        if (_database == null) return;
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var hex = await _database.GetAsync<string>("Settings.Color.Orb");
                if (!string.IsNullOrEmpty(hex) && Avalonia.Media.Color.TryParse(hex, out var color))
                {
                     Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                     {
                         AccentColor = new Avalonia.Media.SolidColorBrush(color);
                     });
                     return; // Success
                }
                else
                {
                     // Found nothing? Keep default?
                     // Verify if key exists. If not, default is fine.
                     return;
                }
            }
            catch 
            {
                retries--;
                await Task.Delay(500); // Wait bit
            }
        }
    }

    // Metadata for Aliases and Priority
    private Dictionary<string, string> _scriptAliases = new();
    private List<string> _scriptPriorityList = new(); // Stores order of keys in JSON
    private string _metadataPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");

    [ObservableProperty] private string _customScriptsPath = "";

    private void LoadMetadata()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);
                
                // 1. Load Dictionary for Display Names
                _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

                // 2. Load Keys List for Order Priority
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                _scriptPriorityList = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            }
        }
        catch 
        { 
            _scriptAliases = new(); 
            _scriptPriorityList = new();
        }
    }
    
    private async void LoadScripts()
    {
        // Ensure settings are loaded first (specifically path)
        if (_database != null)
        {
             var customScripts = await _database.GetAsync<string>("Configs.System.ScriptsPath");
             if (!string.IsNullOrEmpty(customScripts) && Directory.Exists(customScripts))
             {
                 CustomScriptsPath = customScripts;
             }
        }

        LoadMetadata();

        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        // Override with Custom Path if set
        if (!string.IsNullOrEmpty(CustomScriptsPath) && Directory.Exists(CustomScriptsPath))
        {
            baseDir = CustomScriptsPath;
        }

        // 1. Master/Python (.py only) - Matches BatchViewModel
        var masterPath = Path.Combine(baseDir, "Master");
        IEnumerable<string> pyFiles = new List<string>();

        if (Directory.Exists(masterPath))
        {
             pyFiles = Directory.GetFiles(masterPath, "*.*")
                .Where(f => f.EndsWith(".py")); // Removed .pyw from Master
        }

        UpdateCollection(MasterItems, pyFiles, "IconPython", "Master");

        // 2. Action/JSX (.jsx) AND .pyw (Root Scripts) - Matches BatchViewModel
        var actionFilesList = new List<string>();
        
        // A. Load JSX from Action folder
        var actionPath = Path.Combine(baseDir, "Action");
        if (Directory.Exists(actionPath))
        {
            actionFilesList.AddRange(Directory.GetFiles(actionPath, "*.jsx"));
        }

        // B. Load PYW from Root Scripts folder
        if (Directory.Exists(baseDir))
        {
             actionFilesList.AddRange(Directory.GetFiles(baseDir, "*.pyw"));
        }

        UpdateCollection(ActionItems, actionFilesList, "IconBolt", "Action");
    }
    
    private void UpdateCollection(ObservableCollection<FloatingItem> collection, IEnumerable<string> files, string icon, string cat)
    {
        var currentFiles = files.ToList();
        
        // Remove deleted files
        var toRemove = collection.Where(i => !currentFiles.Contains(i.FilePath)).ToList();
        foreach(var r in toRemove) collection.Remove(r);
        
        // Add new files
        foreach(var file in currentFiles)
        {
            var fileName = Path.GetFileName(file);
            var displayName = _scriptAliases.ContainsKey(fileName) ? _scriptAliases[fileName] : Path.GetFileNameWithoutExtension(fileName);
            
            var existing = collection.FirstOrDefault(i => i.FilePath == file);
            if (existing != null)
            {
                if (existing.Name != displayName) existing.Name = displayName;
            }
            else
            {
                collection.Add(new FloatingItem 
                { 
                    Name = displayName,
                    FilePath = file,
                    Icon = icon,
                    Category = cat,
                    ActionCommand = new RelayCommand(() => ExecuteFile(file))
                });
            }
        }
        
        // Apply Priority Sort (Matches Batch View)
        ApplyPrioritySort(collection);
    }
    
    private void ApplyPrioritySort(ObservableCollection<FloatingItem> collection)
    {
        // Sort collection based on _scriptPriorityList (filenames) then Alphabetical
        // Matches BatchViewModel logic:
        // var index = _scriptPriorityList.IndexOf(key);
        // return index == -1 ? int.MaxValue : index;
            
        var sorted = collection.OrderBy(item => 
        {
            var key = Path.GetFileName(item.FilePath);
            var idx = _scriptPriorityList.IndexOf(key);
            return idx == -1 ? int.MaxValue : idx;
        }).ThenBy(item => item.Name).ToList();
            
        // Re-populate if order changed
        for(int i=0; i<sorted.Count; i++)
        {
            int oldIdx = collection.IndexOf(sorted[i]);
            if (oldIdx != i) collection.Move(oldIdx, i);
        }
    }
    
    public void ReorderItem(FloatingItem source, FloatingItem target, bool insertAfter)
    {
        // determine collection from category
        var collection = GetCollectionByCategory(source.Category);
        if (collection == null || !collection.Contains(target)) return;
        
        int oldIndex = collection.IndexOf(source);
        int newIndex = collection.IndexOf(target);
        
        if (insertAfter)
        {
            if (newIndex < collection.Count - 1) newIndex++; // Insert after target
        }
        else
        {
             // Insert before target, newIndex is correct
        }
        
        // Adjust index if moving down
        if (oldIndex < newIndex && insertAfter) newIndex--; 
        
        // Safety clamps
        if (newIndex < 0) newIndex = 0;
        if (newIndex >= collection.Count) newIndex = collection.Count - 1;
        
        if (oldIndex == newIndex) return;

        collection.Move(oldIndex, newIndex);
        
        // Save Global Order
        _ = SavePriorityToMetadata();
    }
    
    private ObservableCollection<FloatingItem>? GetCollectionByCategory(string cat)
    {
        if (cat == "Master") return MasterItems;
        if (cat == "Action") return ActionItems;
        if (cat == "Others") return OtherItems;
        return null;
    }
    
    private async Task SavePriorityToMetadata()
    {
        try 
        {
            // Reconstruct the Dictionary in the new order
            // Priority: Master Items -> Action Items -> Others (hidden ones preserved)
            var newOrder = new Dictionary<string, string>();
            
            // 1. Master Scripts
            foreach (var item in MasterItems)
            {
                var fname = Path.GetFileName(item.FilePath);
                // Use existing alias if known, otherwise DisplayName
                var alias = _scriptAliases.ContainsKey(fname) ? _scriptAliases[fname] : item.Name;
                newOrder[fname] = alias;
            }
            
            // 2. Action Scripts
            foreach (var item in ActionItems)
            {
                var fname = Path.GetFileName(item.FilePath);
                var alias = _scriptAliases.ContainsKey(fname) ? _scriptAliases[fname] : item.Name;
                newOrder[fname] = alias;
            }
            
            // 3. Preserve any other scripts in the file (hidden/unloaded) that are not yet added
            foreach (var kvp in _scriptAliases)
            {
                if (!newOrder.ContainsKey(kvp.Key))
                {
                    newOrder[kvp.Key] = kvp.Value;
                }
            }
            
            // Update internal cache
            _scriptAliases = newOrder;
            _scriptPriorityList = newOrder.Keys.ToList();
            
            // Save to JSON
            // Using JsonSerializer checks insertion order for properties mostly, 
            // but for Dictionary it's implementation dependent. 
            // However, reconstructing a new Dict usually preserves insertion order in simple serialization.
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(newOrder, options);
            
            await File.WriteAllTextAsync(_metadataPath, json);
            
            // Notify Batch View to reload
            WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.ScriptOrderChangedMessage());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving scripts metadata: {ex.Message}");
        }
    }
    
    private Views.PixelcutWindow? _pixelcutWindow;
    private Views.GdriveWindow? _gdriveWindow;

    private void LoadOthers()
    {
        // 3. Others (Any Files)
        var othersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Others");
        if (!Directory.Exists(othersPath)) Directory.CreateDirectory(othersPath);
        
        var files = Directory.GetFiles(othersPath);
        UpdateCollection(OtherItems, files, "IconExtension", "Others");



        // Manually Add GDrive Extension if not present
        var gdriveItem = OtherItems.FirstOrDefault(i => i.FilePath == "INTERNAL_GDRIVE_EXTENSION");
        
        var gdriveDisplayName = _scriptAliases.ContainsKey("INTERNAL_GDRIVE_EXTENSION") 
            ? _scriptAliases["INTERNAL_GDRIVE_EXTENSION"] 
            : "Upload GDrive";

        if (gdriveItem == null)
        {
            OtherItems.Add(new FloatingItem 
            { 
                Name = gdriveDisplayName,
                FilePath = "INTERNAL_GDRIVE_EXTENSION",
                Icon = "IconExtension",
                Category = "Others",
                ActionCommand = new RelayCommand(() => ExecuteFile("INTERNAL_GDRIVE_EXTENSION"))
            });
        }
        else
        {
            if (gdriveItem.Name != gdriveDisplayName) gdriveItem.Name = gdriveDisplayName;
        }
    }

    private async void ExecuteFile(string path)
    {
        var fileName = Path.GetFileName(path).ToLower();
        IsExpanded = false; // Close after click
        CloseSubMenus();

        try 
        {
            if (path == "INTERNAL_GDRIVE_EXTENSION")
            {
                if (_gdriveWindow == null || !_gdriveWindow.IsVisible)
                {
                    _gdriveWindow = new Views.GdriveWindow();
                    _gdriveWindow.DataContext = new GdriveViewModel(_database);
                    _gdriveWindow.Closed += (s, e) => _gdriveWindow = null;
                    _gdriveWindow.Show();
                }
                else
                {
                    _gdriveWindow.Activate();
                }
            }
            else if (path.EndsWith(".jsx"))
            {
                await RunJsxScript(path);
            }
            else if (fileName == "profesi_flat.py")
            {
                await RunProfesi(path);
            }
            else if (fileName.Contains("manasik") && fileName.EndsWith(".py")) // Matches manasik (1).py
            {
                await RunManasik(path);
            }
            else if (fileName == "pasfoto.py")
            {
                await RunPasFoto(path);
            }
            else if (fileName == "wisuda.py")
            {
                await RunWisuda(path);
            }
            else
            {
                // Default Execution
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }

        catch(Exception ex) 
        {
            Console.WriteLine($"Error running {path}: {ex.Message}");
            _logService?.AddLog($"Error running {Path.GetFileName(path)}: {ex.Message}");
        }
    }
    
    private async Task RunJsxScript(string scriptPath)
    {
        if (_database == null) return;
        
        var photoshopPath = await _database.GetAsync<string>("Configs.Master.PhotoshopPath");
        if (string.IsNullOrEmpty(photoshopPath) || !File.Exists(photoshopPath))
        {
             _logService?.AddLog("[ERROR] Photoshop path not set or invalid. Please Config in Settings.");
             Process.Start("explorer", "/select,\"" + scriptPath + "\""); // Fallback: Show file
             return;
        }
        
        _logService?.AddLog($"Running JSX in Photoshop: {Path.GetFileName(scriptPath)}");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = photoshopPath,
            Arguments = $"-r \"{scriptPath}\"",
            UseShellExecute = false
        };
        
        try
        {
             Process.Start(startInfo);
        }
        catch(Exception ex)
        {
             _logService?.AddLog($"[ERROR] Failed to launch Photoshop: {ex.Message}");
        }
    }

    private async Task<(string? Input, string? Output)> AskForInputOutputFolders(string titleSuffix)
    {
        var window = GetActiveWindow();
        if (window == null) 
        {
             _logService?.AddLog("[ERROR] No active window found for dialogs.");
             return (null, null);
        }

        // 1. Input Folder
        _logService?.AddLog($"[DEBUG] Asking for INPUT folder for {titleSuffix}...");
        var inputResult = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Select INPUT Folder for {titleSuffix}",
            AllowMultiple = false
        });
        
        if (inputResult == null || inputResult.Count == 0) 
        {
             _logService?.AddLog("[DEBUG] Input folder selection cancelled.");
             return (null, null);
        }
        var inputPath = inputResult[0].Path.LocalPath;
        _logService?.AddLog($"[DEBUG] Input selected: {inputPath}");

        // 2. Output Folder
        // Check if Default Local Output is set
        string defaultOutput = "";
        if (_database != null)
        {
             defaultOutput = await _database.GetAsync<string>("Configs.Master.LocalOutput") ?? "";
        }
        
        string outputPath;
        if (!string.IsNullOrEmpty(defaultOutput) && Directory.Exists(defaultOutput))
        {
             // Use Default
             outputPath = defaultOutput;
             _logService?.AddLog($"[DEBUG] Using Default Output: {outputPath}");
        }
        else
        {
            // Ask User
            _logService?.AddLog($"[DEBUG] Asking for OUTPUT folder...");
            var outputResult = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select OUTPUT Folder for {titleSuffix}",
                AllowMultiple = false,
                SuggestedStartLocation = inputResult[0] // Suggest same folder
            });
            if (outputResult == null || outputResult.Count == 0) 
            {
                _logService?.AddLog("[DEBUG] Output folder selection cancelled.");
                return (null, null);
            }
            outputPath = outputResult[0].Path.LocalPath;
            _logService?.AddLog($"[DEBUG] Output selected: {outputPath}");
        }

        return (inputPath, outputPath);
    }

    private async Task RunProfesi(string scriptPath)
    {
        if (_database == null) return;
        
        // 1. Get Master Paths
        var masterProfesi = await _database.GetAsync<string>("Configs.Master.Profesi");
        var masterSporty = await _database.GetAsync<string>("Configs.Master.Sporty") ?? "";

        if (string.IsNullOrEmpty(masterProfesi))
        {
            _logService?.AddLog("[ERROR] Master Profesi path is not set in Settings! Please configure it.");
            return;
        }

        // 2. Ask User
        var folders = await AskForInputOutputFolders("Profesi");
        if (folders.Input == null || folders.Output == null) 
        {
            _logService?.AddLog("[INFO] Folder selection cancelled.");
            return;
        }

        // 3. Run
        var okeBase = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? "";
        var userName = await _database.GetAsync<string>("UserProfile.Name") ?? "USER";
        // Args: <master_profesi> <master_sporty> <pilihan> <output> [oke_base]
        var args = new List<string> { scriptPath, masterProfesi, masterSporty, folders.Input, folders.Output, okeBase };
        RunPythonProcess(args, userName, Path.GetDirectoryName(scriptPath));
    }

    private async Task RunManasik(string scriptPath)
    {
        if (_database == null) return;

        // 1. Get Master Path (Use 10RP or 8R, script finds parent)
        var masterPath = await _database.GetAsync<string>("Configs.Master.Manasik10RP");
        if (string.IsNullOrEmpty(masterPath))
        {
             masterPath = await _database.GetAsync<string>("Configs.Master.Manasik8R");
        }

        if (string.IsNullOrEmpty(masterPath))
        {
            _logService?.AddLog("[ERROR] Master Manasik path not set in Settings.");
            return;
        }

        // 2. Ask User
        var folders = await AskForInputOutputFolders("Manasik");
        if (folders.Input == null || folders.Output == null) 
        {
             _logService?.AddLog("[INFO] Folder selection cancelled.");
             return;
        }

        // 3. Run
        var okeBase = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? "";
        var userName = await _database.GetAsync<string>("UserProfile.Name") ?? "USER";
        // Args: <master_path> <pilihan> <output> [oke_base]
         var args = new List<string> { scriptPath, masterPath, folders.Input, folders.Output, okeBase };
        RunPythonProcess(args, userName, Path.GetDirectoryName(scriptPath));
    }
    
    private async Task RunPasFoto(string scriptPath)
    {
        if (_database == null) return;
        
        var masterPath = await _database.GetAsync<string>("Configs.Master.PasFoto");
        if (string.IsNullOrEmpty(masterPath))
        {
            _logService?.AddLog("[ERROR] Master PasFoto path not set.");
            return;
        }
        
        var folders = await AskForInputOutputFolders("Pas Foto");
        if (folders.Input == null || folders.Output == null) 
        {
             _logService?.AddLog("[INFO] Folder selection cancelled.");
             return;
        }

        // Args: <master_path> <pilihan> <output> <oke_base>
        var okeBase = await _database.GetAsync<string>("Configs.Master.OkeBase");
        var userName = await _database.GetAsync<string>("UserProfile.Name") ?? "USER";
        var finalOke = string.IsNullOrEmpty(okeBase) ? "NONE" : okeBase;
        var args = new List<string> { scriptPath, masterPath, folders.Input, folders.Output, finalOke };
        RunPythonProcess(args, userName, Path.GetDirectoryName(scriptPath));


    }

    private async Task RunWisuda(string scriptPath)
    {
        if (_database == null) return;

        // 1. Get Master Path
        var masterPath = await _database.GetAsync<string>("Configs.Master.Wisuda10RP");
        if (string.IsNullOrEmpty(masterPath))
        {
             masterPath = await _database.GetAsync<string>("Configs.Master.Wisuda8R");
        }

        if (string.IsNullOrEmpty(masterPath))
        {
            Console.WriteLine("[Error] Master Wisuda path not set in Settings.");
            return;
        }

        // 2. Ask User
        var folders = await AskForInputOutputFolders("Wisuda");
        if (folders.Input == null || folders.Output == null) return;

        // 3. Run
        var okeBase = await _database.GetAsync<string>("Configs.Master.OkeBase") ?? "";
        var userName = await _database.GetAsync<string>("UserProfile.Name") ?? "USER";
        var args = new List<string> { scriptPath, masterPath, folders.Input, folders.Output, okeBase };
        RunPythonProcess(args, userName, Path.GetDirectoryName(scriptPath));
    }

    private void RunPythonProcess(IEnumerable<string> argsList, string userName = "USER", string? workingDir = null)
    {
        _logService?.AddLog($"[DEBUG] RunPythonProcess called. User: {userName}");
        
        // Robust Working Directory Logic
        string wd = workingDir;
        if (string.IsNullOrEmpty(wd))
        {
             try 
             {
                 var firstArg = argsList.FirstOrDefault();
                 if (!string.IsNullOrEmpty(firstArg)) wd = Path.GetDirectoryName(firstArg);
             } 
             catch { wd = ""; }
        }
        _logService?.AddLog($"[DEBUG] Working Directory: {wd}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "python", 
            // Arguments property is NOT used. ArgumentList is populated manually.
            UseShellExecute = false, // Must be false for Env Vars & Redirection
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = wd ?? ""
        };

        foreach(var arg in argsList) startInfo.ArgumentList.Add(arg);

        // Set Environment Variable explicitly

        // Set Environment Variable explicitly
        startInfo.EnvironmentVariables["BMACHINE_USER_NAME"] = userName;
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8"; // Ensure UTF-8 output
        
        Task.Run(() =>
        {
            try
            {
                // Unset loading state just in case, then Set
                WeakReferenceMessenger.Default.Send(new ProcessStatusMessage(true, "Python Script"));
                
                using var process = new Process();
                process.StartInfo = startInfo;
                
                process.OutputDataReceived += (s, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data)) _logService?.AddLog(e.Data);
                };
                process.ErrorDataReceived += (s, e) => 
                {
                     if (!string.IsNullOrEmpty(e.Data)) _logService?.AddLog($"[ERROR] {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                process.WaitForExit();
                _logService?.AddLog($"Process exited with code {process.ExitCode}");
            }
            catch (System.ComponentModel.Win32Exception w32ex)
            {
                _logService?.AddLog($"[CRITICAL] Could not find 'python'. Is Python installed and added to PATH? Details: {w32ex.Message}");
            }
            catch (Exception ex)
            {
                _logService?.AddLog($"[EXCEPTION] Failed to start process: {ex.Message}");
            }
            finally
            {
                WeakReferenceMessenger.Default.Send(new ProcessStatusMessage(false));
            }
        });
    }

    private Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Try to get active window, or main window, or first window (FloatingWidget itself)
            var active = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow ?? desktop.Windows.FirstOrDefault();
            return active;
        }
        return null;
    }

    private string _lastSelectedMenu = "Master"; // Default to Master

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded)
        {
            // Restore last selected menu
            CloseSubMenus();
            switch (_lastSelectedMenu)
            {
                case "Master": ShowMasterMenu = true; break;
                case "Action": ShowActionMenu = true; break;
                case "Others": ShowOthersMenu = true; break;
                default: ShowMasterMenu = true; break;
            }
        }
        else 
        {
            CloseSubMenus();
        }
    }
    
    [RelayCommand]
    private void ToggleMaster()
    {
        bool wasOpen = ShowMasterMenu;
        CloseSubMenus();
        if (!wasOpen) 
        {
            ShowMasterMenu = true;
            _lastSelectedMenu = "Master";
        }
    }
    
    [RelayCommand]
    private void ToggleAction()
    {
        bool wasOpen = ShowActionMenu;
        CloseSubMenus();
        if (!wasOpen) 
        {
            ShowActionMenu = true;
            _lastSelectedMenu = "Action";
        }
    }
    
    [RelayCommand]
    private void ToggleOthers()
    {
        bool wasOpen = ShowOthersMenu;
        CloseSubMenus();
        if (!wasOpen) 
        {
            ShowOthersMenu = true;
            _lastSelectedMenu = "Others";
        }
    }
    
    private void CloseSubMenus()
    {
        ShowMasterMenu = false;
        ShowActionMenu = false;
        ShowOthersMenu = false;
    }

    [RelayCommand]
    private void GoHome()
    {
        _launchAction?.Invoke("Home");
        IsExpanded = false;
        CloseSubMenus();
    }

    public async Task SavePosition(int x, int y)
    {
        if (_database == null) return;
        await _database.SetAsync("Configs.Widget.Position.X", x.ToString());
        await _database.SetAsync("Configs.Widget.Position.Y", y.ToString());
    }

    public async Task<(int X, int Y)?> GetSavedPosition()
    {
        if (_database == null) return null;
        var xStr = await _database.GetAsync<string>("Configs.Widget.Position.X");
        var yStr = await _database.GetAsync<string>("Configs.Widget.Position.Y");

        if (int.TryParse(xStr, out int x) && int.TryParse(yStr, out int y))
        {
            return (x, y);
        }
        return null;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
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
    public partial class BatchViewModel : ObservableObject, IRecipient<FolderDeletedMessage>, IRecipient<OpenMasterBrowserMessage>, IRecipient<MasterPathsChangedMessage>
    {
        private readonly IDatabase? _database;
        private readonly Services.IProcessLogService? _logService;
        private readonly BMachine.Core.Platform.IPlatformService _platformService;

        /// <summary>
        /// Collection of source folder items dropped by the user.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFolders))]
        [NotifyPropertyChangedFor(nameof(ShowDropZone))]
        private ObservableCollection<BatchFolderRoot> _sourceFolders = new();
        
        // Store current process to kill it later
        private Process? _currentProcess;
    
        public BatchViewModel(IDatabase? database, Services.IProcessLogService? logService, BMachine.Core.Platform.IPlatformService? platformService = null)
        {
            _database = database;
            _logService = logService;
            _platformService = platformService ?? BMachine.Core.Platform.PlatformServiceFactory.Get();
            
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

            // Register for Folder Deleted Message
            WeakReferenceMessenger.Default.Register<FolderDeletedMessage>(this);
    
            _ = LoadOutputBasePathAsync();
            
            // Ensure HasFolders updates when items are added/removed
            SourceFolders.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasFolders));
                OnPropertyChanged(nameof(ShowDropZone));
            };
    


            // Register Master Browser Messages
            WeakReferenceMessenger.Default.Register<OpenMasterBrowserMessage>(this);
            WeakReferenceMessenger.Default.Register<MasterPathsChangedMessage>(this);
        }

        public void Receive(OpenMasterBrowserMessage message)
        {
            OpenMasterBrowser(message.TargetNode, message.Side);
        }

        public void Receive(MasterPathsChangedMessage message)
        {
            // Reload master files if browser is open, or just clear cache
            if (IsMasterBrowserOpenLeft || IsMasterBrowserOpenRight)
            {
                _ = LoadMasterNodes();
            }
        }

        public void Receive(FolderDeletedMessage message)
        {
            // Simple approach: Refresh all roots to reflect changes
            // Since we deleted a folder, the tree needs to be rebuilt or the item removed.
            // If the deleted item was a Root, we remove it.
            // If it was a child, Refreshing the root should clear it.
            
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
            {
                // Check if the deleted item is one of the roots
                var rootMatch = SourceFolders.FirstOrDefault(x => x.SourcePath == message.Value.FullPath);
                if (rootMatch != null)
                {
                    SourceFolders.Remove(rootMatch);
                }
                else
                {
                    // It's a subfolder, so we refresh all roots
                    Refresh();
                }
            });
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
    /// <summary>
    /// Refresh source and output folders.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        foreach (var item in SourceFolders)
        {
            item.RefreshSource();
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

    // --- MASTER FILE BROWSER LOGIC ---

    [ObservableProperty] private bool _isMasterBrowserOpenLeft;
    [ObservableProperty] private bool _isMasterBrowserOpenRight;
    [ObservableProperty] private string _masterBrowserTargetName = "";
    [ObservableProperty] private string _masterBrowserTargetPath = "";
    [ObservableProperty] private string _masterSearchText = "";
    [ObservableProperty] private ObservableCollection<MasterNode> _masterNodes = new(); // Changed to MasterNode
    
    // Copy Feedback
    [ObservableProperty] private string _copyStatusText = "";
    [ObservableProperty] private bool _isCopying;

    private BatchNodeItem? _currentTargetNode;

    partial void OnMasterSearchTextChanged(string value)
    {
        ApplyMasterFilter(value);
    }

    private void ApplyMasterFilter(string filter)
    {
        // Simple reload logic for now to handle search filtering
        // Ideally we filter the existing tree visually, but reloading is safer for correctness
        _ = LoadMasterNodes(filter);
    }

    private async Task LoadMasterNodes(string filter = "")
    {
        MasterNodes.Clear();

        var paths = new List<string>();
        
        // 1. Get Additional Paths from Settings
        if (_database != null)
        {
            var json = await _database.GetAsync<string>("Configs.Master.AdditionalPaths");
            if (!string.IsNullOrEmpty(json))
            {
                try {
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                    if (loaded != null) paths.AddRange(loaded);
                } catch {}
            }
        }

        // 2. Recursive Scan
        var nodes = await Task.Run(() => 
        {
            var result = new List<MasterNode>();
            
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var rootNode = ScanDirectory(path, filter);
                    if (rootNode != null)
                    {
                        // Expand only if filtering
                        if (!string.IsNullOrEmpty(filter)) rootNode.IsExpanded = true;
                        result.Add(rootNode);
                    }
                }
            }
            return result;
        });

        foreach (var node in nodes) MasterNodes.Add(node);
    }

    // Recursive Scanner
    private MasterNode? ScanDirectory(string path, string filter)
    {
        var node = new MasterNode(path, true);
        bool hasContent = false;
        
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true };
            
            // Subdirectories
            foreach (var d in Directory.EnumerateDirectories(path, "*", opts))
            {
                var subNode = ScanDirectory(d, filter);
                if (subNode != null)
                {
                    node.Children.Add(subNode);
                    hasContent = true;
                }
            }
            
            // Files (.psd, .psb)
            var files = Directory.EnumerateFiles(path, "*.*", opts)
                .Where(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
                
            foreach (var f in files)
            {
                var fName = Path.GetFileName(f);
                
                // Filter Logic:
                // If filter is present, check filename OR if parent folder matched (implicit from recursive call structure?)
                // Actually, if we want to filter, we need to know if this file matches.
                
                bool matches = string.IsNullOrEmpty(filter) || fName.Contains(filter, StringComparison.OrdinalIgnoreCase);
                
                if (matches)
                {
                    node.Children.Add(new MasterNode(f, false));
                    hasContent = true;
                }
            }
        }
        catch {}

        // If filtering, only return node if it has content (files or subfolders with content)
        if (!string.IsNullOrEmpty(filter) && !hasContent) return null;
        
        // If not filtering, we might want to return empty folders? 
        // Requirement said: "only include folder that contains .psd/.psb (or subfolder with it)"
        // So yes, strictly check hasContent.
        if (!hasContent) return null;
        
        // Sort
        // node.SortChildren(); // Can sort if needed, ObservableCollection doesn't sort automatically
        
        return node;
    }

    private void OpenMasterBrowser(BatchNodeItem target, string side)
    {
        _currentTargetNode = target;
        MasterBrowserTargetName = target.Name;
        MasterBrowserTargetPath = target.FullPath; 
        
        // If node is a file, use parent dir
        if (!target.IsDirectory)
        {
             MasterBrowserTargetPath = Path.GetDirectoryName(target.FullPath) ?? "";
        }

        _ = LoadMasterNodes(); // Load data

        if (side == "Left")
        {
            IsMasterBrowserOpenLeft = true;
            IsMasterBrowserOpenRight = false; // Close other
        }
        else
        {
            IsMasterBrowserOpenRight = true;
            IsMasterBrowserOpenLeft = false;
        }
    }

    [RelayCommand]
    private void CloseMasterBrowser()
    {
        IsMasterBrowserOpenLeft = false;
        IsMasterBrowserOpenRight = false;
        _currentTargetNode = null;
        MasterSearchText = "";
    }

    [RelayCommand]
    private async Task CopyMasterFile(MasterNode item)
    {
        if (item.IsDirectory) return; // Can't copy folder directly yet

        if (string.IsNullOrEmpty(MasterBrowserTargetPath) || !Directory.Exists(MasterBrowserTargetPath))
        {
            _logService?.AddLog("[ERROR] Target folder not found.");
            return;
        }

        IsCopying = true;
        
        // Log Start
        if (_logService != null)
        {
             await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
             {
                 _logService.AddLog($"[INFO] Copying {item.Name}...");
             });
        }

        var source = item.FullPath;
        var dest = Path.Combine(MasterBrowserTargetPath, item.Name);

        // Auto Rename Logic if Exists
        if (File.Exists(dest))
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(dest);
            string ext = Path.GetExtension(dest);
            int count = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(MasterBrowserTargetPath, $"{nameNoExt} ({count}){ext}");
                count++;
            }
        }

        try
        {
             // Copy in background
             await Task.Run(() => File.Copy(source, dest));
             
             // Refresh Target Node Children
             if (_currentTargetNode != null)
             {
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                 {
                     _currentTargetNode.LoadChildren(); 
                 });
             }
             
             // Log Success
             if (_logService != null)
             {
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                 {
                     _logService.AddLog($"[INFO] Copied: {Path.GetFileName(dest)}");
                 });
            }
        }
        catch (Exception ex)
        {
            if (_logService != null)
            {
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                 {
                     _logService.AddLog($"[ERROR] Copy failed: {ex.Message}");
                 });
            }
        }
        finally
        {
            IsCopying = false;
        }
    }

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
            var scriptPath = "python"; // Default fallback if needed, but the service handles it.
            // Wait, the interface expects scriptPath separated from args?
            // The existing `args` list contains "Scripts/batch_wrapper.py" as the first element!
            // See line 912: "Scripts/batch_wrapper.py"
            
            string actualScript = "";
            var actualArgs = new List<string>();
            
            if (args.Count > 0)
            {
                actualScript = args[0];
                if (args.Count > 1) actualArgs = args.Skip(1).ToList();
            }
            
            // Resolve script path to absolute if it's relative?
            // "Scripts/batch_wrapper.py" implies relative to BaseDirectory.
            if (!Path.IsPathRooted(actualScript))
            {
                actualScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, actualScript);
            }

            await _platformService.RunPythonScriptAsync(
                actualScript, 
                actualArgs, 
                envVars,
                onOutput: (data) => _logService?.AddLog(data),
                onError: (data) => _logService?.AddLog($"[ERROR] {data}")
            );
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
            
            if (SelectedActionScript.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase))
            {
                 // Run Python GUI Script direclty
                 _logService?.AddLog($"[INFO] Launching Python Script: {Path.GetFileName(SelectedActionScript)}");
                 _platformService.RunPythonScript(SelectedActionScript, true);
            }
            else
            {
                // Default: Photoshop Action (.jsx)
                 _logService?.AddLog($"[INFO] Launching Photoshop...");
                 // Need Photoshop path? 
                 // The old code assumed "photoshop" in PATH.
                 // We can lookup or just try "photoshop" if we implement fuzzy search in Service?
                 // Or we can use RunJsxInPhotoshop which requires a path.
                 // Let's get the path from DB first as Best Practice.
                 var photoshopPath = await _database.GetAsync<string>("Configs.Master.PhotoshopPath") ?? "photoshop";
                 
                 _platformService.RunJsxInPhotoshop(SelectedActionScript, photoshopPath);
            }
            
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

    // --- QUICK ACTIONS ---
    [ObservableProperty]
    private string _newFolderName = "";

    [RelayCommand]
    private async Task CreateFolder(string targetType)
    {
        if (string.IsNullOrWhiteSpace(NewFolderName)) return;
        
        string? targetPath = null;
        BatchFolderRoot? itemToRefresh = null;

        // Determine Target Path
        if (targetType.Equals("Source", StringComparison.OrdinalIgnoreCase))
        {
            var root = SourceFolders.FirstOrDefault();
            if (root != null) 
            {
                targetPath = root.SourcePath;
                itemToRefresh = root;
            }
        }
        else if (targetType.Equals("Output", StringComparison.OrdinalIgnoreCase))
        {
             // For Output, we create in the Output Base Path + Relative Path of first item?
             // Or just in the Output Base Path directly if no folder structure?
             // Let's assume user wants to create folder in the Output directory of the first item
             // OR in the main OutputBasePath if it's set.
             
             // Strategy: Try to use the first item's OutputPath
             var root = SourceFolders.FirstOrDefault();
             if (root != null && !string.IsNullOrEmpty(root.OutputPath))
             {
                 targetPath = root.OutputPath;
                 itemToRefresh = root;
             }
             else if (!string.IsNullOrEmpty(OutputBasePath))
             {
                 targetPath = OutputBasePath;
             }
        }

        if (string.IsNullOrEmpty(targetPath)) 
        {
            _logService?.AddLog("[WARNING] Cannot create folder: Target path not found.");
            return;
        }
        
        try
        {
            var newPath = Path.Combine(targetPath, NewFolderName);
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                
                // Show success (log)
                _logService?.AddLog($"[INFO] Folder Created: {newPath}");
                
                // Refresh
                if (itemToRefresh != null)
                {
                    itemToRefresh.RefreshSource();
                    itemToRefresh.RefreshOutput();
                }
                else
                {
                    Refresh();
                }
                
                // Clear input
                NewFolderName = "";
            }
            else
            {
                 _logService?.AddLog($"[WARNING] Folder already exists: {NewFolderName}");
            }
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[ERROR] Failed to create folder: {ex.Message}");
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



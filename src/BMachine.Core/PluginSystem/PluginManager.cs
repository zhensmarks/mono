using System.Reflection;
using BMachine.SDK;

namespace BMachine.Core.PluginSystem;

/// <summary>
/// Manager untuk loading, unloading, dan mengelola plugins
/// </summary>
public class PluginManager
{
    private readonly string _pluginsPath;
    private readonly Dictionary<string, LoadedPlugin> _plugins = new();
    
    // Services needed for context creation
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IDatabase _database;
    private readonly IActivityService _activity;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notification;
    private readonly IServiceProvider _services;
    
    public IReadOnlyDictionary<string, LoadedPlugin> Plugins => _plugins;
    
    public event EventHandler<PluginEventArgs>? PluginLoaded;
    public event EventHandler<PluginEventArgs>? PluginUnloaded;
    
    public PluginManager(
        string pluginsPath,
        IEventBus eventBus,
        ILogger logger,
        IDatabase database,
        IActivityService activity,
        INavigationService navigation,
        INotificationService notification,
        IServiceProvider services)
    {
        _pluginsPath = pluginsPath;
        _eventBus = eventBus;
        _logger = logger;
        _database = database;
        _activity = activity;
        _navigation = navigation;
        _notification = notification;
        _services = services;
        
        if (!Directory.Exists(_pluginsPath))
            Directory.CreateDirectory(_pluginsPath);
    }
    
    /// <summary>
    /// Load semua plugin dari folder plugins
    /// </summary>
    public async Task LoadAllPluginsAsync()
    {
        var pluginDirs = Directory.GetDirectories(_pluginsPath);
        
        foreach (var dir in pluginDirs)
        {
            try
            {
                await LoadPluginFromDirectoryAsync(dir);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load plugin from {dir}", ex);
            }
        }
        
        // Also load DLLs directly in plugins folder
        var dllFiles = Directory.GetFiles(_pluginsPath, "*.dll");
        foreach (var dll in dllFiles)
        {
            try
            {
                await LoadPluginFromDllAsync(dll);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load plugin from {dll}", ex);
            }
        }
    }
    
    /// <summary>
    /// Load plugin dari direktori (dengan plugin.json)
    /// </summary>
    public async Task<IPlugin?> LoadPluginFromDirectoryAsync(string directory)
    {
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            _logger.Warning($"No plugin.json found in {directory}");
            return null;
        }
        
        var manifest = await LoadManifestAsync(manifestPath);
        if (manifest == null) return null;
        
        var dllPath = Path.Combine(directory, manifest.EntryPoint);
        return await LoadPluginFromDllAsync(dllPath, manifest);
    }
    
    /// <summary>
    /// Load plugin dari DLL file
    /// </summary>
    public async Task<IPlugin?> LoadPluginFromDllAsync(string dllPath, PluginManifest? manifest = null)
    {
        if (!File.Exists(dllPath))
        {
            _logger.Error($"Plugin DLL not found: {dllPath}");
            return null;
        }
        
        // Suppress async warning for now
        await Task.CompletedTask;
        
        var assembly = Assembly.LoadFrom(dllPath);
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);
        
        foreach (var type in pluginTypes)
        {
            var plugin = Activator.CreateInstance(type) as IPlugin;
            if (plugin == null) continue;
            
            if (_plugins.ContainsKey(plugin.Id))
            {
                _logger.Warning($"Plugin {plugin.Id} already loaded, skipping");
                continue;
            }
            
            var context = CreatePluginContext(plugin.Id);
            plugin.Initialize(context);
            
            var loaded = new LoadedPlugin
            {
                Plugin = plugin,
                Assembly = assembly,
                Manifest = manifest,
                LoadedAt = DateTime.Now
            };
            
            _plugins[plugin.Id] = loaded;
            PluginLoaded?.Invoke(this, new PluginEventArgs(plugin));
            
            _logger.Info($"Loaded plugin: {plugin.Name} v{plugin.Version}");
            return plugin;
        }
        
        return null;
    }
    
    /// <summary>
    /// Unload plugin by ID
    /// </summary>
    public void UnloadPlugin(string pluginId)
    {
        if (!_plugins.TryGetValue(pluginId, out var loaded))
        {
            _logger.Warning($"Plugin {pluginId} not found");
            return;
        }
        
        loaded.Plugin.Shutdown();
        _plugins.Remove(pluginId);
        PluginUnloaded?.Invoke(this, new PluginEventArgs(loaded.Plugin));
        
        _logger.Info($"Unloaded plugin: {loaded.Plugin.Name}");
    }
    
    /// <summary>
    /// Get semua widgets dari semua plugins
    /// </summary>
    public IEnumerable<IWidget> GetAllWidgets()
    {
        return _plugins.Values
            .SelectMany(p => p.Plugin.GetDashboardWidgets());
    }
    
    /// <summary>
    /// Get semua menu entries dari semua plugins
    /// </summary>
    public IEnumerable<IMenuEntry> GetAllMenuEntries()
    {
        return _plugins.Values
            .SelectMany(p => p.Plugin.GetMenuEntries())
            .OrderBy(m => m.Order);
    }
    
    private async Task<PluginManifest?> LoadManifestAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(json);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load manifest: {path}", ex);
            return null;
        }
    }

    private IPluginContext CreatePluginContext(string pluginId)
    {
        return new PluginContext(
            pluginId,
            _eventBus,
            _logger,
            _database,
            _activity,
            _navigation,
            _notification,
            _services
        );
    }
}

/// <summary>
/// Container untuk loaded plugin
/// </summary>
public class LoadedPlugin
{
    public required IPlugin Plugin { get; init; }
    public required Assembly Assembly { get; init; }
    public PluginManifest? Manifest { get; init; }
    public DateTime LoadedAt { get; init; }
}

/// <summary>
/// Plugin manifest (plugin.json)
/// </summary>
public class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Icon { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public string[] Permissions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Event args untuk plugin events
/// </summary>
public class PluginEventArgs : EventArgs
{
    public IPlugin Plugin { get; }
    
    public PluginEventArgs(IPlugin plugin)
    {
        Plugin = plugin;
    }
}

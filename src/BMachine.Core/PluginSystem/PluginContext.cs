using BMachine.SDK;

namespace BMachine.Core.PluginSystem;

/// <summary>
/// Implementation of IPluginContext untuk di-inject ke plugins
/// </summary>
public class PluginContext : IPluginContext
{
    private readonly string _pluginId;
    private readonly IServiceProvider _services;
    
    public IEventBus EventBus { get; }
    public ILogger Logger { get; }
    public IDatabase Database { get; }
    public IActivityService Activity { get; }
    public INavigationService Navigation { get; }
    public INotificationService Notification { get; }
    
    public PluginContext(
        string pluginId,
        IEventBus eventBus,
        ILogger logger,
        IDatabase database,
        IActivityService activity,
        INavigationService navigation,
        INotificationService notification,
        IServiceProvider services)
    {
        _pluginId = pluginId;
        _services = services;
        EventBus = eventBus;
        Logger = new PluginLogger(pluginId, logger);
        Database = new ScopedDatabase(pluginId, database);
        Activity = activity;
        Navigation = navigation;
        Notification = notification;
    }
    
    public T? GetSetting<T>(string key, T? defaultValue = default)
    {
        // Implementation will use database
        return defaultValue;
    }
    
    public void SetSetting<T>(string key, T value)
    {
        // Implementation will use database
    }
}

/// <summary>
/// Logger wrapper yang menambahkan prefix plugin ID
/// </summary>
internal class PluginLogger : ILogger
{
    private readonly string _prefix;
    private readonly ILogger _inner;
    
    public PluginLogger(string pluginId, ILogger inner)
    {
        _prefix = $"[{pluginId}]";
        _inner = inner;
    }
    
    public void Debug(string message) => _inner.Debug($"{_prefix} {message}");
    public void Info(string message) => _inner.Info($"{_prefix} {message}");
    public void Warning(string message) => _inner.Warning($"{_prefix} {message}");
    public void Error(string message, Exception? ex = null) => _inner.Error($"{_prefix} {message}", ex);
}

/// <summary>
/// Database wrapper yang scope ke plugin tertentu
/// </summary>
internal class ScopedDatabase : IDatabase
{
    private readonly string _scope;
    private readonly IDatabase _inner;
    
    public ScopedDatabase(string pluginId, IDatabase inner)
    {
        _scope = $"plugin:{pluginId}:";
        _inner = inner;
    }
    
    private string ScopedKey(string key) => $"{_scope}{key}";
    
    public Task<T?> GetAsync<T>(string key) where T : class 
        => _inner.GetAsync<T>(ScopedKey(key));
    
    public Task SetAsync<T>(string key, T value) where T : class 
        => _inner.SetAsync(ScopedKey(key), value);
    
    public Task DeleteAsync(string key) 
        => _inner.DeleteAsync(ScopedKey(key));
    
    public Task<IEnumerable<T>> QueryAsync<T>(Func<T, bool> predicate) where T : class 
        => _inner.QueryAsync(predicate);

    public string DatabasePath => _inner.DatabasePath;
}

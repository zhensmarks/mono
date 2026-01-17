namespace BMachine.SDK;

/// <summary>
/// Base class untuk membuat plugin dengan boilerplate minimal
/// </summary>
public abstract class BasePlugin : IPlugin
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual string Description => "";
    public virtual string Version => "1.0.0";
    public virtual string Icon => "puzzle";
    
    protected IPluginContext? Context { get; private set; }
    protected ILogger? Logger => Context?.Logger;
    protected IEventBus? EventBus => Context?.EventBus;
    protected IDatabase? Database => Context?.Database;
    
    public virtual void Initialize(IPluginContext context)
    {
        Context = context;
        Logger?.Info($"Plugin {Name} v{Version} initialized");
        OnInitialize();
    }
    
    public virtual void Shutdown()
    {
        OnShutdown();
        Logger?.Info($"Plugin {Name} shutdown");
        Context = null;
    }
    
    /// <summary>Override untuk custom initialization logic</summary>
    protected virtual void OnInitialize() { }
    
    /// <summary>Override untuk custom shutdown logic</summary>
    protected virtual void OnShutdown() { }
    
    public virtual IEnumerable<IWidget> GetDashboardWidgets() 
        => Enumerable.Empty<IWidget>();
    
    public virtual IEnumerable<IMenuEntry> GetMenuEntries() 
        => Enumerable.Empty<IMenuEntry>();
}

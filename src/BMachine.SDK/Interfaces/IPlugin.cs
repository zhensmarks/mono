namespace BMachine.SDK;

/// <summary>
/// Interface utama untuk plugin BMachine
/// </summary>
public interface IPlugin
{
    /// <summary>Unique identifier plugin</summary>
    string Id { get; }
    
    /// <summary>Nama display plugin</summary>
    string Name { get; }
    
    /// <summary>Deskripsi singkat</summary>
    string Description { get; }
    
    /// <summary>Versi plugin (semver)</summary>
    string Version { get; }
    
    /// <summary>Icon path atau glyph name</summary>
    string Icon { get; }
    
    /// <summary>Dipanggil saat plugin dimuat</summary>
    void Initialize(IPluginContext context);
    
    /// <summary>Dipanggil saat plugin di-unload</summary>
    void Shutdown();
    
    /// <summary>Widgets untuk dashboard (opsional)</summary>
    IEnumerable<IWidget> GetDashboardWidgets() => Enumerable.Empty<IWidget>();
    
    /// <summary>Menu entries (opsional)</summary>
    IEnumerable<IMenuEntry> GetMenuEntries() => Enumerable.Empty<IMenuEntry>();
}

/// <summary>
/// Context yang disediakan ke plugin untuk akses services
/// </summary>
public interface IPluginContext
{
    /// <summary>Event bus untuk komunikasi antar plugin</summary>
    IEventBus EventBus { get; }
    
    /// <summary>Logger untuk plugin</summary>
    ILogger Logger { get; }
    
    /// <summary>Akses ke database</summary>
    IDatabase Database { get; }
    
    /// <summary>Service untuk navigasi</summary>
    INavigationService Navigation { get; }
    
    /// <summary>Service untuk notifikasi</summary>
    INotificationService Notification { get; }
    
    /// <summary>Mendapatkan setting plugin</summary>
    T? GetSetting<T>(string key, T? defaultValue = default);
    
    /// <summary>Menyimpan setting plugin</summary>
    void SetSetting<T>(string key, T value);
}

/// <summary>
/// Widget yang bisa ditampilkan di dashboard
/// </summary>
public interface IWidget
{
    string Id { get; }
    string Title { get; }
    WidgetSize Size { get; }
    object CreateView();
}

/// <summary>
/// Entry menu untuk navigation
/// </summary>
public interface IMenuEntry
{
    string Id { get; }
    string Title { get; }
    string Icon { get; }
    int Order { get; }
    Action OnClick { get; }
}

/// <summary>
/// Ukuran widget
/// </summary>
public enum WidgetSize
{
    Small,      // 1x1
    Medium,     // 2x1
    Large,      // 2x2
    Wide        // 4x1
}

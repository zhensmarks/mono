namespace BMachine.SDK;

/// <summary>
/// Event bus untuk komunikasi antar plugin dan core app
/// </summary>
public interface IEventBus
{
    /// <summary>Publish event ke semua subscriber</summary>
    void Publish<T>(T eventData) where T : class, IEvent;
    
    /// <summary>Subscribe ke event type tertentu</summary>
    IDisposable Subscribe<T>(Action<T> handler) where T : class, IEvent;
}

/// <summary>
/// Base interface untuk semua events
/// </summary>
public interface IEvent
{
    DateTime Timestamp { get; }
    string Source { get; }
}

/// <summary>
/// Logger interface untuk plugin
/// </summary>
public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>
/// Database abstraction untuk plugin
/// </summary>
public interface IDatabase
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value) where T : class;
    Task DeleteAsync(string key);
    Task<IEnumerable<T>> QueryAsync<T>(Func<T, bool> predicate) where T : class;
    string DatabasePath { get; }
}

/// <summary>
/// Navigation service untuk perpindahan view
/// </summary>
public interface INavigationService
{
    void NavigateTo(string viewId, object? parameter = null);
    void GoBack();
    bool CanGoBack { get; }
}

/// <summary>
/// Notification service untuk UI feedback
/// </summary>
public interface INotificationService
{
    void ShowInfo(string message, string? title = null);
    void ShowSuccess(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);
    Task<bool> ShowConfirmAsync(string message, string? title = null);
}

/// <summary>
/// Service untuk mencatat dan mengambil data aktivitas user/sistem
/// </summary>
public interface IActivityService
{
    Task LogAsync(string type, string title, string description);
    Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 10);
    Task<int> GetCountAsync(string type);
    Task ClearAsync();
}

public class ActivityLog
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

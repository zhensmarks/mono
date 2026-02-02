using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMachine.SDK;
using BMachine.Core.Database;
using BMachine.UI.ViewModels;
using BMachine.UI.Services;
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.Services;

public class AppLogger : ILogger
{
    private readonly ProcessLogService _logService;

    public AppLogger(ProcessLogService logService)
    {
        _logService = logService;
    }

    public void Debug(string message)
    {
        // Debug logs might be too noisy for the main log, so maybe just Console?
        System.Diagnostics.Debug.WriteLine($"[PLUGIN-DEBUG] {message}");
    }

    public void Info(string message)
    {
        _logService.AddLog($"[PLUGIN] {message}");
    }

    public void Warning(string message)
    {
        _logService.AddLog($"[PLUGIN-WARN] {message}");
    }

    public void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message}: {ex.Message}" : message;
        _logService.AddLog($"[PLUGIN-ERROR] {msg}");
    }
}

public class AppDatabase : IDatabase
{
    private readonly DatabaseService _db;

    public AppDatabase(DatabaseService db)
    {
        _db = db;
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        // SDK expects reference types for T? (class constraint)
        // DatabaseService might support primitives.
        // We might need to handle casting carefully or assume T is string for now mostly.
        return await _db.GetAsync<T>(key);
    }

    public async Task SetAsync<T>(string key, T value) where T : class
    {
        await _db.SetAsync(key, value);
    }

    public async Task DeleteAsync(string key)
    {
        await _db.DeleteAsync(key);
    }

    public Task<IEnumerable<T>> QueryAsync<T>(Func<T, bool> predicate) where T : class
    {
        // Not implemented in core yet?
        return Task.FromResult<IEnumerable<T>>(new List<T>());
    }
}

public class AppEventBus : IEventBus
{
    // Simple wrapper using Messenger or just valid internal even handler
    // For now, simple implementation
    public void Publish<T>(T eventData) where T : class, IEvent
    {
        // Broadcaster
        WeakReferenceMessenger.Default.Send(eventData);
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : class, IEvent
    {
        // No-op for now (or implement register)
        return new DisposableAction(() => { });
    }

    private class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}

public class AppActivity : IActivityService
{
    public Task LogAsync(string type, string title, string description) => Task.CompletedTask;
    public Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 10) => Task.FromResult<IEnumerable<ActivityLog>>(new List<ActivityLog>());
    public Task<int> GetCountAsync(string type) => Task.FromResult(0);
    public Task ClearAsync() => Task.CompletedTask;
}

public class AppNavigation : INavigationService
{
    public bool CanGoBack => false;
    public void NavigateTo(string viewId, object? parameter = null) { }
    public void GoBack() { }
}

public class AppNotification : INotificationService
{
    public void ShowInfo(string message, string? title = null) { }
    public void ShowSuccess(string message, string? title = null) { }
    public void ShowWarning(string message, string? title = null) { }
    public void ShowError(string message, string? title = null) { }
    public Task<bool> ShowConfirmAsync(string message, string? title = null) => Task.FromResult(true);
}

public class AppServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<T>(T service) where T : notnull
    {
        _services[typeof(T)] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}

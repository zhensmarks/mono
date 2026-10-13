using System.Collections.Concurrent;
using BMachine.Core.Platform;
using BMachine.SDK;
using BMachine.UI.Services;

namespace BMachine.UI.RegressionTests;

/// <summary>In-memory IDatabase fake (thread-safe, mirrors real key/value semantics).</summary>
public sealed class FakeDatabase : IDatabase
{
    private readonly ConcurrentDictionary<string, object> _store = new();

    public string DatabasePath => ":memory:";

    public Task<T?> GetAsync<T>(string key) where T : class
        => Task.FromResult(_store.TryGetValue(key, out var v) && v is T typed ? typed : null);

    public Task SetAsync<T>(string key, T value) where T : class
    {
        if (value is null) _store.TryRemove(key, out _);
        else _store[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<T>> QueryAsync<T>(Func<T, bool> predicate) where T : class
        => Task.FromResult(_store.Values.OfType<T>().Where(predicate));

    // Helper for assertions
    public bool Has(string key) => _store.ContainsKey(key);
}

/// <summary>Records calls instead of touching the real OS.</summary>
public sealed class FakePlatformService : IPlatformService
{
    public ConcurrentQueue<string> RevealedFiles { get; } = new();
    public ConcurrentQueue<string> OpenedFolders { get; } = new();
    public ConcurrentQueue<string> OpenedWithDefault { get; } = new();
    public ConcurrentQueue<string> RecycleBinCalls { get; } = new();
    public bool RecycleBinResult { get; set; } = true;

    public void RevealFileInExplorer(string filePath) => RevealedFiles.Enqueue(filePath);
    public void OpenFolder(string folderPath) => OpenedFolders.Enqueue(folderPath);
    public void OpenDateTimeSettings() { }
    public void OpenUrl(string url) { }
    public IEnumerable<string> GetPhotoshopSearchPaths() => Enumerable.Empty<string>();
    public void RunPythonScript(string scriptPath, bool gui) { }
    public Task RunPythonScriptAsync(string scriptPath, IEnumerable<string> args,
        Dictionary<string, string>? envVars = null, Action<string>? onOutput = null,
        Action<string>? onError = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public void RunJsxInPhotoshop(string jsxPath, string photoshopPath) { }
    public string GetAppDataDirectory() => Path.Combine(Path.GetTempPath(), "bmachine-test-appdata");
    public void OpenWithDefaultApp(string fileOrFolderPath) => OpenedWithDefault.Enqueue(fileOrFolderPath);
    public bool MoveToRecycleBin(string fileOrFolderPath)
    {
        RecycleBinCalls.Enqueue(fileOrFolderPath);
        if (RecycleBinResult && File.Exists(fileOrFolderPath))
            try { File.Delete(fileOrFolderPath); } catch { }
        else if (RecycleBinResult && Directory.Exists(fileOrFolderPath))
            try { Directory.Delete(fileOrFolderPath, true); } catch { }
        return RecycleBinResult;
    }
    public void OpenWithDialog(string filePath) { }
    public string SilencePhotoshopWarnings(string photoshopPath) => photoshopPath;
    public bool IsExecutableValid(string path) => false;
}

/// <summary>Swallows notifications so tests never show popups and can assert on them.</summary>
public sealed class FakeNotificationService : INotificationService
{
    public ConcurrentQueue<string> Errors { get; } = new();
    public ConcurrentQueue<string> Successes { get; } = new();
    public ConcurrentQueue<string> Infos { get; } = new();

    public void ShowInfo(string message, string? title = null) => Infos.Enqueue(message);
    public void ShowSuccess(string message, string? title = null) => Successes.Enqueue(message);
    public void ShowWarning(string message, string? title = null) { }
    public void ShowError(string message, string? title = null) => Errors.Enqueue(message);

    /// <summary>What the next (and every) ShowConfirmAsync call answers. Default: confirmed.</summary>
    public bool ConfirmResult { get; set; } = true;

    public ConcurrentQueue<string> Confirms { get; } = new();

    public Task<bool> ShowConfirmAsync(string message, string? title = null)
    {
        Confirms.Enqueue(message);
        return Task.FromResult(ConfirmResult);
    }
}

/// <summary>In-memory clipboard so Copy/Cut/Paste paths can be tested without a desktop clipboard.</summary>
public sealed class FakeExplorerClipboard : IExplorerClipboard
{
    private List<string>? _paths;
    private string? _text;

    public Task<bool> SetFilePathsAsync(IReadOnlyList<string> paths)
    {
        _paths = paths.ToList();
        _text = string.Join(Environment.NewLine, paths);
        return Task.FromResult(true);
    }

    public Task<bool> SetTextAsync(string text)
    {
        _text = text;
        _paths = null;
        return Task.FromResult(true);
    }

    public Task<string?> GetTextAsync() => Task.FromResult(_text);

    public Task<IReadOnlyList<string>?> GetFilePathsAsync()
        => Task.FromResult<IReadOnlyList<string>?>(_paths?.ToList());

    public Task<bool> HasFilePayloadAsync() => Task.FromResult(_paths is { Count: > 0 });

    public Task ClearAsync()
    {
        _paths = null;
        _text = null;
        return Task.CompletedTask;
    }
}

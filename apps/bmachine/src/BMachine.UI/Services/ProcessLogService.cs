using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BMachine.UI.Services;

public interface IProcessLogService
{
    ObservableCollection<string> Logs { get; }
    void AddLog(string log);
    void Clear();
    void Flush();
}

/// <summary>
/// Log service dengan throttle: buffer AddLog() dari background thread,
/// flush ke ObservableCollection (UI) maksimal setiap 100ms.
/// Ini menghilangkan lag: ratusan Dispatcher.UIThread.Post per detik →
/// satu Post per 100ms yang flush batch sekaligus.
/// </summary>
public class ProcessLogService : IProcessLogService
{
    private readonly ObservableCollection<string> _logs = new();
    public ObservableCollection<string> Logs => _logs;

    private readonly ConcurrentQueue<string> _buffer = new();
    private readonly System.Threading.Timer _timer;
    private volatile bool _flushPending;

    private const int MaxLogs = 1000;
    private const int FlushIntervalMs = 100; // flush ke UI maks 10x/detik

    public ProcessLogService()
    {
        _timer = new System.Threading.Timer(_ => FlushBuffer(), null,
            FlushIntervalMs, FlushIntervalMs);
    }

    public void AddLog(string log)
    {
        _buffer.Enqueue(log);
        // Jika belum ada flush pending, jadwalkan satu (hindari spam Post)
        if (!_flushPending)
        {
            _flushPending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushOnUiThread,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    public void Flush()
    {
        FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (_buffer.IsEmpty) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(FlushOnUiThread,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private void FlushOnUiThread()
    {
        _flushPending = false;
        // Drain semua yang ada di buffer sekarang
        var batch = new System.Collections.Generic.List<string>(64);
        while (_buffer.TryDequeue(out var line))
            batch.Add(line);

        if (batch.Count == 0) return;

        foreach (var line in batch)
            _logs.Add(line);

        // Trim dari atas bila melebihi MaxLogs
        while (_logs.Count > MaxLogs)
            _logs.RemoveAt(0);
    }

    public void Clear()
    {
        // Buang buffer pending juga
        while (_buffer.TryDequeue(out _)) { }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _logs.Clear(),
            Avalonia.Threading.DispatcherPriority.Background);
    }
}

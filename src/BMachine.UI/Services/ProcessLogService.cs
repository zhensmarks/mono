using System.Collections.ObjectModel;

namespace BMachine.UI.Services;

public interface IProcessLogService
{
    ObservableCollection<string> Logs { get; }
    void AddLog(string log);
    void Clear();
}

public class ProcessLogService : IProcessLogService
{
    private readonly ObservableCollection<string> _logs = new();
    public ObservableCollection<string> Logs => _logs;

    public void AddLog(string log)
    {
        // Must ensure UI thread if bound directly, but usually ObservableCollection requires UI thread dispatch for updates
        // However, we'll handle thread safety in ViewModel or simple Dispatcher if needed.
        // For simplicity here:
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _logs.Add(log);
            // Optional: Limit log size?
            if (_logs.Count > 1000) _logs.RemoveAt(0);
        });
    }

    public void Clear()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _logs.Clear());
    }
}

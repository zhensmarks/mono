using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.ViewModels;

public partial class SplashViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "Memuat...";
    [ObservableProperty] private double _progress = 0;
    
    // For Terminal-style scrolling logs
    public ObservableCollection<string> TerminalLogs { get; } = new();

    public void AddLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TerminalLogs.Add($"> {message}");
            if (TerminalLogs.Count > 3) 
            {
                TerminalLogs.RemoveAt(0); // Keep only the latest 3 logs
            }
        });
    }
}

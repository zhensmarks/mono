using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.UI.Services;

namespace BMachine.UI.ViewModels;

/// <summary>
/// Data context for the shared "File Operations" monitor window.
/// It wraps the process-wide <see cref="FileOperationManager"/> directly instead of borrowing
/// whichever <see cref="OutputExplorerViewModel"/> happened to open the window first —
/// that old behaviour left the window bound to a stale, possibly closed tab.
/// </summary>
public partial class FileOperationsMonitorViewModel : ObservableObject
{
    private readonly FileOperationManager _manager;

    public FileOperationsMonitorViewModel(FileOperationManager manager)
    {
        _manager = manager;
    }

    public ObservableCollection<FileTaskItem> ActiveTasks => _manager.ActiveTasks;

    [RelayCommand]
    private void CancelTask(FileTaskItem? task)
    {
        if (task != null) _manager.CancelTask(task);
    }

    [RelayCommand]
    private void RemoveTask(FileTaskItem? task)
    {
        if (task != null) _manager.RemoveTask(task);
    }

    [RelayCommand]
    private void RetryTask(FileTaskItem? task)
    {
        if (task != null) _manager.RetryTask(task);
    }

    [RelayCommand]
    private void ClearCompleted() => _manager.ClearCompletedTasks();
}

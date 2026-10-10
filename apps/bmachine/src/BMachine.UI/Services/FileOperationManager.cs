using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Concurrent;
using System.Linq;
using Avalonia.Threading;

namespace BMachine.UI.Services;

public enum FileTaskType
{
    Copy,
    Move,
    Delete
}

public partial class FileTaskItem : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public FileTaskType Type { get; set; }
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";

    public bool IsCopy => Type == FileTaskType.Copy;
    public bool IsMove => Type == FileTaskType.Move;
    public bool IsDelete => Type == FileTaskType.Delete;

    /// <summary>Badge text shown in the monitor: COPY / MOVE / DELETE.</summary>
    public string TypeLabel => Type switch
    {
        FileTaskType.Copy => "COPY",
        FileTaskType.Move => "MOVE",
        FileTaskType.Delete => "DELETE",
        _ => ""
    };

    /// <summary>Trimmed destination path for "To: ...".</summary>
    public string DestinationDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(DestinationPath)) return "";
            try { var d = Path.GetDirectoryName(DestinationPath); if (string.IsNullOrEmpty(d)) return DestinationPath; return d.Length > 50 ? "..." + d.Substring(d.Length - 47) : d; }
            catch { return ""; }
        }
    }

    /// <summary>Short display path for "From: ..." (e.g. folder name or path).</summary>
    public string SourceFolderDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return "";
            try
            {
                var dir = Path.GetDirectoryName(SourcePath);
                if (string.IsNullOrEmpty(dir)) return SourcePath;
                return dir.Length > 50 ? "..." + dir.Substring(dir.Length - 47) : dir;
            }
            catch { return ""; }
        }
    }

    [ObservableProperty] private double _progress; // 0 to 100
    [ObservableProperty] private string _status = "Pending";
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isCancelled;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private double _speed; // MB/s
    [ObservableProperty] private string _eta = "Calculating..."; // "00:00:00" or "Calculating..."
    [ObservableProperty] private string _sizeInfo = ""; // "12.4 MB / 180.0 MB"

    /// <summary>Last time progress UI was pushed (throttling; UI-thread only).</summary>
    public DateTime LastProgressUpdate { get; set; }

    public System.Threading.CancellationTokenSource? Cts { get; set; }

    public bool IsRunning => !IsCompleted && !IsFailed && !IsCancelled;

    partial void OnIsCompletedChanged(bool value) => OnPropertyChanged(nameof(IsRunning));
    partial void OnIsFailedChanged(bool value) => OnPropertyChanged(nameof(IsRunning));
    partial void OnIsCancelledChanged(bool value) => OnPropertyChanged(nameof(IsRunning));
}

public class FileOperationManager
{
    private readonly ConcurrentQueue<FileTaskItem> _taskQueue = new();
    public ObservableCollection<FileTaskItem> ActiveTasks { get; } = new();
    
    private bool _isProcessing;
    private readonly object _lock = new();
    
    public void QueueTask(FileTaskItem task)
    {
        task.Cts = new System.Threading.CancellationTokenSource();
        _taskQueue.Enqueue(task);
        ProcessQueue();
    }
    
    public void CancelTask(FileTaskItem task)
    {
        if (!task.IsCompleted && !task.IsCancelled)
        {
            try 
            {
                task.Cts?.Cancel();
                task.IsCancelled = true;
                task.Status = "Cancelling...";
            }
            catch {}
        }
    }

    public void ClearCompletedTasks()
    {
        var toRemove = ActiveTasks.Where(t => t.IsCompleted || t.IsCancelled || t.IsFailed).ToList();
        foreach (var t in toRemove)
        {
            ActiveTasks.Remove(t);
        }
    }

    public void RemoveTask(FileTaskItem task)
    {
        if (task == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ActiveTasks.Contains(task))
                ActiveTasks.Remove(task);
        });
    }

    public void RetryTask(FileTaskItem task)
    {
        if (task == null || string.IsNullOrEmpty(task.SourcePath) || string.IsNullOrEmpty(task.DestinationPath)) return;
        var newTask = new FileTaskItem
        {
            Type = task.Type,
            Name = task.Name,
            SourcePath = task.SourcePath,
            DestinationPath = task.DestinationPath,
            Status = "Pending"
        };
        QueueTask(newTask);
    }
    
    private async void ProcessQueue()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        
        while (_taskQueue.TryDequeue(out var task))
        {
            Dispatcher.UIThread.Post(() => ActiveTasks.Insert(0, task));
            
            task.Status = "Running...";
            
            try
            {
                if (task.Cts != null && task.Cts.Token.IsCancellationRequested)
                {
                     throw new OperationCanceledException();
                }

                await RunTaskAsync(task, task.Cts!.Token);
                
                if (!task.IsCancelled)
                {
                    SetProgress(task, 100);
                    SetStatus(task, "Completed");
                    task.IsCompleted = true;
                }
                else
                {
                    task.Status = "Cancelled";
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = "Cancelled";
                task.IsCancelled = true;
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                task.IsFailed = true;
                task.ErrorMessage = ex.Message;
            }
            
            if (ActiveTasks.Count > 50)
            {
                Dispatcher.UIThread.Post(() => {
                    if (ActiveTasks.Count > 50) ActiveTasks.RemoveAt(ActiveTasks.Count - 1);
                });
            }
        }
        
        _isProcessing = false;
    }
    
    public void MoveFileBackground(string sourcePath, string destPath)
    {
        var task = new FileTaskItem
        {
            Type = FileTaskType.Move,
            Name = Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            DestinationPath = destPath,
            Status = "Pending"
        };
        QueueTask(task);
    }
    
    public void CopyFileBackground(string sourcePath, string destPath)
    {
        var task = new FileTaskItem
        {
            Type = FileTaskType.Copy,
            Name = Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            DestinationPath = destPath,
            Status = "Pending"
        };
        QueueTask(task);
    }
    
    public void DeleteFileBackground(string path)
    {
        var task = new FileTaskItem
        {
            Type = FileTaskType.Delete,
            Name = Path.GetFileName(path),
            SourcePath = path,
            DestinationPath = "",
            Status = "Pending"
        };
        QueueTask(task);
    }

    private async Task RunTaskAsync(FileTaskItem task, System.Threading.CancellationToken token)
    {
        await Task.Run(async () => 
        {
            if (task.Type == FileTaskType.Copy)
            {
                bool isDir = Directory.Exists(task.SourcePath);
                if (isDir)
                    await CopyDirectoryAsync(task.SourcePath, task.DestinationPath, task, token);
                else
                    await CopyFileAsync(task.SourcePath, task.DestinationPath, task, token);
            }
            else if (task.Type == FileTaskType.Move)
            {
                bool isDir = Directory.Exists(task.SourcePath);
                if (isDir)
                    await MoveDirectoryAsync(task.SourcePath, task.DestinationPath, task, token);
                else
                    await MoveFileAsync(task.SourcePath, task.DestinationPath, task, token);
            }
            else if (task.Type == FileTaskType.Delete)
            {
                await DeleteItemAsync(task.SourcePath, task, token);
            }
        });
    }

    private async Task CopyFileAsync(string source, string dest, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException();
        
        if (!File.Exists(source)) throw new FileNotFoundException("Source file not found", source);
        
        string dir = Path.GetDirectoryName(dest)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        long totalBytes = new FileInfo(source).Length;
        long copiedBytes = 0;
        DateTime startTime = DateTime.Now;
        
        using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
        using (var destStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            byte[] buffer = new byte[65536];
            int bytesRead;
            
            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                copiedBytes += bytesRead;
                
                UpdateCopyProgress(task, totalBytes, copiedBytes, startTime);
            }
        }
        
        SetProgress(task, 100);
    }
    
    private async Task MoveFileAsync(string source, string dest, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException();

        if (!File.Exists(source)) throw new FileNotFoundException("Source file not found", source);
        string dir = Path.GetDirectoryName(dest)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(dest)) File.Delete(dest);
        
        if (token.IsCancellationRequested) throw new OperationCanceledException();
        
        File.Move(source, dest);
        SetProgress(task, 100);
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, FileTaskItem task, System.Threading.CancellationToken token)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        
        if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

        long totalBytes = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        long copiedBytes = 0;
        DateTime startTime = DateTime.Now;
        var files = dir.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        int totalFiles = files.Count;
        int processedFiles = 0;

        foreach (FileInfo file in files)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException();

            string tempPath = Path.Combine(destinationDir, file.FullName.Substring(dir.FullName.Length + 1));
            string dirPath = Path.GetDirectoryName(tempPath)!;
            
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
            
            long fileLen = file.Length;
            using (var sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                byte[] buffer = new byte[65536];
                int bytesRead;
                long fileCopied = 0;
                
                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException();
                    
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    fileCopied += bytesRead;
                    
                    long globalCopied = copiedBytes + fileCopied;
                    UpdateCopyProgress(task, totalBytes, globalCopied, startTime);
                }
            }
            
            copiedBytes += fileLen;
            processedFiles++;
        }
        
        SetProgress(task, 100);
    }
    
    private async Task MoveDirectoryAsync(string sourceDir, string destinationDir, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (Path.GetPathRoot(sourceDir) == Path.GetPathRoot(destinationDir))
        {
            if (Directory.Exists(destinationDir))
            {
                SetStatus(task, "Merging...");
                await CopyDirectoryAsync(sourceDir, destinationDir, task, token);
                
                if (!token.IsCancellationRequested)
                {
                    Directory.Delete(sourceDir, true);
                }
            }
            else
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                Directory.Move(sourceDir, destinationDir);
                SetProgress(task, 100);
            }
        }
        else
        {
            await CopyDirectoryAsync(sourceDir, destinationDir, task, token);
            
            if (!token.IsCancellationRequested)
            {
                 Directory.Delete(sourceDir, true);
            }
        }
    }
    
    private async Task DeleteItemAsync(string path, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException();

        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            long totalBytes = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            long deletedBytes = 0;
            DateTime startTime = DateTime.Now;
            
            foreach (FileInfo file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                
                File.Delete(file.FullName);
                deletedBytes += file.Length;
                UpdateCopyProgress(task, totalBytes, deletedBytes, startTime);
            }
            
            Directory.Delete(path, true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
        
        SetProgress(task, 100);
    }

    // ---- UI-thread-safe setters (progress callbacks run on the background worker) ----

    private static void SetProgress(FileTaskItem task, double value)
    {
        if (Dispatcher.UIThread.CheckAccess()) task.Progress = value;
        else Dispatcher.UIThread.Post(() => task.Progress = value);
    }

    private static void SetStatus(FileTaskItem task, string value)
    {
        if (Dispatcher.UIThread.CheckAccess()) task.Status = value;
        else Dispatcher.UIThread.Post(() => task.Status = value);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#") + " GB";
        if (bytes >= 1024L * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.#") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
        return bytes + " B";
    }

    private void UpdateCopyProgress(FileTaskItem task, long totalBytes, long copiedBytes, DateTime startTime)
    {
        if (totalBytes <= 0) return;

        // Throttle: push to UI at most ~8x/sec (every 125ms). Reading progress from the
        // bound properties happens on the UI thread, so we marshal the writes there.
        var now = DateTime.Now;
        if ((now - task.LastProgressUpdate).TotalMilliseconds < 125 && copiedBytes < totalBytes)
            return;
        task.LastProgressUpdate = now;

        string sizeInfo = FormatBytes(copiedBytes) + " / " + FormatBytes(totalBytes);

        double progress = (double)copiedBytes / totalBytes * 100;
        double clamped = Math.Min(100, progress);

        TimeSpan elapsed = now - startTime;
        double speed = 0;
        string eta = "Calculating...";

        if (elapsed.TotalSeconds >= 1)
        {
            double bytesPerSec = copiedBytes / elapsed.TotalSeconds;
            speed = Math.Round(bytesPerSec / 1024.0 / 1024.0, 2);

            double remainingBytes = totalBytes - copiedBytes;
            if (bytesPerSec > 0 && remainingBytes > 0)
            {
                double remainingSeconds = remainingBytes / bytesPerSec;
                if (double.IsFinite(remainingSeconds) && remainingSeconds >= 0)
                    eta = TimeSpan.FromSeconds(remainingSeconds).ToString(@"hh\:mm\:ss");
            }
            else if (remainingBytes <= 0)
            {
                eta = "00:00:00";
            }
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            task.Progress = clamped;
            task.Speed = speed;
            task.Eta = eta;
            task.SizeInfo = sizeInfo;
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                task.Progress = clamped;
                task.Speed = speed;
                task.Eta = eta;
                task.SizeInfo = sizeInfo;
            });
        }
    }
}

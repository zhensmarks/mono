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
    
    [ObservableProperty] private double _progress; // 0 to 100
    [ObservableProperty] private string _status = "Pending";
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isCancelled;
    [ObservableProperty] private string _errorMessage = "";
    
    public System.Threading.CancellationTokenSource? Cts { get; set; }
}

public class FileOperationManager
{
    private readonly ConcurrentQueue<FileTaskItem> _taskQueue = new();
    public ObservableCollection<FileTaskItem> ActiveTasks { get; } = new();
    
    private bool _isProcessing;
    
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
    
    private async void ProcessQueue()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        
        while (_taskQueue.TryDequeue(out var task))
        {
            // Add to Active UI list
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
                    task.Status = "Completed";
                    task.IsCompleted = true;
                    task.Progress = 100;
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
            
            // Remove from active list after delay? Or keep them?
            // User might want to see history.
            // For now, keep them. Maybe limit count?
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
            Name = $"Move {Path.GetFileName(sourcePath)}",
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
            Name = $"Copy {Path.GetFileName(sourcePath)}",
            SourcePath = sourcePath,
            DestinationPath = destPath,
            Status = "Pending"
        };
        QueueTask(task);
    }

    private async Task RunTaskAsync(FileTaskItem task, System.Threading.CancellationToken token)
    {
        await Task.Run(async () => 
        {
            bool isDir = Directory.Exists(task.SourcePath);
            
            if (task.Type == FileTaskType.Copy)
            {
                if (isDir)
                    await CopyDirectoryAsync(task.SourcePath, task.DestinationPath, task, token);
                else
                    CopyFile(task.SourcePath, task.DestinationPath, task, token);
            }
            else if (task.Type == FileTaskType.Move)
            {
                if (isDir)
                    await MoveDirectoryAsync(task.SourcePath, task.DestinationPath, task, token);
                else
                    MoveFile(task.SourcePath, task.DestinationPath, task, token);
            }
        });
    }

    private void CopyFile(string source, string dest, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException();
        
        if (!File.Exists(source)) throw new FileNotFoundException("Source file not found", source);
        
        string dir = Path.GetDirectoryName(dest)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        // Simple file copy doesn't support cancellation well without stream copying
        // But for small files it's atomic. For large files we'd need streams.
        // For MVP, we check before copy.
        // If user wants PAUSE, we really need streams.
        
        File.Copy(source, dest, true);
        task.Progress = 100;
    }
    
    private void MoveFile(string source, string dest, FileTaskItem task, System.Threading.CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException();

         if (!File.Exists(source)) throw new FileNotFoundException("Source file not found", source);
        string dir = Path.GetDirectoryName(dest)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(dest)) File.Delete(dest);
        
        if (token.IsCancellationRequested) throw new OperationCanceledException();
        
        File.Move(source, dest);
        task.Progress = 100;
    }


    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, FileTaskItem task, System.Threading.CancellationToken token)
    {
        // Get subdirectories and files
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        
        if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

        // Count total bytes for progress
        long totalBytes = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        long copiedBytes = 0;
        
        // Copy files
        foreach (FileInfo file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException();

            string tempPath = Path.Combine(destinationDir, file.FullName.Substring(dir.FullName.Length + 1));
            string dirPath = Path.GetDirectoryName(tempPath)!;
            
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
            
            file.CopyTo(tempPath, true);
            
            copiedBytes += file.Length;
            task.Progress = (double)copiedBytes / totalBytes * 100;
        }
    }
    
    private async Task MoveDirectoryAsync(string sourceDir, string destinationDir, FileTaskItem task, System.Threading.CancellationToken token)
    {
        // DestinationDir is the FULL target path (e.g., .../#OKE/bahan)
        
        // Simple Move if on same drive
        if (Path.GetPathRoot(sourceDir) == Path.GetPathRoot(destinationDir))
        {
            if (Directory.Exists(destinationDir))
            {
                // Merge needed (Copy + Delete)
                task.Status = "Merging...";
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
                task.Progress = 100;
            }
        }
        else
        {
            // Different drive: Copy + Delete
            await CopyDirectoryAsync(sourceDir, destinationDir, task, token);
            
            if (!token.IsCancellationRequested)
            {
                 Directory.Delete(sourceDir, true);
            }
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BMachine.UI.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class LogPanelSidebar : UserControl
{
    public LogPanelSidebar()
    {
        InitializeComponent();
    }



    private void LogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Scroll to bottom when items are added
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.UIThread.Post(() => 
            {
                 var scroll = this.FindControl<ScrollViewer>("LogScrollViewer");
                 scroll?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
    }

    private void OnRootPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            if (DataContext is DashboardViewModel vm)
            {
                vm.ToggleLogPanelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private System.IO.FileSystemWatcher? _progressWatcher;
    private System.IO.FileSystemWatcher? _resultWatcher;
    private DateTime _lastReadTime = DateTime.MinValue;
    private DateTime _lastResultTime = DateTime.MinValue;

    protected override void OnDataContextChanged(EventArgs e)
    {
         base.OnDataContextChanged(e);
         if (DataContext is DashboardViewModel vm)
         {
             Avalonia.Input.DragDrop.SetAllowDrop(this, true);
             AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
             // Subscribe to collection changes
            if (vm.LogItems != null)
            {
                vm.LogItems.CollectionChanged += LogItems_CollectionChanged;
            }

            // Setup Progress Watcher if not already
            if (_progressWatcher == null)
            {
                try 
                {
                    var tempPath = System.IO.Path.GetTempPath();
                    _progressWatcher = new System.IO.FileSystemWatcher(tempPath, "bmachine_progress.json");
                    _progressWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite;
                    _progressWatcher.Changed += (s, args) => OnProgressFileChanged(vm);
                    _progressWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start progress watcher: {ex.Message}");
                }
            }
            
            // Setup Result Watcher (for script completion alerts)
            if (_resultWatcher == null)
            {
                try 
                {
                    var tempPath = System.IO.Path.GetTempPath();
                    _resultWatcher = new System.IO.FileSystemWatcher(tempPath, "bmachine_result.json");
                    _resultWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite;
                    _resultWatcher.Changed += (s, args) => OnResultFileChanged(vm);
                    _resultWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start result watcher: {ex.Message}");
                }
            }
         }
    }

    private void OnProgressFileChanged(DashboardViewModel vm)
    {
        // Debounce simple to avoid double reads (FileSystemWatcher fires multiple times often)
        if ((DateTime.Now - _lastReadTime).TotalMilliseconds < 50) return;
        _lastReadTime = DateTime.Now;

        try 
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bmachine_progress.json");
            if (!System.IO.File.Exists(path)) return;

            string json = "";
            // Retry loop for file lock
            for (int i = 0; i < 5; i++)
            {
                try 
                {
                    using (var stream = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        json = reader.ReadToEnd();
                    }
                    break; 
                }
                catch (System.IO.IOException) 
                { 
                    System.Threading.Thread.Sleep(20); 
                }
            }
            
            if (string.IsNullOrWhiteSpace(json)) return;

            // Simple JSON parsing to avoid heavy dependencies if possible, or use System.Text.Json
            // Format: {"current": 1, "total": 10, "file": "name.jpg", "status": "processing"}
            
            int current = 0;
            int total = 0;
            string file = "";
            
            // Regex parsing is safer/faster for this specific known format without adding dependencies
            var matchCur = System.Text.RegularExpressions.Regex.Match(json, "\"current\":\\s*(\\d+)");
            if (matchCur.Success) int.TryParse(matchCur.Groups[1].Value, out current);

            var matchTot = System.Text.RegularExpressions.Regex.Match(json, "\"total\":\\s*(\\d+)");
            if (matchTot.Success) int.TryParse(matchTot.Groups[1].Value, out total);

            var matchFile = System.Text.RegularExpressions.Regex.Match(json, "\"file\":\\s*\"([^\"]+)\"");
            if (matchFile.Success) file = matchFile.Groups[1].Value;

            if (total > 0 || current > 0)
            {
                Dispatcher.UIThread.Post(() => 
                {
                     // Update Progress Bar
                     vm.ProgressMax = total > 0 ? total : 100;
                     vm.ProgressValue = current;
                     vm.IsDeterminateProgress = total > 0;

                     if (total > 0)
                     {
                         int percent = (int)((double)current / total * 100);
                         vm.ProcessStatusText = $"Running... {percent}%";
                     }

                     // Update Log UI
                     // We could remove the previous 'Progress' line to avoid spamming the log? 
                     // Or just append? User asked to "show process".
                     // If we append every file of 500 files, log becomes useless.
                     // Better: Update the LAST log item if it IS a progress item, otherwise Add new.
                     
                     var msg = $"[Photoshop] Processing {current}/{total}: {file}";
                     
                     var last = vm.LogItems.LastOrDefault();
                     if (last != null && last.Message.StartsWith("[Photoshop] Processing"))
                     {
                         // Update existing item (Hack: LogItem usually immutable? Let's see)
                         // LogItem is a record or class? Check later. 
                         // If we can't update, we remove last and add new.
                         vm.LogItems.Remove(last);
                     }
                     
                     vm.LogItems.Add(new BMachine.UI.Models.LogItem(msg, BMachine.UI.Models.LogLevel.Info) 
                     { 
                         Color = Avalonia.Media.Brushes.Cyan 
                     });

                     // Auto Scroll
                     var scroll = this.FindControl<ScrollViewer>("LogScrollViewer");
                     scroll?.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Progress read error: {ex.Message}");
        }
    }

    private void OnResultFileChanged(DashboardViewModel vm)
    {
        // Debounce
        if ((DateTime.Now - _lastResultTime).TotalMilliseconds < 100) return;
        _lastResultTime = DateTime.Now;

        try 
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bmachine_result.json");
            if (!System.IO.File.Exists(path)) return;

            string json = "";
            // Retry loop for file lock
            for (int i = 0; i < 5; i++)
            {
                try 
                {
                    using (var stream = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        json = reader.ReadToEnd();
                    }
                    break; 
                }
                catch (System.IO.IOException) 
                { 
                    System.Threading.Thread.Sleep(20); 
                }
            }
            
            if (string.IsNullOrWhiteSpace(json)) return;

            // Parse JSON
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            string title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Result" : "Result";
            
            if (root.TryGetProperty("lines", out var linesProp) && linesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                Dispatcher.UIThread.Post(() => 
                {
                     // Add Header
                     vm.LogItems.Add(new BMachine.UI.Models.LogItem($"=== {title} ===", BMachine.UI.Models.LogLevel.Info) 
                     { 
                         Color = Avalonia.Media.Brushes.LimeGreen 
                     });
                     
                     // Add Each Line
                     foreach (var line in linesProp.EnumerateArray())
                     {
                         var text = line.GetString() ?? "";
                         var color = Avalonia.Media.Brushes.White;
                         if (text.StartsWith("OK")) color = Avalonia.Media.Brushes.LimeGreen;
                         else if (text.StartsWith("GAGAL") || text.StartsWith("LEWATI")) color = Avalonia.Media.Brushes.Orange;
                         
                         vm.LogItems.Add(new BMachine.UI.Models.LogItem(text, BMachine.UI.Models.LogLevel.Info) 
                         { 
                             Color = color 
                         });
                     }

                     // Auto Scroll
                     var scroll = this.FindControl<ScrollViewer>("LogScrollViewer");
                     scroll?.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Result read error: {ex.Message}");
        }
    }

    private async void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            if (e.Data.Contains(Avalonia.Input.DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        var path = file.Path.LocalPath;
                        var ext = System.IO.Path.GetExtension(path);
                        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || 
                            ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                        {
                            await vm.HandleDroppedLogFile(path);
                        }
                    }
                }
            }
        }
    }
}

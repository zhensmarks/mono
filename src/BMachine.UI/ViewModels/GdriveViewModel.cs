using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;
using System.Threading;
using System.Collections.Generic;
using BMachine.UI.Models;
using BMachine.UI.Services;
using BMachine.SDK;

namespace BMachine.UI.ViewModels;

public partial class GdriveViewModel : ObservableObject
{
    private readonly IDatabase? _database;
    private readonly GdriveService _gdriveService;
    private CancellationTokenSource? _cancellationTokenSource;
    
    [ObservableProperty] private ObservableCollection<GdriveFileItem> _files = new();
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _isProcessing;
    
    // Login State
    [ObservableProperty] private string _userEmail = "Belum login";
    [ObservableProperty] private bool _isLoggedIn;
    
    // Settings
    [ObservableProperty] private string _driveFolderId = "";
    [ObservableProperty] private string _baseFolderPath = "";
    [ObservableProperty] private bool _isSettingsOpen;
    
    // Completion Dialog
    [ObservableProperty] private bool _showCompletionDialog;
    [ObservableProperty] private int _completionSuccessCount;
    [ObservableProperty] private int _completionFailureCount;
    
    // Log
    [ObservableProperty] private string _logOutput = "";
    [ObservableProperty] private bool _showLogPanel;
    
    private bool _stopRequested;

    public GdriveViewModel(IDatabase? database)
    {
        _database = database;
        _gdriveService = new GdriveService();
        LoadSettings();
    }
    
    private async void LoadSettings()
    {
        if (_database != null)
        {
            DriveFolderId = await _database.GetAsync<string>("Configs.Gdrive.FolderId") ?? "";
            BaseFolderPath = await _database.GetAsync<string>("Configs.Gdrive.BaseFolderPath") ?? "";
        }
    }

    private async Task PersistSettings()
    {
        if (_database != null)
        {
            await _database.SetAsync("Configs.Gdrive.FolderId", DriveFolderId);
            await _database.SetAsync("Configs.Gdrive.BaseFolderPath", BaseFolderPath);
        }
    }

    private void AppendLog(string message)
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }

    [RelayCommand]
    private async Task Login()
    {
        try
        {
            AppendLog("Memulai login ke Google Drive...");
            await _gdriveService.LoginAsync();
            UserEmail = $"Login sebagai: {_gdriveService.UserEmail}";
            IsLoggedIn = true;
            AppendLog($"Login berhasil: {_gdriveService.UserEmail}");
        }
        catch (Exception e)
        {
            UserEmail = "Login gagal";
            IsLoggedIn = false;
            AppendLog($"Login gagal: {e.Message}");
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _gdriveService.Logout();
        UserEmail = "Belum login";
        IsLoggedIn = false;
        AppendLog("Logout berhasil.");
    }

    [RelayCommand]
    private async Task DropFiles(string[] paths)
    {
        IsProcessing = true;
        try
        {
            await Task.Run(() =>
            {
                var validFiles = new System.Collections.Generic.List<string>();
                var searchPattern = new System.Collections.Generic.HashSet<string> { ".jpg", ".jpeg", ".png", ".psd", ".webp", ".zip", ".rar", ".7z", ".txt" };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        var ext = Path.GetExtension(path).ToLower();
                        if (searchPattern.Contains(ext))
                        {
                            validFiles.Add(path);
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                         validFiles.AddRange(SafeGetFiles(path, searchPattern));
                    }
                }

                if (validFiles.Any())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var f in validFiles)
                        {
                             var smartPath = GetSmartDisplayPath(f);
                             AddFileJob(f, smartPath);
                        }
                        HasFiles = Files.Count > 0;
                    });
                }
            });
        }
        catch (Exception ex)
        {
             Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                AppendLog($"Error processing drop: {ex.Message}"));
        }
        finally
        {
             IsProcessing = false;
        }
    }

    private string GetSmartDisplayPath(string fullPath)
    {
        try
        {
            var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // ... (Same Month detection logic) ...
            
            // Reuse logic? Or copy. Let's keep it simple and shared or duplicated for safety.
            // Actually, we can just extract the Month Detection logic.
            
            int foundIndex = FindMonthIndex(parts);

            if (foundIndex != -1)
            {
                var monthPart = parts[foundIndex];
                var fileName = parts[parts.Length - 1];
                
                if (foundIndex == parts.Length - 2)
                    return Path.Combine(monthPart, fileName);
                
                if (foundIndex < parts.Length - 2)
                    return Path.Combine(monthPart, "...", fileName);
                    
                return string.Join(Path.DirectorySeparatorChar, parts.Skip(foundIndex));
            }
        }
        catch { }

        return GetRelativePathFromBase(fullPath);
    }
    
    private string GetUploadEffectivePath(string fullPath)
    {
        // This method returns the path structure we want on GDrive (Full Relative from Month)
        try 
        {
            var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int foundIndex = FindMonthIndex(parts);
            
            if (foundIndex != -1)
            {
                // Returns: Month/Sub1/Sub2/File.ext
                return string.Join(Path.DirectorySeparatorChar, parts.Skip(foundIndex));
            }
        }
        catch {}
        
        // Fallback: Relative to Base or just Filename
        return GetRelativePathFromBase(fullPath); 
    }

    private int FindMonthIndex(string[] parts)
    {
        var months = new[] { 
            "JANUARI", "FEBRUARI", "MARET", "APRIL", "MEI", "JUNI", 
            "JULI", "AGUSTUS", "SEPTEMBER", "OKTOBER", "NOVEMBER", "DESEMBER",
            "JANUARY", "FEBRUARY", "MARCH", "MAY", "JUNE", "JULY", "AUGUST", "OCTOBER", "DECEMBER"
        };
        
        for (int i = 0; i < parts.Length; i++)
        {
            if (months.Any(m => parts[i].ToUpper().Contains(m))) return i;
        }
        return -1;
    }

    private string GetRelativePathFromBase(string fullPath)
    {
        // 1. Try to use BaseFolderPath if configured and valid
        if (!string.IsNullOrWhiteSpace(BaseFolderPath))
        {
            // Normalize slashes for comparison
            var normalizedBase = Path.GetFullPath(BaseFolderPath).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(fullPath);

            if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(normalizedBase, normalizedPath);
            }
        }

        // 2. Fallback: Use just filename (flat) if no base folder match
        // Or could try to be smart, but consistent behavior is better.
        // For drop folder, usually we want relative to drop, but here we process file-by-file.
        // Let's stick to filename if BaseFolder doesn't match, unless we want to infer.
        return Path.GetFileName(fullPath);
    }
    
    private IEnumerable<string> GetSupportedFiles(string folder)
    {
        var extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.txt" };
        return extensions.SelectMany(ext => Directory.GetFiles(folder, ext, SearchOption.AllDirectories));
    }
    
    private bool IsSupportedExtension(string ext)
    {
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".txt";
    }

    private void AddFileJob(string filePath, string displayPath)
    {
        if (Files.Any(f => f.FilePath == filePath)) return;
        Files.Add(new GdriveFileItem(filePath, displayPath));
    }

    [RelayCommand]
    private void RemoveFile(GdriveFileItem item)
    {
        if (Files.Contains(item)) Files.Remove(item);
        HasFiles = Files.Count > 0;
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsProcessing) return;
        
        // Smart Clear: If items are selected, remove them. Else clear all.
        if (Files.Any(f => f.IsSelected))
        {
            RemoveSelectedFiles();
        }
        else
        {
            Files.Clear();
            HasFiles = false;
            LogOutput = "";
        }
    }

    private void RemoveSelectedFiles()
    {
        var selected = Files.Where(x => x.IsSelected).ToList();
        foreach (var item in selected)
        {
            Files.Remove(item);
        }
        HasFiles = Files.Count > 0;
    }

    [RelayCommand]
    private void ToggleLog()
    {
        ShowLogPanel = !ShowLogPanel;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        await PersistSettings();
        IsSettingsOpen = false;
        AppendLog("Pengaturan disimpan.");
    }

    [RelayCommand]
    private async Task SelectBaseFolder()
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Pilih Base Folder",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            BaseFolderPath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private void ClearBaseFolder()
    {
        BaseFolderPath = "";
    }

    [RelayCommand]
    private void CloseCompletionDialog()
    {
        ShowCompletionDialog = false;
    }

    [RelayCommand]
    private async Task StartUpload()
    {
        if (IsProcessing) return;

        if (!IsLoggedIn)
        {
            AppendLog("Error: Harap login terlebih dahulu!");
            return;
        }

        if (string.IsNullOrWhiteSpace(DriveFolderId))
        {
            AppendLog("Error: ID Folder Google Drive kosong!");
            return;
        }

        if (!await _gdriveService.ValidateFolderIdAsync(DriveFolderId))
        {
            AppendLog("Error: ID Folder Google Drive tidak valid!");
            return;
        }

        IsProcessing = true;
        _stopRequested = false;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        
        int success = 0;
        int failed = 0;
        
        AppendLog("Memulai proses upload...");

        // Live Queue Loop
        while (!_stopRequested && !token.IsCancellationRequested)
        {
             // Find next item to process
             // Strategy: Prioritize 'Ready', but also retry 'Failed' if logic allows?
             // StartUpload logic usually starts fresh. 
             // Logic in StartUpload was: Files.Where(j => j.Status == "Ready" || j.IsFailed)
             // So we should pick one.
             
             var item = Files.FirstOrDefault(j => j.Status == "Ready" || j.IsFailed);
             
             if (item == null) break; // Queue Empty
             
             item.Status = "Uploading...";
             item.Progress = 0;
             item.IsFailed = false;
             item.ErrorMessage = null;

            try
            {
                // Calculate relative path for GDrive folder structure
                // CRITICAL FIX: Use GetUploadEffectivePath (Full) not DisplayPath (Ellipsis)
                var effectivePath = GetUploadEffectivePath(item.FilePath);
                var relativePath = Path.GetDirectoryName(effectivePath);
                var relativeParts = relativePath?.Split(Path.DirectorySeparatorChar).ToList() ?? new List<string>();
                
                var parentId = await _gdriveService.EnsurePathAsync(DriveFolderId, relativeParts);

                await _gdriveService.UploadFileAsync(item.FilePath, parentId, (sent, total) =>
                {
                    item.Progress = (int)((double)sent / total * 100);
                }, token);

                item.Status = "Selesai";
                item.IsDone = true;
                item.Progress = 100;
                success++;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested) 
                {
                    item.Status = "Ready"; // Reset if cancelled mid-way? Or Failed?
                }
                else 
                {
                    item.Status = "Gagal";
                    item.IsFailed = true;
                    item.ErrorMessage = e.Message.Length > 50 ? e.Message.Substring(0, 50) + "..." : e.Message;
                    AppendLog($"Gagal upload {item.DisplayPath}: {e.Message}");
                    failed++;
                }
            }
        }

        IsProcessing = false;
        AppendLog("Antrian selesai.");

        if (!_stopRequested)
        {
            CompletionSuccessCount = success;
            CompletionFailureCount = failed;
            ShowCompletionDialog = true;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _stopRequested = true;
        _cancellationTokenSource?.Cancel();
        AppendLog("Stop diminta...");
    }

    [RelayCommand]
    private async Task RetryFile(GdriveFileItem item)
    {
        if (IsProcessing) return;
        if (!IsLoggedIn || string.IsNullOrWhiteSpace(DriveFolderId)) return;
        
        IsProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        
        AppendLog($"Mengulangi upload {item.DisplayPath}...");
        
        item.Status = "Uploading...";
        item.Progress = 0;
        item.IsFailed = false;
        item.ErrorMessage = null;

        try
        {
            // CRITICAL FIX: Use GetUploadEffectivePath
            var effectivePath = GetUploadEffectivePath(item.FilePath);
            var relativePath = Path.GetDirectoryName(effectivePath);
            var relativeParts = relativePath?.Split(Path.DirectorySeparatorChar).ToList() ?? new List<string>();
            
            var parentId = await _gdriveService.EnsurePathAsync(DriveFolderId, relativeParts);
            await _gdriveService.UploadFileAsync(item.FilePath, parentId, (sent, total) =>
            {
                item.Progress = (int)((double)sent / total * 100);
            }, token);

            item.Status = "Selesai";
            item.IsDone = true;
            item.Progress = 100;
        }
        catch (Exception e)
        {
            item.Status = "Gagal";
            item.IsFailed = true;
            item.ErrorMessage = e.Message.Length > 50 ? e.Message.Substring(0, 50) + "..." : e.Message;
            AppendLog($"Gagal upload {item.DisplayPath}: {e.Message}");
        }
        
        IsProcessing = false;
    }
    private System.Collections.Generic.IEnumerable<string> SafeGetFiles(string rootPath, System.Collections.Generic.HashSet<string> extensions)
    {
        var result = new System.Collections.Generic.List<string>();
        var stack = new System.Collections.Generic.Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                // Files
                foreach (var file in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (extensions.Contains(ext))
                    {
                         result.Add(file);
                    }
                }

                // Subdirectories
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    stack.Push(subDir);
                }
            }
            catch (Exception)
            {
                // Ignore Access Denied / Path Too Long / etc for this specific folder
            }
        }
        return result;
    }
}

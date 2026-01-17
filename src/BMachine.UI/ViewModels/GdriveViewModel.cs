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

    private async Task SaveSettings()
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
    private void DropFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLower();
                if (IsSupportedExtension(ext))
                {
                    var relativePath = GetRelativePathFromBase(path);
                    AddFileJob(path, relativePath);
                }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    var allFiles = GetSupportedFiles(path);
                    foreach (var f in allFiles)
                    {
                        var relativePath = GetRelativePathFromBase(f);
                        AddFileJob(f, relativePath);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Error accessing directory {path}: {ex.Message}");
                }
            }
        }
        HasFiles = Files.Count > 0;
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
        Files.Clear();
        HasFiles = false;
        LogOutput = "";
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
    private async Task SaveSettingsCommand()
    {
        await SaveSettings();
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

        foreach (var item in Files.Where(j => j.Status == "Ready" || j.IsFailed))
        {
            if (_stopRequested || token.IsCancellationRequested) break;

            item.Status = "Uploading...";
            item.Progress = 0;
            item.IsFailed = false;
            item.ErrorMessage = null;

            try
            {
                // Calculate relative path for GDrive folder structure
                var relativePath = Path.GetDirectoryName(item.DisplayPath);
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
                item.Status = "Gagal";
                item.IsFailed = true;
                item.ErrorMessage = e.Message.Length > 50 ? e.Message.Substring(0, 50) + "..." : e.Message;
                AppendLog($"Gagal upload {item.DisplayPath}: {e.Message}");
                failed++;
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
            var relativePath = Path.GetDirectoryName(item.DisplayPath);
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
}

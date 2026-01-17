using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.Core.Security;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;

namespace BMachine.UI.ViewModels;

public class FilePreviewItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📄";
    public bool IsLocked { get; set; }
}

public partial class FolderLockerViewModel : ObservableObject
{
    private readonly FolderLockerService _service;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _folderPath = "";

    [ObservableProperty]
    private string _folderName = "";

    [ObservableProperty]
    private bool _isFolderSelected;

    [ObservableProperty]
    private int _normalFileCount;

    [ObservableProperty]
    private int _lockedFileCount;

    [ObservableProperty]
    private ObservableCollection<FilePreviewItem> _filePreviewList = new();

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusText = ""; // Default empty, only show if important

    [ObservableProperty]
    private bool _isSetupMode;

    [ObservableProperty]
    private string _setupPassword = "";

    [ObservableProperty]
    private string _setupConfirmPassword = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _setupQrCode;

    [ObservableProperty]
    private bool _isSetupConfigured;



    // Commands
    [RelayCommand]
    private void EnterSetup()
    {
        IsSetupMode = true;
        StatusText = "";

        // Check if config exists
        var config = FolderLockerConfig.Load();
        if (config.IsConfigured)
        {
            IsSetupConfigured = true;
            SetupPassword = ""; 
            SetupConfirmPassword = "";
            
            // Regenerate QR from existing secret
            GenerateQrBitmap(config.TotpSecret);
        }
        else
        {
            IsSetupConfigured = false;
            SetupPassword = "";
            SetupConfirmPassword = "";
            SetupQrCode = null;
        }
    }

    [RelayCommand]
    private void ResetSetup()
    {
        // 1. Delete Config File
        FolderLockerConfig.Reset();
        
        // 2. Refresh Service
        _service.ReloadConfig();
        
        // 3. Reset VM State
        IsConfigured = false;
        IsSetupConfigured = false;
        SetupQrCode = null;
        SetupPassword = "";
        SetupConfirmPassword = "";
        
        // 4. Update Lock Logic
        if (IsFolderSelected) SetFolder(FolderPath);
    }


    [RelayCommand]
    private void CancelSetup()
    {
        IsSetupMode = false;
        // Don't clear status text entirely if processing, but usually safe
    }

    private void GenerateQrBitmap(string secret)
    {
        try
        {
             var uri = TotpService.GetProvisioningUri(secret, "FolderLocker", "BMachine");
             using var qrGenerator = new QRCoder.QRCodeGenerator();
             using var qrData = qrGenerator.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.Q);
             using var qrCode = new QRCoder.PngByteQRCode(qrData);
             var qrBytes = qrCode.GetGraphic(5); // 5 pixels per module
             using var ms = new MemoryStream(qrBytes);
             SetupQrCode = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch (Exception ex)
        {
            StatusText = $"QR Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GenerateSetup()
    {
        if (string.IsNullOrWhiteSpace(SetupPassword) || SetupPassword != SetupConfirmPassword)
        {
            StatusText = "Password tidak cocok / kosong!";
            return;
        }

        try 
        {
            var secret = TotpService.GenerateSecret();
            
            // Render QR
            GenerateQrBitmap(secret);

            // Save Config
            var config = FolderLockerConfig.Load();
            config.Password = SetupPassword;
            config.TotpSecret = secret;
            config.Save();

            // Refresh Service!
            _service.ReloadConfig();

            IsConfigured = true;
            IsSetupConfigured = true; // Switch view to QR only
            StatusText = "Config Saved! Scan QR Code.";
            
            // Re-evaluate CanLock/CanUnlock
            if (IsFolderSelected) SetFolder(FolderPath);
            
            // Auto close setup? User said "simpan tampilan langsung ke qr code"
            // So we stay in Setup Mode but show QR (Logic matches IsSetupConfigured=true)
        }
        catch (Exception ex)
        {
            StatusText = $"Setup Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseSetup()
    {
        IsSetupMode = false;
        StatusText = "";
    }

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _canLock;

    [ObservableProperty]
    private bool _canUnlock;

    [ObservableProperty]
    private bool _isConfigured;

    public FolderLockerViewModel()
    {
        _service = new FolderLockerService();
        IsConfigured = _service.IsConfigured;
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel == null) return;

            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pilih Folder",
                AllowMultiple = false
            });

            if (result.Count > 0)
            {
                SetFolder(result[0].Path.LocalPath);
            }
        }
    }

    public void SetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ClearFolder();
            return;
        }

        // Fix: Trim trailing slash to ensure GetFileName works
        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        FolderPath = path;
        FolderName = Path.GetFileName(path);
        IsFolderSelected = true;
        
        var (normal, locked) = FolderLockerService.CountFiles(path);
        NormalFileCount = normal;
        LockedFileCount = locked;
        
        // Update Preview List (Max 10 items)
        FilePreviewList.Clear();
        var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Take(10);
        foreach (var f in files)
        {
            var isLocked = f.EndsWith(".dma", StringComparison.OrdinalIgnoreCase);
            FilePreviewList.Add(new FilePreviewItem 
            { 
                Name = Path.GetFileName(f), 
                Icon = isLocked ? "🔒" : "📄",
                IsLocked = isLocked
            });
        }
        
        if (normal + locked > 10)
        {
            FilePreviewList.Add(new FilePreviewItem { Name = $"... dan {normal + locked - 10} file lainnya", Icon = "" });
        }
        
        CanLock = normal > 0 && IsConfigured;
        CanUnlock = locked > 0 && IsConfigured;
        
        // Show Full Path in Status
        // Show Full Path in Status
        StatusText = "";
    }

    [RelayCommand]
    public void ClearFolder()
    {
        FolderPath = "";
        FolderName = "";
        IsFolderSelected = false;
        NormalFileCount = 0;
        LockedFileCount = 0;
        FilePreviewList.Clear();
        CanLock = false;
        CanUnlock = false;
        StatusText = "Siap";
        Progress = 0;
    }

    [RelayCommand]
    private async Task LockFolder()
    {
        if (string.IsNullOrEmpty(FolderPath) || !IsConfigured) return;

        IsProcessing = true;
        CanLock = false;
        CanUnlock = false;
        Progress = 0;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(int percent, string file)>(p =>
            {
                Progress = p.percent;
                StatusText = p.file;
            });

            await _service.LockFolderAsync(FolderPath, progress, _cts.Token);
            StatusText = "Selesai! Semua file terkunci.";
            
            // Refresh counts
            SetFolder(FolderPath);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Dibatalkan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [ObservableProperty]
    private bool _isUnlocking;

    [ObservableProperty]
    private string _otpInput = "";

    [RelayCommand]
    private void StartUnlock()
    {
        IsUnlocking = true;
        OtpInput = "";
        StatusText = "Enter Code from Authenticator App";
    }

    [RelayCommand]
    private void CancelUnlock()
    {
        IsUnlocking = false;
        OtpInput = "";
        StatusText = "";
    }

    [RelayCommand]
    private async Task ConfirmUnlock()
    {
        if (string.IsNullOrWhiteSpace(OtpInput)) return;
        
        // Hide overlay immediately to show progress? Or keep it?
        // Let's keep it or switch to processing view.
        // We call the existing logic.
        
        IsUnlocking = false; 
        await UnlockFolderWithOtp(OtpInput);
    }

    /// <summary>
    /// Core unlock logic
    /// </summary>
    public async Task UnlockFolderWithOtp(string otpCode)
    {
        if (string.IsNullOrEmpty(FolderPath) || !IsConfigured) return;

        IsProcessing = true;
        CanLock = false;
        CanUnlock = false;
        Progress = 0;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(int percent, string file)>(p =>
            {
                Progress = p.percent;
                StatusText = p.file;
            });

            await _service.UnlockFolderAsync(FolderPath, otpCode, progress, _cts.Token);
            StatusText = "Selesai! Semua file terbuka.";
            
            // Refresh counts
            SetFolder(FolderPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Dibatalkan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }
}

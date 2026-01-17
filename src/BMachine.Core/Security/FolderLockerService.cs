namespace BMachine.Core.Security;

/// <summary>
/// Service for locking and unlocking folders using AES-256-GCM encryption.
/// </summary>
public class FolderLockerService
{
    private FolderLockerConfig _config = new FolderLockerConfig();
    private TotpService? _totp;

    public FolderLockerService()
    {
        ReloadConfig();
    }

    public void ReloadConfig()
    {
        _config = FolderLockerConfig.Load();
        if (!string.IsNullOrEmpty(_config.TotpSecret))
            _totp = new TotpService(_config.TotpSecret);
        else
            _totp = null;
    }

    public bool IsConfigured => _config.IsConfigured;

    /// <summary>
    /// Locks folder by creating a duplicate with "_ORI" suffix containing encrypted files.
    /// The original folder remains untouched.
    /// </summary>
    public async Task LockFolderAsync(string folderPath, IProgress<(int percent, string file)>? progress = null, CancellationToken ct = default)
    {
        if (!_config.IsConfigured)
            throw new InvalidOperationException("Folder Locker belum dikonfigurasi. Buka Settings untuk setup.");

        var targetFolder = folderPath + "_ORI";
        Directory.CreateDirectory(targetFolder);

        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".dma", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            progress?.Report((100, "Tidak ada file untuk dikunci."));
            return;
        }

        var total = files.Count;
        var done = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                // Determine relative path to maintain structure
                var relativePath = Path.GetRelativePath(folderPath, file);
                var destFile = Path.Combine(targetFolder, relativePath);
                
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                var data = await File.ReadAllBytesAsync(file, ct);
                var encrypted = AesGcmCryptor.Encrypt(data, _config.Password);
                var outPath = destFile + ".dma";
                
                await File.WriteAllBytesAsync(outPath, encrypted, ct);
                
                done++;
                var percent = (int)(done * 100.0 / total);
                progress?.Report((percent, $"LOCK [ORI]: {Path.GetFileName(file)}"));
            }
            catch (Exception ex)
            {
                done++;
                progress?.Report(((int)(done * 100.0 / total), $"FAIL: {Path.GetFileName(file)} - {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// Unlocks all .dma files in a folder (requires valid OTP code).
    /// </summary>
    public async Task UnlockFolderAsync(string folderPath, string otpCode, IProgress<(int percent, string file)>? progress = null, CancellationToken ct = default)
    {
        if (!_config.IsConfigured)
            throw new InvalidOperationException("Folder Locker belum dikonfigurasi.");

        // Verify OTP
        if (_totp != null && !_totp.Verify(otpCode))
            throw new UnauthorizedAccessException("Kode OTP salah atau expired.");

        var files = Directory.EnumerateFiles(folderPath, "*.dma", SearchOption.AllDirectories).ToList();

        if (files.Count == 0)
        {
            progress?.Report((100, "Tidak ada file .dma untuk dibuka."));
            return;
        }

        var total = files.Count;
        var done = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                var encrypted = await File.ReadAllBytesAsync(file, ct);
                var decrypted = AesGcmCryptor.Decrypt(encrypted, _config.Password);
                var outPath = file[..^4]; // Remove .dma extension
                await File.WriteAllBytesAsync(outPath, decrypted, ct);
                File.Delete(file);
                
                done++;
                var percent = (int)(done * 100.0 / total);
                progress?.Report((percent, $"UNLOCK: {Path.GetFileName(outPath)}"));
            }
            catch (Exception ex)
            {
                done++;
                progress?.Report(((int)(done * 100.0 / total), $"FAIL: {Path.GetFileName(file)} - {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// Counts files in a folder for preview.
    /// </summary>
    public static (int normalFiles, int lockedFiles) CountFiles(string folderPath)
    {
        var allFiles = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
        var normal = 0;
        var locked = 0;
        
        foreach (var f in allFiles)
        {
            if (f.EndsWith(".dma", StringComparison.OrdinalIgnoreCase))
                locked++;
            else
                normal++;
        }
        
        return (normal, locked);
    }
}

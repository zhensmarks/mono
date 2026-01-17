using System.Text.Json;
using System.Security.Cryptography;

namespace BMachine.Core.Security;

/// <summary>
/// Configuration for Folder Locker, stored encrypted on disk.
/// </summary>
public class FolderLockerConfig
{
    public string Password { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    
    // Window Geometry Persistence
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 540;
    public int WindowHeight { get; set; } = 460;
    
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BMachine", "FolderLocker"
    );
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.enc");
    private static readonly byte[] MasterSalt = "BMachineFLock!!"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool IsConfigured => !string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(TotpSecret);

    /// <summary>
    /// Loads config from encrypted file. Returns empty config if not found.
    /// </summary>
    public static FolderLockerConfig Load()
    {
        if (!File.Exists(ConfigFile))
            return new FolderLockerConfig();
            
        try
        {
            var blob = File.ReadAllBytes(ConfigFile);
            var key = DeriveKey();
            
            var nonce = blob.AsSpan(0, NonceSize).ToArray();
            var ciphertextWithTag = blob.AsSpan(NonceSize).ToArray();
            var ciphertext = ciphertextWithTag.AsSpan(0, ciphertextWithTag.Length - TagSize).ToArray();
            var tag = ciphertextWithTag.AsSpan(ciphertextWithTag.Length - TagSize).ToArray();
            
            using var aes = new AesGcm(key, TagSize);
            var plaintext = new byte[ciphertext.Length];
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            
            var json = System.Text.Encoding.UTF8.GetString(plaintext);
            return JsonSerializer.Deserialize<FolderLockerConfig>(json) ?? new FolderLockerConfig();
        }
        catch
        {
            return new FolderLockerConfig();
        }
    }

    /// <summary>
    /// Saves config to encrypted file.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        
        var key = DeriveKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var json = JsonSerializer.Serialize(this);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(json);
        
        using var aes = new AesGcm(key, TagSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        
        using var ms = new MemoryStream();
        ms.Write(nonce);
        ms.Write(ciphertext);
        ms.Write(tag);
        
        File.WriteAllBytes(ConfigFile, ms.ToArray());
    }

    /// <summary>
    /// Deletes config file (reset).
    /// </summary>
    public static void Reset()
    {
        if (File.Exists(ConfigFile))
            File.Delete(ConfigFile);
    }

    private static byte[] DeriveKey()
    {
        var username = Environment.UserName;
        using var pbkdf2 = new Rfc2898DeriveBytes(username + "BMachineLock", MasterSalt, 100_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }
}

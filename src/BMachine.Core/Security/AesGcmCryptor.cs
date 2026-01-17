using System.Security.Cryptography;
using System.IO.Compression;

namespace BMachine.Core.Security;

/// <summary>
/// AES-256-GCM encryption helper compatible with Python DMA Locker format.
/// File format: HEADER (4 bytes) + SALT (16 bytes) + NONCE (12 bytes) + CIPHERTEXT
/// </summary>
public static class AesGcmCryptor
{
    private static readonly byte[] HeaderMagic = "DMA2"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 200_000;

    /// <summary>
    /// Encrypts data using AES-256-GCM with PBKDF2 key derivation.
    /// </summary>
    public static byte[] Encrypt(byte[] data, string password)
    {
        // Generate salt and nonce
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        
        // Derive key using PBKDF2
        var key = DeriveKey(password, salt);
        
        // Compress data
        var compressed = Compress(data);
        
        // Encrypt
        using var aes = new AesGcm(key, TagSize);
        var ciphertext = new byte[compressed.Length];
        var tag = new byte[TagSize];
        aes.Encrypt(nonce, compressed, ciphertext, tag);
        
        // Build output: HEADER + SALT + NONCE + CIPHERTEXT + TAG
        using var ms = new MemoryStream();
        ms.Write(HeaderMagic);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(ciphertext);
        ms.Write(tag);
        return ms.ToArray();
    }

    /// <summary>
    /// Decrypts data encrypted by this class or the Python DMA Locker.
    /// </summary>
    public static byte[] Decrypt(byte[] blob, string password)
    {
        // Validate header
        if (blob.Length < HeaderMagic.Length + SaltSize + NonceSize + TagSize)
            throw new CryptographicException("Invalid file format: too short");
            
        if (!blob.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
            throw new CryptographicException("Invalid file format: bad header");
        
        // Extract components
        var offset = HeaderMagic.Length;
        var salt = blob.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = blob.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        
        var ciphertextWithTag = blob.AsSpan(offset).ToArray();
        var ciphertext = ciphertextWithTag.AsSpan(0, ciphertextWithTag.Length - TagSize).ToArray();
        var tag = ciphertextWithTag.AsSpan(ciphertextWithTag.Length - TagSize).ToArray();
        
        // Derive key
        var key = DeriveKey(password, salt);
        
        // Decrypt
        using var aes = new AesGcm(key, TagSize);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        
        // Decompress
        return Decompress(plaintext);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256-bit key
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            // Write zlib header (Python uses zlib which has a 2-byte header)
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            deflate.Write(data);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        // Skip zlib header
        input.ReadByte();
        input.ReadByte();
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}

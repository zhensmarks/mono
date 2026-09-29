using System;

namespace PixelcutCompact.Services;

/// <summary>
/// Dilempar saat halaman Pixelcut menampilkan indikasi bahwa akun sudah mencapai limit.
/// Dipakai oleh rotator akun untuk berpindah ke akun berikutnya.
/// </summary>
public class PixaAccountLimitException : Exception
{
    public string AccountName { get; }

    public PixaAccountLimitException(string accountName, string message)
        : base(message)
    {
        AccountName = accountName;
    }
}

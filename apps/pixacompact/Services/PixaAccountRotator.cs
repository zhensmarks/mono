using System;
using System.Collections.Generic;
using System.Linq;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services;

/// <summary>
/// Pengelola rotasi round-robin akun Pixelcut. Akun yang sudah mencapai limit
/// (proaktif lewat counter, atau reaktif dari deteksi halaman) dilewati otomatis.
/// Kelas ini tidak thread-safe secara penuh; pemakaian di service sudah diserialkan.
/// </summary>
public class PixaAccountRotator
{
    private readonly List<PixaAccount> _accounts;
    private int _index;

    public PixaAccountRotator(IEnumerable<PixaAccount> accounts)
    {
        _accounts = accounts?.Where(a => a != null).ToList() ?? new List<PixaAccount>();
        SyncActiveFlags();
    }

    /// <summary>Sinkronkan flag IsActive model dengan index rotasi (untuk indikator UI).</summary>
    private void SyncActiveFlags()
    {
        for (int i = 0; i < _accounts.Count; i++)
        {
            _accounts[i].IsActive = i == _index;
        }
    }
    public IReadOnlyList<PixaAccount> Accounts => _accounts;

    /// <summary>Index akun aktif (0-based), -1 kalau tidak ada akun.</summary>
    public int ActiveIndex => _accounts.Count == 0 ? -1 : _index;

    /// <summary>Akun aktif sekarang (null kalau daftar kosong).</summary>
    public PixaAccount? Current => _accounts.Count == 0 ? null : _accounts[_index];

    /// <summary>Apakah masih ada akun yang belum limit.</summary>
    public bool HasUsableAccount => _accounts.Any(a => !a.IsLimited);

    /// <summary>Jumlah akun yang belum limit.</summary>
    public int UsableCount => _accounts.Count(a => !a.IsLimited);

    /// <summary>Jumlah akun yang sudah limit.</summary>
    public int LimitedCount => _accounts.Count(a => a.IsLimited);

    /// <summary>
    /// Pindah ke akun berikutnya yang belum limit secara round-robin.
    /// Mengembalikan akun berikutnya, atau null kalau semua akun sudah limit.
    /// </summary>
    public PixaAccount? Next()
    {
        if (_accounts.Count == 0) return null;
        if (!HasUsableAccount) return null;

        for (int step = 1; step <= _accounts.Count; step++)
        {
            int candidate = (_index + step) % _accounts.Count;
            if (!_accounts[candidate].IsLimited)
            {
                _index = candidate;
                SyncActiveFlags();
                return _accounts[_index];
            }
        }

        return null;
    }

    /// <summary>
    /// Ambil akun aktif yang bisa dipakai. Kalau akun aktif sudah limit, otomatis
    /// pindah ke akun berikutnya. Mengembalikan null kalau semua limit.
    /// </summary>
    public PixaAccount? CurrentOrNext()
    {
        var current = Current;
        if (current != null && !current.IsLimited) return current;
        return Next();
    }

    /// <summary>Tandai akun sebagai limit beserta alasannya.</summary>
    public void MarkLimited(PixaAccount account, string reason)
    {
        if (account == null) return;
        account.IsLimited = true;
        account.LimitedReason = reason ?? "";
    }

    /// <summary>Reset status sesi semua akun dan kembali ke akun pertama.</summary>
    public void ResetSession()
    {
        foreach (var a in _accounts) a.ResetSession();
        _index = 0;
        SyncActiveFlags();
    }
}

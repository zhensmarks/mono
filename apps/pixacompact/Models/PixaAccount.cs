using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PixelcutCompact.Models;

public partial class PixaAccount : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _lastCredits = "Pending";
    [ObservableProperty] private DateTime? _lastChecked;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";

    /// <summary>
    /// Suffix folder profil browser Playwright untuk akun ini. Tiap akun punya profil
    /// sendiri supaya sesi login tidak saling menimpa. Auto-generate kalau kosong.
    /// </summary>
    [ObservableProperty] private string _profileSuffix = "";

    /// <summary>Batas maksimal gambar per akun per sesi (default 100 = limit akun gratis pixelcut).</summary>
    [ObservableProperty] private int _maxImagesPerSession = 100;

    /// <summary>Jumlah gambar yang sudah diproses akun ini pada sesi berjalan (runtime, tidak persist).</summary>
    [ObservableProperty] private int _imagesUsedThisSession;

    /// <summary>True kalau akun ini terdeteksi/sudah mencapai limit pada sesi berjalan.</summary>
    [ObservableProperty] private bool _isLimited;

    /// <summary>Alasan akun ditandai limit (mis. "Limit terdeteksi di halaman" / "Batas 100 gambar tercapai").</summary>
    [ObservableProperty] private string _limitedReason = "";

    /// <summary>Waktu sesi akun dimulai (untuk reset manual/berkala).</summary>
    [ObservableProperty] private DateTime? _sessionStartedAt;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Pastikan ProfileSuffix terisi (dipakai sebagai nama folder profil browser).</summary>
    public string EnsureProfileSuffix()
    {
        if (string.IsNullOrWhiteSpace(ProfileSuffix) || !IsSafeProfileSuffix(ProfileSuffix.Trim()))
        {
            ProfileSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        }
        var normalized = ProfileSuffix.Trim();
        if (!string.Equals(ProfileSuffix, normalized, StringComparison.Ordinal))
            ProfileSuffix = normalized;
        return normalized;
    }

    public static bool IsSafeProfileSuffix(string? suffix) =>
        !string.IsNullOrWhiteSpace(suffix) && suffix.Length <= 64 &&
        suffix.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static string GetBrowserProfileFolderName(string? profileSuffix)
    {
        if (string.IsNullOrWhiteSpace(profileSuffix)) return "BrowserProfile";
        var normalized = profileSuffix.Trim();
        if (!IsSafeProfileSuffix(normalized)) throw new ArgumentException("Browser profile suffix contains invalid path characters.", nameof(profileSuffix));
        return $"BrowserProfile_{normalized}";
    }

    /// <summary>Reset status sesi: counter, flag limit, dan waktu mulai.</summary>
    public void ResetSession()
    {
        ImagesUsedThisSession = 0;
        IsLimited = false;
        LimitedReason = "";
        SessionStartedAt = DateTime.Now;
    }

    partial void OnImagesUsedThisSessionChanged(int value) => OnPropertyChanged(nameof(SessionStatusText));
    partial void OnIsLimitedChanged(bool value) => OnPropertyChanged(nameof(SessionStatusText));
    partial void OnLimitedReasonChanged(string value) => OnPropertyChanged(nameof(SessionStatusText));
    partial void OnMaxImagesPerSessionChanged(int value) => OnPropertyChanged(nameof(SessionStatusText));

    /// <summary>Teks status untuk ditampilkan di UI.</summary>
    public string SessionStatusText =>
        IsLimited
            ? (string.IsNullOrWhiteSpace(LimitedReason) ? "LIMIT" : $"LIMIT — {LimitedReason}")
            : $"Sisa sesi: {Math.Max(0, MaxImagesPerSession - ImagesUsedThisSession)}/{MaxImagesPerSession}";
}

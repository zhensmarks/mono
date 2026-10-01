using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services;

/// <summary>
/// Imports and exports only the non-secret account metadata needed to reconnect
/// PixaCompact's per-account browser profiles. Browser profile data is never bundled.
/// </summary>
public sealed class PixaAccountBackupService
{
    public const string FormatName = "PixaCompact.AccountBackup";
    public const int CurrentVersion = 1;
    private const int MaxJsonLength = 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ToJson(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var accounts = (settings.PixaAccounts ?? new List<PixaAccount>())
            .Where(account => account != null)
            .Select(account => new PixaAccountBackupEntry
            {
                Id = account.Id,
                Name = account.Name ?? string.Empty,
                ProfileSuffix = account.EnsureProfileSuffix(),
                BrowserProfileFolderName = PixaAccount.GetBrowserProfileFolderName(account.ProfileSuffix),
                MaxImagesPerSession = Math.Clamp(account.MaxImagesPerSession, 1, 10000)
            })
            .ToList();

        var backup = new PixaAccountBackupDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IncludesCredentials = false,
            IncludesBrowserProfiles = false,
            RequiresLoginIfBrowserProfileMissing = true,
            UseAccountRotation = settings.UseAccountRotation,
            ActiveAccountId = settings.ActiveAccountId,
            Accounts = accounts
        };

        Validate(backup);
        return JsonSerializer.Serialize(backup, JsonOptions);
    }

    public void ExportToFile(string filePath, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A backup file path is required.", nameof(filePath));
        File.WriteAllText(filePath, ToJson(settings));
    }

    public PixaAccountBackupDocument ReadBackup(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The account backup is empty.");
        if (json.Length > MaxJsonLength) throw new InvalidDataException("The account backup is too large.");

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The backup must be a JSON object.");

        if (TryGetProperty(root, "format", out var formatElement))
        {
            if (!string.Equals(formatElement.GetString(), FormatName, StringComparison.Ordinal))
                throw new InvalidDataException("This JSON file is not a PixaCompact account backup.");

            var backup = JsonSerializer.Deserialize<PixaAccountBackupDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("The account backup could not be read.");
            Validate(backup);
            return backup;
        }

        // Existing installations may have backed up the entire settings.json manually.
        // Read only its account metadata; unknown legacy secret fields are ignored and never re-exported.
        if (!TryGetProperty(root, nameof(AppSettings.PixaAccounts), out var accountsElement) ||
            accountsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected a PixaCompact account backup or a settings.json containing PixaAccounts.");

        var legacySettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("The legacy settings backup could not be read.");
        var legacyAccounts = legacySettings.PixaAccounts ?? new List<PixaAccount>();
        var legacyBackup = new PixaAccountBackupDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IncludesCredentials = false,
            IncludesBrowserProfiles = false,
            RequiresLoginIfBrowserProfileMissing = true,
            UseAccountRotation = legacySettings.UseAccountRotation,
            ActiveAccountId = legacySettings.ActiveAccountId,
            Accounts = legacyAccounts.Where(account => account != null).Select(account =>
            {
                var suffix = account.ProfileSuffix;
                if (string.IsNullOrWhiteSpace(suffix))
                    suffix = Guid.NewGuid().ToString("N")[..8];
                return new PixaAccountBackupEntry
                {
                    Id = account.Id,
                    Name = account.Name ?? string.Empty,
                    ProfileSuffix = suffix.Trim(),
                    BrowserProfileFolderName = PixaAccount.GetBrowserProfileFolderName(suffix),
                    MaxImagesPerSession = Math.Clamp(account.MaxImagesPerSession, 1, 10000)
                };
            }).ToList()
        };

        if (legacyBackup.ActiveAccountId.HasValue && !legacyBackup.Accounts.Any(account => account.Id == legacyBackup.ActiveAccountId.Value))
            legacyBackup.ActiveAccountId = null;
        Validate(legacyBackup);
        return legacyBackup;
    }

    public PixaAccountBackupDocument ImportFromFile(string filePath, AppSettings targetSettings)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A backup file path is required.", nameof(filePath));
        ArgumentNullException.ThrowIfNull(targetSettings);
        var backup = ReadBackup(File.ReadAllText(filePath));
        ApplyBackup(backup, targetSettings);
        return backup;
    }

    /// <summary>Merge imported accounts before existing entries, preserving non-exported accounts.</summary>
    public void ApplyBackup(PixaAccountBackupDocument backup, AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);
        Validate(backup);

        var existing = targetSettings.PixaAccounts ?? new List<PixaAccount>();
        var importedIds = backup.Accounts.Select(account => account.Id).ToHashSet();
        var rebuilt = new List<PixaAccount>(backup.Accounts.Count + existing.Count);

        foreach (var entry in backup.Accounts)
        {
            var account = existing.FirstOrDefault(candidate => candidate.Id == entry.Id);
            if (account == null)
            {
                account = new PixaAccount { Id = entry.Id };
            }

            account.Name = entry.Name;
            account.ProfileSuffix = entry.ProfileSuffix;
            account.MaxImagesPerSession = Math.Clamp(entry.MaxImagesPerSession, 1, 10000);
            account.ResetSession();
            rebuilt.Add(account);
        }

        rebuilt.AddRange(existing.Where(account => !importedIds.Contains(account.Id)));
        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in rebuilt)
        {
            var suffix = account.EnsureProfileSuffix();
            if (!IsSafeProfileSuffix(suffix) || !suffixes.Add(suffix))
                throw new InvalidDataException("The restored account profiles conflict with an existing account. Remove the duplicate account or profile before restoring.");
        }

        targetSettings.PixaAccounts = rebuilt;
        targetSettings.UseAccountRotation = backup.UseAccountRotation;
        targetSettings.ActiveAccountId = backup.ActiveAccountId ?? targetSettings.ActiveAccountId;
        if (targetSettings.ActiveAccountId.HasValue && !rebuilt.Any(account => account.Id == targetSettings.ActiveAccountId.Value))
            targetSettings.ActiveAccountId = rebuilt.FirstOrDefault()?.Id;
    }

    public static bool IsSafeProfileSuffix(string? suffix)
        => PixaAccount.IsSafeProfileSuffix(suffix);

    private static void Validate(PixaAccountBackupDocument backup)
    {
        if (backup == null) throw new InvalidDataException("The account backup could not be read.");
        if (!string.Equals(backup.Format, FormatName, StringComparison.Ordinal) || backup.Version != CurrentVersion)
            throw new InvalidDataException("This PixaCompact account backup version is not supported.");
        if (backup.IncludesCredentials || backup.IncludesBrowserProfiles)
            throw new InvalidDataException("This backup claims to contain credentials or browser profiles; it cannot be imported as a metadata-only backup.");
        if (backup.Accounts == null || backup.Accounts.Count > 500)
            throw new InvalidDataException("The account backup contains an invalid number of accounts.");

        var ids = new HashSet<Guid>();
        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in backup.Accounts)
        {
            if (account == null || account.Id == Guid.Empty || !ids.Add(account.Id))
                throw new InvalidDataException("The account backup contains a missing or duplicate account ID.");
            if (account.Name == null || account.Name.Length > 128)
                throw new InvalidDataException("An account name in the backup is too long.");
            if (!IsSafeProfileSuffix(account.ProfileSuffix) || !suffixes.Add(account.ProfileSuffix))
                throw new InvalidDataException("The account backup contains an invalid or duplicate browser profile suffix.");
            if (!string.Equals(account.BrowserProfileFolderName, PixaAccount.GetBrowserProfileFolderName(account.ProfileSuffix), StringComparison.Ordinal))
                throw new InvalidDataException("A browser profile folder name does not match its account suffix.");
            if (account.MaxImagesPerSession < 1 || account.MaxImagesPerSession > 10000)
                throw new InvalidDataException("An account session limit is outside the supported range.");
        }
        if (backup.ActiveAccountId.HasValue && !ids.Contains(backup.ActiveAccountId.Value))
            throw new InvalidDataException("The active account ID is not present in the backup.");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

public sealed class PixaAccountBackupDocument
{
    public string Format { get; set; } = PixaAccountBackupService.FormatName;
    public int Version { get; set; } = PixaAccountBackupService.CurrentVersion;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IncludesCredentials { get; set; }
    public bool IncludesBrowserProfiles { get; set; }
    public bool RequiresLoginIfBrowserProfileMissing { get; set; } = true;
    public bool UseAccountRotation { get; set; }
    public Guid? ActiveAccountId { get; set; }
    public List<PixaAccountBackupEntry> Accounts { get; set; } = new();
}

public sealed class PixaAccountBackupEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProfileSuffix { get; set; } = string.Empty;
    public string BrowserProfileFolderName { get; set; } = string.Empty;
    public int MaxImagesPerSession { get; set; } = 100;
}

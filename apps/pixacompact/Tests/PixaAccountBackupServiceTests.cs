using System;
using System.IO;
using PixelcutCompact.Models;
using PixelcutCompact.Services;
using Xunit;

namespace PixelcutCompact.Tests;

public sealed class PixaAccountBackupServiceTests
{
    [Fact]
    public void ExportThenRestoreAfterReset_PreservesAccountIdentityAndProfileBindingWithoutSecrets()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var settings = new AppSettings
        {
            UseAccountRotation = true,
            ActiveAccountId = secondId,
            PixaAccounts =
            {
                new PixaAccount { Id = firstId, Name = "Studio", ProfileSuffix = "a1b2c3d4", MaxImagesPerSession = 80 },
                new PixaAccount { Id = secondId, Name = "Personal", ProfileSuffix = "e5f6a7b8", MaxImagesPerSession = 100 }
            }
        };
        var backupService = new PixaAccountBackupService();
        var json = backupService.ToJson(settings);

        Assert.Contains("PixaCompact.AccountBackup", json);
        Assert.Contains("requiresLoginIfBrowserProfileMissing", json);
        Assert.Contains("browserProfileFolderName", json);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);

        var restored = new AppSettings();
        var backup = backupService.ReadBackup(json);
        backupService.ApplyBackup(backup, restored);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "pixacompact-account-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var settingsPath = Path.Combine(tempDirectory, "settings.json");
            var writer = new SettingsService(settingsPath);
            writer.Save(restored);

            // A fresh SettingsService instance simulates a reset/restart after restore.
            var loaded = new SettingsService(settingsPath).Load();
            Assert.Equal(new[] { firstId, secondId }, loaded.PixaAccounts.ConvertAll(account => account.Id));
            Assert.Equal("Personal", loaded.PixaAccounts[1].Name);
            Assert.Equal("e5f6a7b8", loaded.PixaAccounts[1].ProfileSuffix);
            Assert.Equal(secondId, loaded.ActiveAccountId);
            Assert.True(loaded.UseAccountRotation);
            Assert.Equal("BrowserProfile_e5f6a7b8", PixaAccount.GetBrowserProfileFolderName(loaded.PixaAccounts[1].ProfileSuffix));
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void LegacySettingsJsonImport_IgnoresAndDoesNotReExportLegacyApiKeys()
    {
        var id = Guid.NewGuid();
        var secret = "legacy-api-key-must-not-escape";
        var json = $$"""
        {
          "PixaAccounts": [
            { "Id": "{{id}}", "Name": "Existing", "ApiKey": "{{secret}}", "ProfileSuffix": "1122aabb", "MaxImagesPerSession": 100 }
          ],
          "ActiveAccountId": "{{id}}",
          "UseAccountRotation": true,
          "PixaApiKey": "{{secret}}"
        }
        """;

        var service = new PixaAccountBackupService();
        var backup = service.ReadBackup(json);
        var restored = new AppSettings();
        service.ApplyBackup(backup, restored);
        var exported = service.ToJson(restored);

        Assert.Single(restored.PixaAccounts);
        Assert.Equal("Existing", restored.PixaAccounts[0].Name);
        Assert.Equal("1122aabb", restored.PixaAccounts[0].ProfileSuffix);
        Assert.DoesNotContain(secret, exported, StringComparison.Ordinal);
        Assert.DoesNotContain("PixaApiKey", exported, StringComparison.Ordinal);
        Assert.False(backup.IncludesCredentials);
        Assert.False(backup.IncludesBrowserProfiles);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a\\b")]
    [InlineData(" ")]
    public void ImportRejectsUnsafeBrowserProfileSuffix(string suffix)
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "format": "PixaCompact.AccountBackup",
          "version": 1,
          "includesCredentials": false,
          "includesBrowserProfiles": false,
          "requiresLoginIfBrowserProfileMissing": true,
          "activeAccountId": "{{id}}",
          "accounts": [
            { "id": "{{id}}", "name": "Unsafe", "profileSuffix": "{{suffix}}", "browserProfileFolderName": "BrowserProfile_{{suffix}}", "maxImagesPerSession": 100 }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => new PixaAccountBackupService().ReadBackup(json));
    }
}

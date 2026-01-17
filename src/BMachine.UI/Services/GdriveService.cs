using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.Collections.Generic;

namespace BMachine.UI.Services;

public class GdriveService
{
    private static readonly string[] Scopes = { DriveService.Scope.Drive };
    private static readonly string ApplicationName = "BMachine";

    public DriveService? Service { get; private set; }
    public string? UserEmail { get; private set; }
    public bool IsLoggedIn => Service != null;

    public async Task LoginAsync()
    {
        UserCredential credential;
        var credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json");

        if (!File.Exists(credentialsPath))
        {
            throw new FileNotFoundException($"credentials.json tidak ditemukan di {credentialsPath}. Silakan download dari Google Cloud Console.");
        }

        await using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            var clientSecrets = await GoogleClientSecrets.FromStreamAsync(stream);
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets.Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore("gdrive_token", true));
        }

        Service = new DriveService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });

        var aboutRequest = Service.About.Get();
        aboutRequest.Fields = "user(emailAddress)";
        var about = await aboutRequest.ExecuteAsync();
        UserEmail = about.User.EmailAddress;
    }

    public void Logout()
    {
        Service = null;
        UserEmail = null;
        
        // Delete stored token
        var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gdrive_token");
        if (Directory.Exists(tokenPath))
        {
            try { Directory.Delete(tokenPath, true); } catch { }
        }
    }

    public async Task<bool> ValidateFolderIdAsync(string folderId)
    {
        if (Service == null) return false;

        try
        {
            var request = Service.Files.Get(folderId);
            request.Fields = "mimeType, trashed";
            var file = await request.ExecuteAsync();
            return file.MimeType == "application/vnd.google-apps.folder" && file.Trashed != true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> EnsurePathAsync(string rootFolderId, List<string> relativeParts)
    {
        if (Service == null) throw new InvalidOperationException("Service is not initialized.");
        var parentId = rootFolderId;
        
        foreach (var part in relativeParts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            
            var request = Service.Files.List();
            request.Q = $"mimeType='application/vnd.google-apps.folder' and trashed=false and name='{part.Replace("'", "\\'")}' and '{parentId}' in parents";
            request.Fields = "files(id)";
            var result = await request.ExecuteAsync();
            
            if (result.Files.Count > 0)
            {
                parentId = result.Files[0].Id;
            }
            else
            {
                var newFolder = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = part,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { parentId }
                };
                var createRequest = Service.Files.Create(newFolder);
                createRequest.Fields = "id";
                var createdFolder = await createRequest.ExecuteAsync();
                parentId = createdFolder.Id;
            }
        }
        return parentId;
    }

    public async Task UploadFileAsync(string filePath, string parentId, Action<long, long> onProgress, CancellationToken cancellationToken = default)
    {
        if (Service == null) throw new InvalidOperationException("Belum login ke Google Drive.");

        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = Path.GetFileName(filePath),
            Parents = new List<string> { parentId }
        };

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var request = Service.Files.Create(fileMetadata, stream, "application/octet-stream");
        request.Fields = "id";

        request.ProgressChanged += (IUploadProgress progress) =>
        {
            onProgress(progress.BytesSent, stream.Length);
        };

        await request.UploadAsync(cancellationToken);
    }
}

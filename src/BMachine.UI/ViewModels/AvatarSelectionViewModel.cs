using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using System.IO;

namespace BMachine.UI.ViewModels;

public partial class AvatarSelectionViewModel : ObservableObject
{
    public class AvatarItem
    {
        public string Source { get; set; } = ""; // "preset:file.png"
        public Bitmap? Image { get; set; }
        public bool IsSelected { get; set; }
    }

    [ObservableProperty]
    private ObservableCollection<AvatarItem> _presets = new();

    [ObservableProperty]
    private AvatarItem? _selectedAvatar;
    
    [ObservableProperty]
    private Bitmap? _previewImage;
    
    // Result
    public string SelectedSource { get; private set; } = "";
    public event Action<string>? OnAvatarSelected;
    public event Action? OnCancel;
    public Func<Task<string?>>? PickCustomImageFunc { get; set; }

    public AvatarSelectionViewModel()
    {
        LoadPresets();
    }

    [ObservableProperty] private string _debugStatus = "Initializing...";
    
    private void LoadPresets()
    {
        _debugStatus = "";
        Presets.Clear();
        
        var filesFound = 0;
        
        // 1. Try AssetLoader with multiple URI variations
        // Sometimes assembly name is different or defaults
        var assemblyNames = new[] { "BMachine.UI", "BMachine.App", "BMachine" };
        
        for (int i = 1; i <= 20; i++)
        {
             // Fix: Use D2 for C# 2-digit padding
             var filename = $"avatar_{i:D2}.png";
             bool loaded = false;

             foreach (var asm in assemblyNames)
             {
                 var uri = new Uri($"avares://{asm}/Assets/Avatars/{filename}");
                 if (Avalonia.Platform.AssetLoader.Exists(uri))
                 {
                     try 
                     {
                         var bitmap = new Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                         Presets.Add(new AvatarItem { Source = $"preset:{filename}", Image = bitmap });
                         loaded = true;
                         break;
                     } 
                     catch(Exception ex) { _debugStatus += $"Err load {uri}: {ex.Message}\n"; }
                 }
             }
             
             if (loaded) 
             {
                 filesFound++;
                 continue; 
             }
             
             // 2. Fallback to Filesystem with Search
             // First look in common locations
             var baseDir = AppDomain.CurrentDomain.BaseDirectory;
             var candidatePaths = new List<string>
             {
                 Path.Combine(baseDir, "Assets", "Avatars", filename),
                 Path.Combine(baseDir, "src", "BMachine.UI", "Assets", "Avatars", filename)
             };
             
             // If this is the FIRST file, perform a search if not found
             if (i == 1 && !candidatePaths.Any(File.Exists))
             {
                 // Heavy search only once
                 try 
                 {
                     var foundFiles = Directory.GetFiles(baseDir, "avatar_01.png", SearchOption.AllDirectories);
                     if (foundFiles.Length > 0)
                     {
                         var dir = Path.GetDirectoryName(foundFiles[0]);
                         if (dir != null) DebugStatus += $"Found avatars in: {dir}\n";
                     }
                 }
                 catch {}
             }
             
             foreach(var p in candidatePaths)
             {
                 if (File.Exists(p))
                 {
                     try
                     {
                         using var stream = File.OpenRead(p);
                         var bitmap = new Bitmap(stream);
                         Presets.Add(new AvatarItem { Source = $"preset:{filename}", Image = bitmap });
                         loaded = true;
                         filesFound++;
                         break;
                     }
                     catch(Exception ex) { _debugStatus += $"Err file {p}: {ex.Message}\n"; }
                 }
             }
             
             if (!loaded) _debugStatus += $"Missing: {filename}\n";
        }
        
        if (filesFound == 0)
        {
            _debugStatus += $"Checked BaseDir: {AppDomain.CurrentDomain.BaseDirectory}\n";
            _debugStatus += "No avatars found in Assets/Avatars or via avares://.\n";
            // Show Debug log
        }
    }

    [RelayCommand]
    private void SelectAvatar(AvatarItem item)
    {
        SelectedAvatar = item;
        PreviewImage = item.Image;
        SelectedSource = item.Source;
    }

    [RelayCommand]
    private async Task UploadPhoto()
    {
        if (PickCustomImageFunc == null) return;
        var path = await PickCustomImageFunc();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var bitmap = new Bitmap(stream);
                PreviewImage = bitmap;
                SelectedSource = $"custom:{path}";
                SelectedAvatar = null; // Clear preset selection
            }
            catch {}
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!string.IsNullOrEmpty(SelectedSource))
        {
            OnAvatarSelected?.Invoke(SelectedSource);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }
}

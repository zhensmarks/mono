using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

    public class ManualPsdButton
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ShortcutKey { get; set; } = "";
        public string ColorHex { get; set; } = "#6366F1";
    }

public partial class PsdBucinViewModel : ObservableObject
{
    [ObservableProperty]
    private string _masterDirectory = string.Empty;

    [ObservableProperty]
    private string _photoDirectory = string.Empty;

    [ObservableProperty]
    private string _statusText = "Menunggu Folder...";

    [ObservableProperty]
    private bool _canStart = false;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private int _progressMaximum = 100;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isManualModeActive = false;

    [ObservableProperty]
    private Bitmap? _currentManualImage;

    [ObservableProperty]
    private string _manualProgressText = "";

    [ObservableProperty]
    private string _manualFilename = "";

    [ObservableProperty]
    private bool _isLoadingImage = false;

    [ObservableProperty]
    private int _imageRotation = 0;

    [ObservableProperty]
    private bool _isTypingMode = false;

    [ObservableProperty]
    private string _searchQuery = "";

    partial void OnIsTypingModeChanged(bool value)
    {
        if (value) OnTypingModeExecuted?.Invoke();
    }

    public ObservableCollection<ManualPsdButton> ManualPsdButtons { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    private List<(string RelPath, string FullPath)> _psdMasters = new();
    private List<(string FullPath, string RelPath)> _jpgPhotos = new();
    
    private int _currentIndex = -1;
    private bool _isCancelled = false;

    // Regular expressions for filename matching logic
    private static readonly Regex OnlyParen = new(@"^\(\s*(\d+)\s*\)$", RegexOptions.Compiled);
    private static readonly Regex SpaceForm = new(@"^(\d+)\s*\(\s*\d+\s*\)(?:\b.*)?$", RegexOptions.Compiled);
    private static readonly Regex TightForm = new(@"^\d+\(\s*(\d+)\s*\)$", RegexOptions.Compiled);

    public PsdBucinViewModel()
    {
    }

    partial void OnMasterDirectoryChanged(string value) => ValidateSetup();
    partial void OnPhotoDirectoryChanged(string value) => ValidateSetup();

    private void ValidateSetup()
    {
        if (string.IsNullOrWhiteSpace(MasterDirectory) && string.IsNullOrWhiteSpace(PhotoDirectory))
        {
            StatusText = "Menunggu Folder...";
            CanStart = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(MasterDirectory) || string.IsNullOrWhiteSpace(PhotoDirectory))
        {
            StatusText = "Pilih kedua folder terlebih dahulu";
            CanStart = false;
            return;
        }

        try
        {
            int psdCount = 0;
            if (Directory.Exists(MasterDirectory))
            {
                psdCount = Directory.EnumerateFiles(MasterDirectory, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
            }

            int jpgCount = 0;
            if (Directory.Exists(PhotoDirectory))
            {
                jpgCount = Directory.EnumerateFiles(PhotoDirectory, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            }

            if (psdCount == 0)
            {
                StatusText = "⚠️ Tidak ada PSD di folder Master";
                CanStart = false;
            }
            else if (jpgCount == 0)
            {
                StatusText = "⚠️ Tidak ada Foto di folder";
                CanStart = false;
            }
            else
            {
                StatusText = $"Siap: {psdCount} Template • {jpgCount} Foto";
                CanStart = true;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            CanStart = false;
        }
    }

    [RelayCommand]
    public void ClearPaths()
    {
        MasterDirectory = string.Empty;
        PhotoDirectory = string.Empty;
        StatusText = "Menunggu Folder...";
        CanStart = false;
    }

    private void PrepareData()
    {
        _psdMasters.Clear();
        if (Directory.Exists(MasterDirectory))
        {
            var files = Directory.EnumerateFiles(MasterDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
            
            foreach (var f in files)
            {
                var relPath = Path.GetRelativePath(MasterDirectory, f);
                _psdMasters.Add((relPath, f));
            }
        }
        _psdMasters = _psdMasters.OrderBy(x => x.RelPath.ToLower()).ToList();

        _jpgPhotos.Clear();
        if (Directory.Exists(PhotoDirectory))
        {
            var files = Directory.EnumerateFiles(PhotoDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
            
            foreach (var f in files)
            {
                var relPath = Path.GetRelativePath(PhotoDirectory, f);
                _jpgPhotos.Add((f, relPath));
            }
        }
        _jpgPhotos = _jpgPhotos.OrderBy(x => x.RelPath.ToLower()).ToList();
    }

    public string ComputeTargetName(string jpgName)
    {
        var n = Path.GetFileNameWithoutExtension(jpgName).Trim();
        var m = OnlyParen.Match(n);
        if (m.Success) return m.Groups[1].Value;
        
        m = SpaceForm.Match(n);
        if (m.Success) return m.Groups[1].Value;
        
        m = TightForm.Match(n);
        if (m.Success) return m.Groups[1].Value;
        
        return n;
    }

    // StartAutoAsync was removed because we are switching to manual mode only

    [RelayCommand]
    public void StartManualViewer()
    {
        if (string.IsNullOrWhiteSpace(MasterDirectory) || string.IsNullOrWhiteSpace(PhotoDirectory))
        {
            StatusText = "Pilih kedua folder terlebih dahulu";
            return;
        }

        PrepareData();
        if (_jpgPhotos.Count == 0 || _psdMasters.Count == 0)
        {
            StatusText = "Folder kosong!";
            return;
        }

        IsManualModeActive = true;
        _isCancelled = false;
        _currentIndex = -1;
        Logs.Clear();
        
        ManualPsdButtons.Clear();
        string[] colors = { "#4F46E5", "#16A34A", "#D97706", "#9333EA", "#E11D48" };
        
        for (int i = 0; i < _psdMasters.Count; i++)
        {
            var m = _psdMasters[i];
            string key = (i + 1) <= 9 ? (i + 1).ToString() : "?";
            ManualPsdButtons.Add(new ManualPsdButton
            {
                Name = Path.GetFileNameWithoutExtension(m.RelPath),
                Path = m.FullPath,
                ShortcutKey = key,
                ColorHex = colors[i % colors.Length]
            });
        }

        LoadNextImage();
        if (IsTypingMode) OnTypingModeExecuted?.Invoke();
    }

    private void LoadNextImage()
    {
        if (_isCancelled) return;
        
        _currentIndex++;
        if (_currentIndex >= _jpgPhotos.Count)
        {
            ExitManualMode();
            StatusText = "Selesai memproses semua foto.";
            return;
        }

        var (fullJpg, relJpg) = _jpgPhotos[_currentIndex];
        ManualProgressText = $"{_currentIndex + 1} / {_jpgPhotos.Count}";
        ManualFilename = relJpg;

        IsLoadingImage = true;
        Task.Run(() => 
        {
            try
            {
                // We use memory stream so we don't lock the file
                using var fs = File.OpenRead(fullJpg);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                var bmp = new Bitmap(ms);
                int rotation = bmp.Size.Width > bmp.Size.Height ? 270 : 0;

                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    ImageRotation = rotation;
                    var oldBmp = CurrentManualImage;
                    CurrentManualImage = bmp;
                    oldBmp?.Dispose();
                    IsLoadingImage = false;
                });
            }
            catch (Exception)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    ImageRotation = 0;
                    CurrentManualImage = null;
                    IsLoadingImage = false;
                });
            }
        });
    }

    [RelayCommand]
    public void SkipManual()
    {
        if (!IsManualModeActive || IsProcessing) return;
        Logs.Add($"[SKIP] {_jpgPhotos[_currentIndex].RelPath}");
        LoadNextImage();
        if (IsTypingMode) OnTypingModeExecuted?.Invoke();
    }

    [RelayCommand]
    public void ExitManualMode()
    {
        IsManualModeActive = false;
        _isCancelled = true;
        var old = CurrentManualImage;
        CurrentManualImage = null;
        old?.Dispose();
    }

    public void HandleShortcut(string key)
    {
        if (!IsManualModeActive || IsProcessing) return;

        if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
        {
            Logs.Add($"[SKIP] {_jpgPhotos[_currentIndex].RelPath}");
            LoadNextImage();
            return;
        }

        var btn = ManualPsdButtons.FirstOrDefault(x => x.ShortcutKey == key);
        if (btn != null)
        {
            _ = ProcessSingleManualAsync(btn.Path);
        }
    }

    [RelayCommand]
    public async Task ExecuteTypingMode()
    {
        if (!IsManualModeActive || IsProcessing || string.IsNullOrWhiteSpace(SearchQuery)) return;
        
        var q = SearchQuery.Trim();
        var match = ManualPsdButtons.FirstOrDefault(x => 
            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || 
            x.ShortcutKey.Equals(q, StringComparison.OrdinalIgnoreCase));
            
        if (match != null)
        {
            SearchQuery = "";
            await ProcessSingleManualAsync(match.Path);
            if (IsTypingMode) OnTypingModeExecuted?.Invoke();
        }
    }

    public event Action? OnTypingModeExecuted;

    [RelayCommand]
    public async Task ProcessSingleManualAsync(string masterPath)
    {
        if (!IsManualModeActive || IsProcessing) return;

        IsProcessing = true;
        var (fullJpg, relJpg) = _jpgPhotos[_currentIndex];
        var masterName = Path.GetFileNameWithoutExtension(masterPath);

        await Task.Run(() =>
        {
            try
            {
                var tname = ComputeTargetName(Path.GetFileName(relJpg));
                var ext = Path.GetExtension(masterPath);
                var dst = Path.Combine(MasterDirectory, $"{tname}{ext}");

                if (File.Exists(dst))
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Logs.Add($"[EXIST] {relJpg}"));
                }
                else
                {
                    File.Copy(masterPath, dst);
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Logs.Add($"[OK] {relJpg} -> {masterName}"));
                }
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Logs.Add($"[FAIL] {relJpg} -> {ex.Message}"));
            }
        });

        IsProcessing = false;
        LoadNextImage();
    }

    [RelayCommand]
    public async Task ProcessManualFilesAsync(IEnumerable<string> droppedFiles)
    {
        if (string.IsNullOrWhiteSpace(MasterDirectory) || !Directory.Exists(MasterDirectory))
        {
            StatusText = "Pilih Folder Master PSD terlebih dahulu!";
            return;
        }

        var masterFiles = Directory.EnumerateFiles(MasterDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (masterFiles.Count == 0)
        {
            StatusText = "Tidak ada file PSD di folder Master!";
            return;
        }

        IsProcessing = true;
        _isCancelled = false;
        Logs.Clear();
        
        var validFiles = droppedFiles.Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                 f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).ToList();

        ProgressMaximum = validFiles.Count;
        ProgressValue = 0;

        string fallbackPsd = masterFiles[0];

        await Task.Run(() =>
        {
            for (int i = 0; i < validFiles.Count; i++)
            {
                if (_isCancelled) break;
                var file = validFiles[i];

                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    ProgressValue = i + 1;
                    ProgressText = $"Manual: {Path.GetFileName(file)}";
                });

                try
                {
                    var tname = ComputeTargetName(Path.GetFileName(file));
                    var tdir = Path.GetDirectoryName(file) ?? "";
                    var ext = Path.GetExtension(fallbackPsd);
                    var dst = Path.Combine(tdir, $"{tname}{ext}");

                    if (!File.Exists(dst))
                    {
                        File.Copy(fallbackPsd, dst);
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Logs.Add($"[OK] {tname}"));
                    }
                }
                catch {}
            }
        });

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
        {
            ProgressText = "Selesai (Manual)";
            IsProcessing = false;
            OnAutoProcessCompleted?.Invoke();
        });
    }

    // CancelAuto removed

    public event Action? OnAutoProcessCompleted;
}

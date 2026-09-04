using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class MantraGandaViewModel : ObservableObject
{
    [ObservableProperty]
    private string _masterPath = string.Empty;

    [ObservableProperty]
    private string _dataFolder = string.Empty;

    [ObservableProperty]
    private int _duplicateCount = 10;

    [ObservableProperty]
    private int _startNumber = 1;

    [ObservableProperty]
    private string _separator = " ";

    [ObservableProperty]
    private string _customBaseName = string.Empty;

    [ObservableProperty]
    private string _actionButtonText = "Gandakan Sekarang";

    [ObservableProperty]
    private bool _isProcessing = false;

    private bool _isCancelled = false;

    [ObservableProperty]
    private string _statusText = "Siap untuk menggandakan";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private int _progressMaximum = 100;

    [ObservableProperty]
    private bool _isManualConfigVisible = true;

    public ObservableCollection<string> Logs { get; } = new();

    partial void OnMasterPathChanged(string value) => CheckState();
    partial void OnDataFolderChanged(string value) => CheckState();
    partial void OnDuplicateCountChanged(int value) => CheckState();

    private void CheckState()
    {
        if (string.IsNullOrWhiteSpace(MasterPath))
        {
            StatusText = "Pilih file/folder sumber terlebih dahulu";
            ActionButtonText = "Gandakan Sekarang";
            IsManualConfigVisible = string.IsNullOrWhiteSpace(DataFolder);
        }
        else if (!string.IsNullOrWhiteSpace(DataFolder))
        {
            StatusText = "Siap menggandakan berdasarkan folder data (Otomatis)";
            ActionButtonText = "Sesuaikan Ganda";
            IsManualConfigVisible = false;
        }
        else if (DuplicateCount <= 0)
        {
            StatusText = "Jumlah duplikat harus lebih dari 0";
            ActionButtonText = "Gandakan Sekarang";
            IsManualConfigVisible = true;
        }
        else
        {
            StatusText = $"Siap membuat {DuplicateCount} duplikat manual";
            ActionButtonText = "Gandakan Sekarang";
            IsManualConfigVisible = true;
        }
    }

    [RelayCommand]
    public void ClearPaths()
    {
        MasterPath = string.Empty;
        DataFolder = string.Empty;
        StatusText = "Pilih file/folder sumber terlebih dahulu";
    }

    [RelayCommand]
    public void StopDuplicate()
    {
        _isCancelled = true;
        StatusText = "Membatalkan...";
    }

    private static readonly Regex OnlyParen = new Regex(@"^\(\s*(\d+)\s*\)$", RegexOptions.Compiled);
    private static readonly Regex SpaceForm = new Regex(@"^(\d+)\s*\(\s*\d+\s*\)(?:\b.*)?$", RegexOptions.Compiled);
    private static readonly Regex TightForm = new Regex(@"^\d+\(\s*(\d+)\s*\)$", RegexOptions.Compiled);

    private string ComputeTargetName(string name)
    {
        name = name.Trim();
        var m = OnlyParen.Match(name);
        if (m.Success) return m.Groups[1].Value;
        m = SpaceForm.Match(name);
        if (m.Success) return m.Groups[1].Value;
        m = TightForm.Match(name);
        if (m.Success) return m.Groups[1].Value;
        return name;
    }

    [RelayCommand]
    public async Task ExecuteDuplicateAsync()
    {
        if (string.IsNullOrWhiteSpace(MasterPath) || DuplicateCount <= 0 || IsProcessing) return;

        IsProcessing = true;
        _isCancelled = false;
        Logs.Clear();
        ProgressMaximum = DuplicateCount;
        ProgressValue = 0;

        await Task.Run(() =>
        {
            try
            {
                bool isMasterDir = Directory.Exists(MasterPath);
                
                if (!string.IsNullOrWhiteSpace(DataFolder) && Directory.Exists(DataFolder))
                {
                    var dataFiles = Directory.GetFiles(DataFolder, "*.*", SearchOption.AllDirectories)
                                             .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                         f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                                                         f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                             .ToArray();
                    
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ProgressMaximum = dataFiles.Length);

                    if (isMasterDir)
                    {
                        var masterFiles = Directory.GetFiles(MasterPath, "*.*", SearchOption.AllDirectories)
                                                   .Where(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || 
                                                               f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
                                                   .ToArray();
                                                   
                        var psdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var mf in masterFiles)
                        {
                            var relDir = Path.GetDirectoryName(Path.GetRelativePath(MasterPath, mf))?.Replace('\\', '/').Trim() ?? "";
                            if (!psdMap.ContainsKey(relDir))
                                psdMap[relDir] = mf;
                        }
                        
                        string? fallbackPsd = masterFiles.FirstOrDefault();

                        for (int i = 0; i < dataFiles.Length; i++)
                        {
                            if (_isCancelled) break;

                            var df = dataFiles[i];
                            var relPath = Path.GetRelativePath(DataFolder, df);
                            var relDir = Path.GetDirectoryName(relPath)?.Replace('\\', '/').Trim() ?? "";
                            
                            string? selectedMaster = psdMap.TryGetValue(relDir, out var mapped) ? mapped : fallbackPsd;
                            
                            if (selectedMaster == null)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    Logs.Add($"[FAIL] Master PSD tidak ditemukan untuk {relPath}");
                                    ProgressValue = i + 1;
                                });
                                continue;
                            }
                            
                            var dataName = Path.GetFileNameWithoutExtension(df);
                            var targetName = ComputeTargetName(dataName);
                            var masterExt = Path.GetExtension(selectedMaster);
                            var targetDir = Path.Combine(MasterPath, Path.GetDirectoryName(relPath) ?? "");
                            
                            Directory.CreateDirectory(targetDir);
                            var targetPath = Path.Combine(targetDir, $"{targetName}{masterExt}");
                            
                            if (!File.Exists(targetPath))
                            {
                                File.Copy(selectedMaster, targetPath);
                                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    Logs.Add($"[OK] Dibuat: {Path.GetFileName(targetPath)}");
                                    ProgressValue = i + 1;
                                });
                            }
                            else
                            {
                                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    Logs.Add($"[SKIP] {Path.GetFileName(targetPath)} sudah ada");
                                    ProgressValue = i + 1;
                                });
                            }
                        }
                    }
                    else
                    {
                        var directory = Path.GetDirectoryName(MasterPath) ?? "";
                        var extension = Path.GetExtension(MasterPath);

                        for (int i = 0; i < dataFiles.Length; i++)
                        {
                            if (_isCancelled) break;

                            var df = dataFiles[i];
                            var dataName = Path.GetFileNameWithoutExtension(df);
                            var targetName = ComputeTargetName(dataName);
                            
                            var newFileName = $"{targetName}{extension}";
                            var newPath = Path.Combine(directory, newFileName);

                            if (!File.Exists(newPath))
                            {
                                File.Copy(MasterPath, newPath);
                                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    Logs.Add($"[OK] Dibuat: {newFileName}");
                                    ProgressValue = i + 1;
                                });
                            }
                            else
                            {
                                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    Logs.Add($"[SKIP] {newFileName} sudah ada");
                                    ProgressValue = i + 1;
                                });
                            }
                        }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Selesai. {dataFiles.Length} file diproses dari folder data.");
                }
                else
                {
                    if (isMasterDir)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = "Mode duplikat manual memerlukan single master file, bukan folder.");
                        return;
                    }
                    
                    var directory = Path.GetDirectoryName(MasterPath) ?? "";
                    var originalFileNameWithoutExt = Path.GetFileNameWithoutExtension(MasterPath);
                    var extension = Path.GetExtension(MasterPath);
                    
                    var baseNameToUse = string.IsNullOrWhiteSpace(CustomBaseName) ? originalFileNameWithoutExt : CustomBaseName;
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ProgressMaximum = DuplicateCount);

                    for (int i = 0; i < DuplicateCount; i++)
                    {
                        if (_isCancelled) break;

                        var currentNumber = StartNumber + i;
                        var newFileName = $"{baseNameToUse}{Separator}{currentNumber}{extension}";
                        var newPath = Path.Combine(directory, newFileName);

                        if (!File.Exists(newPath))
                        {
                            File.Copy(MasterPath, newPath);
                            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                            {
                                Logs.Add($"[OK] Dibuat: {newFileName}");
                                ProgressValue = i + 1;
                            });
                        }
                        else
                        {
                            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                            {
                                Logs.Add($"[SKIP] {newFileName} sudah ada");
                                ProgressValue = i + 1;
                            });
                        }
                    }
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Selesai. {DuplicateCount} file manual diproses.");
                }
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
            }
        });

        IsProcessing = false;
    }
}

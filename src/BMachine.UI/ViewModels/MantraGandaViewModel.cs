using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class MantraGandaViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceFile = string.Empty;

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

    public ObservableCollection<string> Logs { get; } = new();

    partial void OnSourceFileChanged(string value) => CheckState();
    partial void OnDataFolderChanged(string value) => CheckState();
    partial void OnDuplicateCountChanged(int value) => CheckState();

    private void CheckState()
    {
        if (string.IsNullOrWhiteSpace(SourceFile))
        {
            StatusText = "Pilih file sumber terlebih dahulu";
            ActionButtonText = "Gandakan Sekarang";
        }
        else if (!string.IsNullOrWhiteSpace(DataFolder))
        {
            StatusText = "Siap menggandakan berdasarkan folder data (Otomatis)";
            ActionButtonText = "Sesuaikan Ganda";
        }
        else if (DuplicateCount <= 0)
        {
            StatusText = "Jumlah duplikat harus lebih dari 0";
            ActionButtonText = "Gandakan Sekarang";
        }
        else
        {
            StatusText = $"Siap membuat {DuplicateCount} duplikat manual";
            ActionButtonText = "Gandakan Sekarang";
        }
    }

    [RelayCommand]
    public void StopDuplicate()
    {
        _isCancelled = true;
        StatusText = "Membatalkan...";
    }

    [RelayCommand]
    public async Task ExecuteDuplicateAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFile) || DuplicateCount <= 0 || IsProcessing) return;

        IsProcessing = true;
        _isCancelled = false;
        Logs.Clear();
        ProgressMaximum = DuplicateCount;
        ProgressValue = 0;

        await Task.Run(() =>
        {
            try
            {
                var directory = Path.GetDirectoryName(SourceFile) ?? "";
                var originalFileNameWithoutExt = Path.GetFileNameWithoutExtension(SourceFile);
                var extension = Path.GetExtension(SourceFile);

                if (!string.IsNullOrWhiteSpace(DataFolder) && Directory.Exists(DataFolder))
                {
                    var dataFiles = Directory.GetFiles(DataFolder, "*.*", SearchOption.AllDirectories);
                    
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ProgressMaximum = dataFiles.Length);

                    for (int i = 0; i < dataFiles.Length; i++)
                    {
                        if (_isCancelled) break;

                        var df = dataFiles[i];
                        var dataName = Path.GetFileNameWithoutExtension(df);
                        
                        // We do the logic of checking parent folder name if it's like PSD Bucin?
                        // "seperti bucin yang tombol otomatis sebelumnya"
                        // But wait, the user just said "sesuai yang saya isi atau saya ingin kan". For data files, just use their name.
                        var newFileName = $"{dataName}{extension}";
                        var newPath = Path.Combine(directory, newFileName);

                        if (!File.Exists(newPath))
                        {
                            File.Copy(SourceFile, newPath);
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
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Selesai. {dataFiles.Length} file diproses dari folder data.");
                }
                else
                {
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
                            File.Copy(SourceFile, newPath);
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

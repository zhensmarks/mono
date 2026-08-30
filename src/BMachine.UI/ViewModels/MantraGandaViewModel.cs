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
    private int _duplicateCount = 10;

    [ObservableProperty]
    private int _startNumber = 1;

    [ObservableProperty]
    private string _separator = " ";

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _statusText = "Siap untuk menggandakan";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private int _progressMaximum = 100;

    public ObservableCollection<string> Logs { get; } = new();

    partial void OnSourceFileChanged(string value) => CheckState();
    partial void OnDuplicateCountChanged(int value) => CheckState();

    private void CheckState()
    {
        if (string.IsNullOrWhiteSpace(SourceFile))
        {
            StatusText = "Pilih file sumber terlebih dahulu";
        }
        else if (DuplicateCount <= 0)
        {
            StatusText = "Jumlah duplikat harus lebih dari 0";
        }
        else
        {
            StatusText = $"Siap membuat {DuplicateCount} duplikat";
        }
    }

    [RelayCommand]
    public async Task ExecuteDuplicateAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFile) || DuplicateCount <= 0 || IsProcessing) return;

        IsProcessing = true;
        Logs.Clear();
        ProgressMaximum = DuplicateCount;
        ProgressValue = 0;

        await Task.Run(() =>
        {
            try
            {
                var directory = Path.GetDirectoryName(SourceFile) ?? "";
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(SourceFile);
                var extension = Path.GetExtension(SourceFile);

                for (int i = 0; i < DuplicateCount; i++)
                {
                    var currentNumber = StartNumber + i;
                    var newFileName = $"{fileNameWithoutExt}{Separator}{currentNumber}{extension}";
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
                
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Selesai. {DuplicateCount} file diproses.");
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
            }
        });

        IsProcessing = false;
    }
}

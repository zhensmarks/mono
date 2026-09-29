using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using PixelcutCompact.Models;
using System.Linq;

namespace PixelcutCompact.ViewModels;

public partial class ProcessTabViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty] private string _title = "Baru";
    [ObservableProperty] private ObservableCollection<PixelcutFileItem> _files = new();
    
    // Tab States
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isWaiting;
    [ObservableProperty] private bool _isDone;
    
    public bool HasFiles => Files.Count > 0;
    
    // UI Helpers for Header (Option C)
    public int FilesCount => Files.Count;
    public int ProcessedCount => Files.Count(x => x.IsDone || x.IsFailed);
    
    public double ProgressPercentage => FilesCount == 0 ? 0 : (double)ProcessedCount / FilesCount * 100;
    // Sudut sapuan untuk arc ring di header tab (0..100% -> 0..360 derajat).
    // 359.9 di 100% karena Arc dengan sweep persis 360 bisa render kosong.
    public double ProgressSweep => ProgressPercentage >= 100 ? 359.9 : ProgressPercentage * 3.6;
    public string ProgressText => $"{ProcessedCount}/{FilesCount} Selesai";

    public ProcessTabViewModel()
    {
        Files.CollectionChanged += (s, e) => 
        {
            OnPropertyChanged(nameof(FilesCount));
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(ProgressSweep));
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(ProgressText));

            // BUG FIX: status tab tidak boleh nanggung ke list baru.
            // Saat list dikosongkan, reset semua status tab.
            if (Files.Count == 0)
            {
                IsProcessing = false;
                IsWaiting = false;
                IsDone = false;
            }
            // File baru masuk ke tab yang sudah ditandai selesai (mis. habis Clear
            // lalu import ulang) => anggap belum selesai karena isinya list baru.
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                     && IsDone && !IsProcessing)
            {
                IsDone = false;
            }

            NotifyProgressChanged();
        };
    }

    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProcessedCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressSweep));
        OnPropertyChanged(nameof(ProgressText));
    }
}

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
    public string ProgressText => $"{ProcessedCount}/{FilesCount} Selesai";

    public ProcessTabViewModel()
    {
        Files.CollectionChanged += (s, e) => 
        {
            OnPropertyChanged(nameof(FilesCount));
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(ProgressText));
        };
    }

    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProcessedCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressText));
    }
}

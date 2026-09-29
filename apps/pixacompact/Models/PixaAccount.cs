using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PixelcutCompact.Models;

public partial class PixaAccount : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _lastCredits = "Pending";
    [ObservableProperty] private DateTime? _lastChecked;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}

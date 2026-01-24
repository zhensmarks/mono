using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.ViewModels;

public partial class SplashViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "Memuat...";
    [ObservableProperty] private double _progress = 0;
}

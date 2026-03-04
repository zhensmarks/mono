using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrelloCompact.Services;

namespace TrelloCompact.ViewModels;

public partial class SetupViewModel : ViewModelBase
{
    private readonly TrelloApiService _api;
    private readonly SettingsService _settings;
    private readonly MainWindowViewModel _mainVm;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _token = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isLoading;

    public SetupViewModel(TrelloApiService api, SettingsService settings, MainWindowViewModel mainVm)
    {
        _api = api;
        _settings = settings;
        _mainVm = mainVm;
    }

    [RelayCommand]
    private void OpenApiKeyUrl()
    {
        var url = "https://trello.com/power-ups/admin";
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        } catch { }
    }

    [RelayCommand]
    private void OpenTokenUrl()
    {
        if (string.IsNullOrEmpty(ApiKey)) {
            ErrorMessage = "Please enter API Key first to generate Token URL";
            return;
        }
        var url = $"https://trello.com/1/authorize?expiration=30days&name=TrelloCompact&scope=read,write&response_type=token&key={ApiKey}";
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        } catch { }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Token))
        {
            ErrorMessage = "API Key and Token are required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = "";

        try
        {
            var isValid = await _api.TestConnectionAsync(ApiKey, Token);
            if (isValid)
            {
                var cfg = _settings.Load();
                cfg.TrelloApiKey = ApiKey;
                cfg.TrelloToken = Token;
                _settings.Save(cfg);
                
                _mainVm.FinishSetupAndStart();
            }
            else
            {
                ErrorMessage = "Connection failed. Please check your Key and Token.";
            }
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        
        IsLoading = false;
    }
}

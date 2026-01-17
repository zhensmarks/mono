using System.Collections.ObjectModel;
using BMachine.SDK;
using BMachine.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class LateCardListViewModel : BaseTrelloListViewModel
{
    public LateCardListViewModel(IDatabase database, INotificationService? notificationService = null)
        : base(database, notificationService)
    {
         _ = LoadAccentColor();
    }

    protected override string ColorSettingKey => "Settings.Color.Late";
    
    // Design-time constructor
    public LateCardListViewModel() : base()
    {
        Cards = new ObservableCollection<TrelloCard>
        {
            new TrelloCard { Name = "Late Mockup", Description = "Sample Late Card", LabelsText = "LATE", IsExpanded = true }
        };
    }

    // Timer for Auto-Refresh
    private Avalonia.Threading.DispatcherTimer? _timer;

    public void StartAutoRefresh()
    {
        RefreshCommand.Execute(null);
        if (_timer == null)
        {
            _timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) => {
                if (!IsRefreshing) RefreshCommand.Execute(null);
            };
        }
        _timer.Start();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        bool isFirstLoad = Cards.Count == 0;
        
        if (isFirstLoad) 
        {
             IsRefreshing = true;
             StatusMessage = "Memuat data...";
        }
        
        try 
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            var listId = await _database.GetAsync<string>("Trello.LateListId");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(listId))
            {
                StatusMessage = "Config missing.";
                IsRefreshing = false;
                return;
            }

            var cards = await FetchCards(listId, apiKey, token);
            UpdateCardsCollection(cards);
            
            StatusMessage = $"Dimuat {Cards.Count} card";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}

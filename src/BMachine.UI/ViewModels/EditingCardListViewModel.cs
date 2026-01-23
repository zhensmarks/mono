using System.Collections.ObjectModel;
using BMachine.SDK;
using BMachine.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public partial class EditingCardListViewModel : BaseTrelloListViewModel
{
    public EditingCardListViewModel(IDatabase database, INotificationService? notificationService = null)
        : base(database, notificationService)
    {
         _ = LoadAccentColor();
    }

    protected override void CloseAllSidePanels()
    {
        base.CloseAllSidePanels();
        IsAddManualPanelOpen = false;
    }

    protected override string ColorSettingKey => "Settings.Color.Editing";
    
    // Design-time constructor
    public EditingCardListViewModel() : base()
    {
        Cards = new ObservableCollection<TrelloCard>
        {
            new TrelloCard { Name = "Design Mockup", Description = "Sample Description", LabelsText = "URGENT", IsExpanded = true },
            new TrelloCard { Name = "Implement API", DueDate = DateTime.Now.AddDays(1) }
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

    // Cache manual cards to prevent flickering if fetch fails momentarily
    private List<TrelloCard>? _cachedManualCards;

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
            var listId = await _database.GetAsync<string>("Trello.EditingListId");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(listId))
            {
                StatusMessage = "Config missing.";
                IsRefreshing = false;
                return;
            }

            // 1. Fetch List Cards
            var listCards = await FetchCards(listId, apiKey, token);

            // 2. Fetch Manual Cards
            var manualIds = await _database.GetAsync<List<string>>("ManualCards.Editing") ?? new List<string>();
            var manualCards = new List<TrelloCard>();
            
            // Optimization: If we have cached cards, check if we really need to re-fetch all?
            // For now, always try fetch, but fallback to cache if fetch fails for specific ID?
            
            foreach (var mid in manualIds)
            {
                var mc = await FetchSingleCard(mid, apiKey, token);
                if (mc != null) 
                {
                    mc.IsManual = true;
                    manualCards.Add(mc);
                }
                else
                {
                    // Fetch failed (maybe deleted or network error).
                    // If network error, we might want to keep the old one?
                    // For now, if we have a cached version, use it but mark as 'Offline'?
                    if (_cachedManualCards != null)
                    {
                        var old = _cachedManualCards.FirstOrDefault(x => x.Id == mid);
                        if (old != null) manualCards.Add(old); // Keep existing
                    }
                }
            }
            
            _cachedManualCards = manualCards;

            // 3. Merge
            var allCards = new List<TrelloCard>();
            allCards.AddRange(listCards);
            
            // Avoid duplicates if card is already in list
            foreach(var mc in manualCards)
            {
                if (!allCards.Any(c => c.Id == mc.Id)) allCards.Add(mc);
            }

            UpdateCardsCollection(allCards);
            
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

    // --- Manual Card Logic ---

    [ObservableProperty] private bool _isAddManualPanelOpen;
    [ObservableProperty] private string _manualCardLinkInput = "";
    [ObservableProperty] private bool _isLoadingManualAdd;

    [RelayCommand]
    private void OpenAddManualPanel()
    {
        CloseAllSidePanels(); // Close check/move/comments/attachments
        IsAddManualPanelOpen = true;
        ManualCardLinkInput = "";
    }

    [RelayCommand]
    private void CloseAddManualPanel()
    {
        IsAddManualPanelOpen = false;
        ManualCardLinkInput = "";
    }

    [RelayCommand]
    private async Task ConfirmAddManualCard()
    {
        if (string.IsNullOrWhiteSpace(ManualCardLinkInput)) return;

        IsLoadingManualAdd = true;
        int addedCount = 0;
        int failedCount = 0;
        
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            // Split by newlines and process each link
            var lines = ManualCardLinkInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var manualIds = await _database.GetAsync<List<string>>("ManualCards.Editing") ?? new List<string>();
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;
                
                // Parse ID from Link
                string cardId = ParseCardIdFromLink(trimmedLine);
                
                if (string.IsNullOrEmpty(cardId))
                {
                    failedCount++;
                    continue;
                }
                
                // Skip if already added
                if (manualIds.Contains(cardId)) continue;
                
                // Verify card exists
                var card = await FetchSingleCard(cardId, apiKey, token);
                if (card != null)
                {
                    manualIds.Add(card.Id);
                    addedCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            
            // Save all at once
            if (addedCount > 0)
            {
                await _database.SetAsync("ManualCards.Editing", manualIds);
            }
            
            CloseAddManualPanel();
            await Refresh();
            
            if (failedCount > 0)
            {
                StatusMessage = $"Ditambahkan {addedCount} card, {failedCount} gagal.";
            }
            else
            {
                StatusMessage = $"Ditambahkan {addedCount} card.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingManualAdd = false;
        }
    }
    
    private string ParseCardIdFromLink(string input)
    {
        // Trello Link formats: https://trello.com/c/CARD_ID/slug?query...
        var parts = input.Split('/');
        
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "c" && i + 1 < parts.Length)
            {
                return parts[i + 1];
            }
        }
        
        // Fallback: assume input IS the ID if short and alphanumeric
        if (input.Length < 30 && !input.Contains("http"))
        {
            return input;
        }
        
        return "";
    }

    [RelayCommand]
    private async Task RemoveManualCard(TrelloCard card)
    {
        if (card == null || !card.IsManual) return;
        
        try
        {
            var manualIds = await _database.GetAsync<List<string>>("ManualCards.Editing") ?? new List<string>();
            if (manualIds.Contains(card.Id))
            {
                manualIds.Remove(card.Id);
                await _database.SetAsync("ManualCards.Editing", manualIds);
                Cards.Remove(card);
                StatusMessage = "Manual card removed.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error removing card: {ex.Message}";
        }
    }

    private async Task<TrelloCard?> FetchSingleCard(string cardId, string apiKey, string token)
    {
        using var client = new HttpClient();
        // Added checklists=all to fetch checklist details
        var url = $"https://api.trello.com/1/cards/{cardId}?key={apiKey}&token={token}&fields=name,desc,due,labels,idMembers,badges&checklists=all";
        
        try 
        {
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var element = doc.RootElement;
            
            var card = new TrelloCard
            {
                Id = element.GetProperty("id").GetString() ?? "",
                Name = element.GetProperty("name").GetString() ?? "",
                Description = element.GetProperty("desc").GetString() ?? ""
            };
            
            if (element.TryGetProperty("due", out var dueProp) && dueProp.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                if (DateTime.TryParse(dueProp.GetString(), out var dt))
                {
                    card.DueDate = dt;
                    card.IsOverdue = dt < DateTime.Now; 
                }
            }
            
            if (element.TryGetProperty("labels", out var labelsProp))
            {
                var lbls = new List<string>();
                foreach (var l in labelsProp.EnumerateArray())
                {
                        if(l.TryGetProperty("name", out var n)) lbls.Add(n.GetString() ?? "");
                }
                card.LabelsText = string.Join(", ", lbls.Where(x => !string.IsNullOrEmpty(x)));
            }

            // Checklists Parsing
            if (element.TryGetProperty("checklists", out var checklistsProp))
            {
                var names = new List<string>();
                int total = 0;
                int completed = 0;

                foreach(var cl in checklistsProp.EnumerateArray())
                {
                    if (cl.TryGetProperty("name", out var clName)) names.Add(clName.GetString() ?? "");
                    
                    if (cl.TryGetProperty("checkItems", out var items))
                    {
                        foreach(var item in items.EnumerateArray())
                        {
                            total++;
                            if (item.TryGetProperty("state", out var state) && state.GetString() == "complete")
                            {
                                completed++;
                            }
                        }
                    }
                }

                card.ChecklistNames = names;
                card.ChecklistTotal = total;
                card.ChecklistCompleted = completed;
                card.HasChecklist = total > 0;
            }
            // Fallback to badges if checklists missing (shouldn't happen with checklists=all)
            else if (element.TryGetProperty("badges", out var badges))
            {
                if (badges.TryGetProperty("attachments", out var att)) card.AttachmentCount = att.GetInt32();
                if (badges.TryGetProperty("checkItems", out var checkItems) && checkItems.GetInt32() > 0) card.HasChecklist = true;
            }

            // Ensure badges attachments still parsed if outside checklists block
            if (element.TryGetProperty("badges", out var badges2))
            {
                 if (badges2.TryGetProperty("attachments", out var att)) card.AttachmentCount = att.GetInt32();
            }

            card.RefreshChecklistStatus(); // Update Tooltips
            return card;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching card {cardId}: {ex.Message}");
            return null; // Card not found/Error
        }
    }
}

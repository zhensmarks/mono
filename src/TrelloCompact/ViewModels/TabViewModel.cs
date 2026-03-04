using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrelloCompact.Models;
using TrelloCompact.Services;

namespace TrelloCompact.ViewModels;

public partial class TabViewModel : ViewModelBase
{
    private readonly TrelloApiService _api;
    private readonly SettingsService _settings = new();
    private readonly MainWindowViewModel _mainVm;
    private readonly CustomTab _tabConfig;
    private readonly NotificationService _notificationService = new();

    public string TabConfigId => _tabConfig.Id;
    public string ListId => _tabConfig.ListId;

    [ObservableProperty]
    private string _tabName;

    [ObservableProperty]
    private string _accentColor;

    public ObservableCollection<TrelloCard> Cards { get; } = new();

    [ObservableProperty]
    private TrelloCard? _selectedCard;

    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isOnline = true;

    // Panels
    [ObservableProperty] private bool _isDetailPanelOpen;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionVisible))]
    private bool _isCommentsPanelOpen;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionVisible))]
    private bool _isChecklistPanelOpen;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionVisible))]
    private bool _isAttachmentPanelOpen;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionVisible))]
    private bool _isMovePanelOpen;
    
    [ObservableProperty] private bool _isNotepadPanelOpen;

    // Derived visibility: hide description if any sub-panel is open
    public bool IsDescriptionVisible => !(IsCommentsPanelOpen || IsChecklistPanelOpen || IsAttachmentPanelOpen || IsMovePanelOpen);

    // Notepad
    [ObservableProperty] private string _notepadText = "";

    // Batch
    [ObservableProperty] private bool _isBatchMoveMode;
    [ObservableProperty] private bool _hasSelectedCards;
    [ObservableProperty] private bool _canUnlinkSelected;

    // Sub-panel loading (separate from main IsLoading to avoid overlay blocking panels)
    [ObservableProperty] private bool _isSubLoading;

    // Add card via link
    [ObservableProperty] private string _addCardUrl = "";

    public TabViewModel(CustomTab tabConfig, TrelloApiService api, MainWindowViewModel mainVm)
    {
        _tabConfig = tabConfig;
        _api = api;
        _mainVm = mainVm;

        TabName = tabConfig.Name;
        AccentColor = tabConfig.AccentColor;
        
        _ = FetchCardsAsync();
    }

    // ===== FETCH CARDS (with offline cache) =====
    [RelayCommand]
    private async Task FetchCardsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = $"Loading {TabName}...";

        try
        {
            var cfg = _settings.Load();
            var manualList = cfg.ManuallyAddedCards.GetValueOrDefault(_tabConfig.ListId, new List<string>());

            var cards = await _api.GetCardsAsync(_tabConfig.ListId);
            Cards.Clear();
            foreach (var c in cards.OrderBy(x => x.Pos))
            {
                c.IsManuallyAdded = manualList.Contains(c.Id);
                Cards.Add(c);
            }
            
            StatusMessage = $"{Cards.Count} cards";
            IsOnline = true;

            // Cache for offline
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(cards);
                cfg.CachedCards[_tabConfig.ListId] = json;
                _settings.Save(cfg);
            }
            catch { }
        }
        catch (Exception)
        {
            IsOnline = false;
            // Try offline cache
            try
            {
                var cfg = _settings.Load();
                if (cfg.CachedCards.TryGetValue(_tabConfig.ListId, out var cachedJson) && !string.IsNullOrEmpty(cachedJson))
                {
                    var cached = System.Text.Json.JsonSerializer.Deserialize<List<TrelloCard>>(cachedJson);
                    if (cached != null)
                    {
                        Cards.Clear();
                        foreach (var c in cached.OrderBy(x => x.Pos))
                            Cards.Add(c);
                        StatusMessage = $"{Cards.Count} cards (Offline)";
                        return;
                    }
                }
            }
            catch { }
            StatusMessage = "Error loading cards (Offline)";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private int _statusToken = 0;
    
    // Shows a status message and clears it after delayMs if another status hasn't overridden it
    private async void ShowTemporaryStatus(string message, int delayMs = 3000)
    {
        StatusMessage = message;
        var token = ++_statusToken;
        await Task.Delay(delayMs);
        if (_statusToken == token)
        {
            StatusMessage = "";
        }
    }

    // ===== CARD SELECTION =====
    [RelayCommand]
    private void SelectCard(TrelloCard card)
    {
        if (card == null) return;
        SelectedCard = card;
        IsDetailPanelOpen = true;
        IsBatchMoveMode = false;
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        if (SelectedCard == null || string.IsNullOrEmpty(SelectedCard.DisplayId)) return;
        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard : null;
            if (clipboard != null)
                await clipboard.SetTextAsync(SelectedCard.DisplayId);
            ShowTemporaryStatus($"Copied ID: {SelectedCard.DisplayId}");
        }
        catch { }
    }

    [RelayCommand]
    private void CloseDetailPanel()
    {
        IsDetailPanelOpen = false;
        CloseAllSidePanels();
        SelectedCard = null;
    }

    [RelayCommand]
    private void CloseAllSidePanels()
    {
        IsCommentsPanelOpen = false;
        IsChecklistPanelOpen = false;
        IsAttachmentPanelOpen = false;
        IsMovePanelOpen = false;
        IsNotepadPanelOpen = false;
    }

    [RelayCommand]
    private void UpdateSelection()
    {
        HasSelectedCards = Cards.Any(c => c.IsSelected);
        
        // Unlink only appears if ALL selected cards were added manually
        var selected = Cards.Where(c => c.IsSelected).ToList();
        CanUnlinkSelected = selected.Any() && selected.All(c => c.IsManuallyAdded);
    }

    // ===== REORDER CARDS =====
    [RelayCommand]
    private async Task MoveCardUpAsync(TrelloCard card)
    {
        if (card == null || !IsOnline) return;
        var index = Cards.IndexOf(card);
        if (index <= 0) return;

        var prevCard = Cards[index - 1];
        // Calculate new position: before prev card
        double newPos;
        if (index - 1 == 0)
            newPos = prevCard.Pos / 2.0;
        else
            newPos = (Cards[index - 2].Pos + prevCard.Pos) / 2.0;

        try
        {
            await _api.UpdateCardPositionAsync(card.Id, newPos);
            card.Pos = newPos;
            Cards.Move(index, index - 1);
            ShowTemporaryStatus("Card moved up", 1500);
        }
        catch (Exception ex) { ShowTemporaryStatus($"Error: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task MoveCardDownAsync(TrelloCard card)
    {
        if (card == null || !IsOnline) return;
        var index = Cards.IndexOf(card);
        if (index < 0 || index >= Cards.Count - 1) return;

        var nextCard = Cards[index + 1];
        // Calculate new position: after next card
        double newPos;
        if (index + 1 == Cards.Count - 1)
            newPos = nextCard.Pos + 65536;
        else
            newPos = (nextCard.Pos + Cards[index + 2].Pos) / 2.0;

        try
        {
            await _api.UpdateCardPositionAsync(card.Id, newPos);
            card.Pos = newPos;
            Cards.Move(index, index + 1);
            ShowTemporaryStatus("Card moved down", 1500);
        }
        catch (Exception ex) { ShowTemporaryStatus($"Error: {ex.Message}"); }
    }

    // ===== COPY LINK =====
    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        if (SelectedCard == null || string.IsNullOrEmpty(SelectedCard.ShortUrl)) return;
        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard : null;
            if (clipboard != null)
                await clipboard.SetTextAsync(SelectedCard.ShortUrl);
            ShowTemporaryStatus("Link copied!");
        }
        catch { }
    }
    
    // ===== BATCH COPY LINK =====
    [RelayCommand]
    private async Task BatchCopyLinkAsync()
    {
        var selected = Cards.Where(c => c.IsSelected).ToList();
        if (!selected.Any()) return;
        
        var links = selected.Where(c => !string.IsNullOrEmpty(c.ShortUrl)).Select(c => c.ShortUrl).ToList();
        if (!links.Any()) return;

        try
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard : null;
            if (clipboard != null)
                await clipboard.SetTextAsync(string.Join(Environment.NewLine, links));
            
            var msg = $"Copied {links.Count} links!";
            ShowTemporaryStatus(msg);
            _notificationService.ShowSuccess(msg);
            
            // Optionally deselect cards after copy
            foreach(var c in selected) c.IsSelected = false;
            UpdateSelection();
        }
        catch { }
    }
    
    // ===== BATCH UNLINK =====
    [RelayCommand]
    private async Task BatchUnlinkAsync()
    {
        var cfg = _settings.Load();
        var manualList = cfg.ManuallyAddedCards.GetValueOrDefault(_tabConfig.ListId, new List<string>());
        
        var selected = Cards.Where(c => c.IsSelected).ToList();
        if (!selected.Any() || !IsOnline) return;
        IsLoading = true;
        int success = 0;
        int total = selected.Count;
        foreach (var card in selected)
        {
            StatusMessage = $"Removing {success + 1}/{total}...";
            try
            {
                // Move card to a temporary/archive position - or just remove from list
                // For unlink we move the card out of this list by archiving it
                await _api.ArchiveCardAsync(card.Id);
                Cards.Remove(card);
                if (manualList.Contains(card.Id))
                {
                    manualList.Remove(card.Id);
                }
                success++;
            }
            catch { }
        }
        
        cfg.ManuallyAddedCards[_tabConfig.ListId] = manualList;
        _settings.Save(cfg);

        HasSelectedCards = false;
        CanUnlinkSelected = false;
        var msg = $"Archived {success}/{total} cards";
        StatusMessage = msg;
        _notificationService.ShowSuccess(msg);
        IsLoading = false;
    }

    // ===== NOTEPAD =====
    [RelayCommand]
    private void ToggleNotepad()
    {
        if (IsNotepadPanelOpen)
        {
            IsNotepadPanelOpen = false;
        }
        else
        {
            CloseAllSidePanels();
            IsNotepadPanelOpen = true;

            // Load notepad text from settings
            var cfg = _settings.Load();
            if (cfg.NotepadTexts.TryGetValue(_tabConfig.ListId, out var text))
                NotepadText = text ?? "";
            else
                NotepadText = "";
        }
    }

    [RelayCommand]
    private void SaveNotepad()
    {
        var cfg = _settings.Load();
        cfg.NotepadTexts[_tabConfig.ListId] = NotepadText;
        _settings.Save(cfg);
        StatusMessage = "Notepad saved";
    }

    // ===== ADD CARD VIA LINK =====
    [RelayCommand]
    private async Task AddCardFromLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(AddCardUrl) || !IsOnline) return;
        IsLoading = true;
        try
        {
            // Parse card ID from URL: https://trello.com/c/XXXXXXXX/... 
            var match = Regex.Match(AddCardUrl, @"trello\.com/c/([a-zA-Z0-9]+)");
            if (!match.Success)
            {
                StatusMessage = "Invalid Trello card URL";
                return;
            }
            var cardId = match.Groups[1].Value;

            // Move card to this list
            await _api.MoveCardAsync(cardId, _tabConfig.ListId);
            
            // Mark as manually added
            var cfg = _settings.Load();
            if (!cfg.ManuallyAddedCards.TryGetValue(_tabConfig.ListId, out var manualList))
            {
                manualList = new List<string>();
                cfg.ManuallyAddedCards[_tabConfig.ListId] = manualList;
            }
            if (!manualList.Contains(cardId))
            {
                manualList.Add(cardId);
                _settings.Save(cfg);
            }

            AddCardUrl = "";
            var msg = "Card added!";
            ShowTemporaryStatus(msg);
            _notificationService.ShowSuccess(msg);
            
            // Refresh to get the card
            await FetchCardsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ===== COMMENTS =====
    public ObservableCollection<TrelloComment> Comments { get; } = new();
    
    [ObservableProperty]
    private string _newCommentText = "";

    [RelayCommand]
    private async Task ShowCommentsAsync()
    {
        if (SelectedCard == null || !IsOnline) return;
        CloseAllSidePanels();
        IsCommentsPanelOpen = true;
        
        IsSubLoading = true;
        Comments.Clear();
        var comments = await _api.GetCommentsAsync(SelectedCard.Id);
        foreach (var c in comments) Comments.Add(c);
        IsSubLoading = false;
    }

    [RelayCommand]
    private async Task SendCommentAsync()
    {
        if (SelectedCard == null || string.IsNullOrWhiteSpace(NewCommentText) || !IsOnline) return;
        IsSubLoading = true;
        await _api.SendCommentAsync(SelectedCard.Id, NewCommentText);
        NewCommentText = "";
        IsSubLoading = false;
        await ShowCommentsAsync();
    }

    // ===== CHECKLISTS =====
    public ObservableCollection<TrelloChecklist> Checklists { get; } = new();

    [RelayCommand]
    private async Task ShowChecklistsAsync()
    {
        if (SelectedCard == null) return;
        CloseAllSidePanels();
        IsChecklistPanelOpen = true;

        IsSubLoading = true;
        Checklists.Clear();
        var lists = await _api.GetChecklistsAsync(SelectedCard.Id);
        foreach (var l in lists) Checklists.Add(l);
        IsSubLoading = false;
    }

    [RelayCommand]
    private async Task ToggleCheckItemAsync(TrelloChecklistItem item)
    {
        if (SelectedCard == null || item == null || !IsOnline) return;
        item.State = item.State == "complete" ? "incomplete" : "complete";
        try
        {
            await _api.ToggleCheckItemAsync(SelectedCard.Id, item.Id, item.State == "complete");
        }
        catch
        {
            item.State = item.State == "complete" ? "incomplete" : "complete";
        }
    }

    [RelayCommand]
    private async Task DuplicateChecklistAsync(TrelloChecklist checklist)
    {
        if (SelectedCard == null || checklist == null || !IsOnline) return;
        IsSubLoading = true;
        try
        {
            // Use DisplayName directly from settings (no #EDITING prefix)
            var cfg = _settings.Load();
            var newName = string.IsNullOrWhiteSpace(cfg.DisplayName) ? "USER" : cfg.DisplayName;

            var (newChecklist, rawJson) = await _api.DuplicateChecklistAsync(SelectedCard.Id, checklist.Id, newName);
            
            if (newChecklist != null && rawJson != null)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var newItemsMap = new Dictionary<string, string>();
                if (doc.RootElement.TryGetProperty("checkItems", out var checkItemsEl))
                {
                    foreach (var item in checkItemsEl.EnumerateArray())
                    {
                        var nm = item.GetProperty("name").GetString();
                        var id = item.GetProperty("id").GetString();
                        if (!string.IsNullOrEmpty(nm) && !string.IsNullOrEmpty(id))
                            newItemsMap[nm] = id;
                    }
                }

                var updateTasks = new List<Task>();
                foreach (var srcItem in checklist.CheckItems)
                {
                    if (srcItem.State == "complete" && newItemsMap.TryGetValue(srcItem.Name, out var newItemId))
                    {
                        updateTasks.Add(_api.UpdateCheckItemStateAsync(SelectedCard.Id, newItemId, true));
                    }
                }
                if (updateTasks.Count > 0) await Task.WhenAll(updateTasks);
            }

            Checklists.Clear();
            var lists = await _api.GetChecklistsAsync(SelectedCard.Id);
            foreach (var l in lists) Checklists.Add(l);
        }
        catch { }
        finally { IsSubLoading = false; }
    }

    [RelayCommand]
    private async Task DeleteChecklistAsync(TrelloChecklist checklist)
    {
        if (SelectedCard == null || checklist == null || !IsOnline) return;
        IsSubLoading = true;
        try
        {
            var success = await _api.DeleteChecklistAsync(checklist.Id);
            if (success) Checklists.Remove(checklist);
        }
        catch { }
        finally { IsSubLoading = false; }
    }

    // ===== ATTACHMENTS =====
    public ObservableCollection<TrelloAttachment> Attachments { get; } = new();

    [RelayCommand]
    private async Task ShowAttachmentsAsync()
    {
        if (SelectedCard == null) return;
        CloseAllSidePanels();
        IsAttachmentPanelOpen = true;

        IsSubLoading = true;
        Attachments.Clear();
        var atts = await _api.GetAttachmentsAsync(SelectedCard.Id);
        foreach (var a in atts) Attachments.Add(a);
        IsSubLoading = false;
    }

    [RelayCommand]
    private void OpenAttachment(TrelloAttachment att)
    {
        if (att == null || string.IsNullOrEmpty(att.Url)) return;
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = att.Url, UseShellExecute = true });
        } catch { }
    }

    // ===== MOVE CARD =====
    public ObservableCollection<TrelloBoard> MoveBoards { get; } = new();
    public ObservableCollection<TrelloList> MoveLists { get; } = new();

    [ObservableProperty] private TrelloBoard? _selectedMoveBoard;
    [ObservableProperty] private TrelloList? _selectedMoveList;
    [ObservableProperty] private bool _setAsDefaultMove;

    [RelayCommand]
    private async Task ShowMovePanelAsync()
    {
        if (!IsOnline) return;
        if (SelectedCard == null && !IsBatchMoveMode) return;
        CloseAllSidePanels();
        IsMovePanelOpen = true;

        await LoadMoveBoardsAsync();
    }

    [RelayCommand]
    private async Task ShowBatchMovePanelAsync()
    {
        var selectedCount = Cards.Count(c => c.IsSelected);
        if (selectedCount == 0 || !IsOnline) return;

        IsBatchMoveMode = true;
        SelectedCard = null;
        IsDetailPanelOpen = false;
        CloseAllSidePanels();
        IsMovePanelOpen = true;

        await LoadMoveBoardsAsync();
    }

    private async Task LoadMoveBoardsAsync()
    {
        IsLoading = true;
        MoveBoards.Clear();
        SelectedMoveBoard = null;
        SelectedMoveList = null;
        SetAsDefaultMove = false;

        try
        {
            var boards = await _api.GetBoardsAsync();
            foreach (var b in boards) MoveBoards.Add(b);

            // Auto-select default board
            var cfg = _settings.Load();
            if (!string.IsNullOrEmpty(cfg.DefaultMoveBoardId))
            {
                var match = MoveBoards.FirstOrDefault(b => b.Id == cfg.DefaultMoveBoardId);
                if (match != null) SelectedMoveBoard = match;
            }
        }
        catch { }
        finally { IsLoading = false; }
    }

    partial void OnSelectedMoveBoardChanged(TrelloBoard? value)
    {
        if (value != null) _ = LoadMoveListsAsync(value.Id);
        else MoveLists.Clear();
    }

    private async Task LoadMoveListsAsync(string boardId)
    {
        IsLoading = true;
        MoveLists.Clear();
        SelectedMoveList = null;
        try
        {
            var lists = await _api.GetListsAsync(boardId);
            foreach (var l in lists) MoveLists.Add(l);

            // Auto-select default list
            var cfg = _settings.Load();
            if (!string.IsNullOrEmpty(cfg.DefaultMoveListId) && boardId == cfg.DefaultMoveBoardId)
            {
                var match = MoveLists.FirstOrDefault(l => l.Id == cfg.DefaultMoveListId);
                if (match != null) SelectedMoveList = match;
            }
        }
        catch { }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task MoveCardAsync()
    {
        if (SelectedMoveBoard == null || SelectedMoveList == null || !IsOnline) return;
        IsLoading = true;
        try
        {
            // Save as default if checkbox checked
            if (SetAsDefaultMove)
            {
                var cfg = _settings.Load();
                cfg.DefaultMoveBoardId = SelectedMoveBoard.Id;
                cfg.DefaultMoveBoardName = SelectedMoveBoard.Name;
                cfg.DefaultMoveListId = SelectedMoveList.Id;
                cfg.DefaultMoveListName = SelectedMoveList.Name;
                _settings.Save(cfg);
            }

            if (IsBatchMoveMode)
            {
                // Batch move
                var selectedCards = Cards.Where(c => c.IsSelected).ToList();
                if (!selectedCards.Any()) return;

                int success = 0;
                int total = selectedCards.Count;
                foreach (var card in selectedCards)
                {
                    StatusMessage = $"Moving {success + 1}/{total}...";
                    try
                    {
                        await _api.MoveCardAsync(card.Id, SelectedMoveList.Id);
                        Cards.Remove(card);
                        success++;
                    }
                    catch { }
                }
                var targetName = SelectedMoveList.Name;
                IsMovePanelOpen = false;
                IsBatchMoveMode = false;
                HasSelectedCards = false;
                StatusMessage = $"Moved {success}/{total} cards to {targetName}";
            }
            else if (SelectedCard != null)
            {
                await _api.MoveCardAsync(SelectedCard.Id, SelectedMoveList.Id);
                var movedName = SelectedMoveList.Name;
                Cards.Remove(SelectedCard);
                CloseDetailPanel();
                StatusMessage = $"Moved to {movedName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Move error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}

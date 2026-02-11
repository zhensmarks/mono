using System.Collections.ObjectModel;
using BMachine.SDK;
using BMachine.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public abstract partial class BaseTrelloListViewModel : ObservableObject
{
    protected readonly IDatabase _database;
    protected readonly INotificationService? _notificationService;
    protected readonly IActivityService? _activityService;

    public BaseTrelloListViewModel(IDatabase database, INotificationService? notificationService = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _notificationService = notificationService;
        _activityService = database as IActivityService; // Attempt to cast, or we could inject explicitly if refactored
        Cards = new ObservableCollection<TrelloCard>();
    }

    // Design-time constructor support
    public BaseTrelloListViewModel()
    {
        _database = null!;
        _activityService = null;
        Cards = new ObservableCollection<TrelloCard>();
    }
    
    // Customize Accent Color
    [ObservableProperty] private Avalonia.Media.IBrush? _accentColor = Avalonia.Media.Brushes.Orange; // Default
    protected virtual string ColorSettingKey => "";
    
    public async Task LoadAccentColor()
    {
        if (_database == null || string.IsNullOrEmpty(ColorSettingKey)) return;
        try
        {
            var hex = await _database.GetAsync<string>(ColorSettingKey);
            if (!string.IsNullOrEmpty(hex))
            {
                if (Avalonia.Media.Color.TryParse(hex, out var color))
                {
                     AccentColor = new Avalonia.Media.SolidColorBrush(color);
                }
            }
        }
        catch { }
    }

    protected async Task LogActivity(string type, string title, string desc)
    {
        if (_activityService != null)
        {
            await _activityService.LogAsync(type, title, desc);
        }
    }

    [ObservableProperty]
    private string _title = "List";

    [ObservableProperty]
    private ObservableCollection<TrelloCard> _cards;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private TrelloCard? _selectedCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    private bool _isOnline = true;

    public string ConnectionStatusText => IsOnline ? "Online" : "Offline";

    partial void OnIsOnlineChanged(bool value)
    {
        // Maybe log status change?
    }

    public event Action? CloseRequested;
    
    [RelayCommand]
    protected virtual void Close()
    {
        CloseRequested?.Invoke();
    }

    // --- Comment Logic ---

    [ObservableProperty] private bool _isCommentPanelOpen;
    [ObservableProperty] private ObservableCollection<TrelloComment> _comments = new();
    [ObservableProperty] private string _newCommentText = "";
    [ObservableProperty] private bool _isLoadingComments;

    [RelayCommand]
    protected async Task ShowComments(TrelloCard card)
    {
        if (card == null) return;
        
        CloseAllSidePanels(); // Close others first
        
        SelectedCard = card;
        IsCommentPanelOpen = true;
        
        await LoadComments(card.Id);
    }

    // --- Detail Panel Logic (Shared) ---
    [ObservableProperty] private bool _isDetailPanelOpen;

    [RelayCommand]
    protected virtual void SelectCard(TrelloCard card)
    {
        if (card == null) return;
        
        // Toggle Logic: If clicking same card, close it
        if (SelectedCard == card && IsDetailPanelOpen)
        {
            CloseDetailPanel();
            return;
        }
        
        // Deactivate previous
        if (SelectedCard != null) SelectedCard.IsActive = false;
        
        CloseAllSidePanels(); // Close others
        SelectedCard = card;
        
        // Activate new
        if (SelectedCard != null) SelectedCard.IsActive = true;
        
        IsDetailPanelOpen = true; // Open Detail Panel
    }

    [RelayCommand]
    protected virtual void CloseDetailPanel()
    {
        IsDetailPanelOpen = false;
        if (SelectedCard != null)
        {
            SelectedCard.IsActive = false;
            SelectedCard = null;
        }
    }

    [RelayCommand]
    protected async Task CopyId(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        var clipboard = GetClipboard();
        if (clipboard == null) return;
        
        await clipboard.SetTextAsync(id);
        StatusMessage = "ID Copied!";
        await LogActivity("System", "Copied", id);
    }
    
    private Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        // Fallback for SingleView (Mobile/Web) if needed, though likely not for this desktop app
        else if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime single)
        {
            var top = Avalonia.Controls.TopLevel.GetTopLevel(single.MainView);
            return top?.Clipboard;
        }
        return null;
    }

    [RelayCommand]
    protected virtual void CloseAllSidePanels()
    {
        IsDetailPanelOpen = false;
        IsCommentPanelOpen = false;
        IsChecklistPanelOpen = false;
        IsMovePanelOpen = false;
        IsAttachmentPanelOpen = false;
    }

    [RelayCommand]
    private void CloseCommentsPanel()
    {
        IsCommentPanelOpen = false;
        Comments.Clear();
        // Return to Detail Panel if a card is selected
        if (SelectedCard != null) IsDetailPanelOpen = true;
    }

    [RelayCommand]
    private async Task SendComment()
    {
        if (SelectedCard == null || string.IsNullOrWhiteSpace(NewCommentText)) return;

        var text = NewCommentText;
        NewCommentText = ""; 
        
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/cards/{SelectedCard.Id}/actions/comments?key={apiKey}&token={token}&text={Uri.EscapeDataString(text)}";
            
            var response = await client.PostAsync(url, null);
            if (response.IsSuccessStatusCode)
            {
                await LoadComments(SelectedCard.Id);
                await LogActivity("Comment", $"Comment on {SelectedCard.Name}", text);
            }
            else
            {
                StatusMessage = "Failed to send comment.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sending comment: {ex.Message}";
        }
    }

    protected async Task LoadComments(string cardId)
    {
        IsLoadingComments = true;
        Comments.Clear();
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/cards/{cardId}/actions?filter=commentCard&key={apiKey}&token={token}&memberCreator=true&memberCreator_fields=fullName,initials,avatarHash";
            
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            var list = new List<TrelloComment>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var comment = new TrelloComment
                {
                    Id = element.GetProperty("id").GetString() ?? "",
                    Date = element.GetProperty("date").GetDateTime().ToLocalTime()
                };

                if (element.TryGetProperty("data", out var data) && data.TryGetProperty("text", out var txt))
                {
                    comment.Text = txt.GetString() ?? "";
                }

                if (element.TryGetProperty("memberCreator", out var creator))
                {
                    comment.MemberCreatorId = creator.GetProperty("id").GetString() ?? "";
                    comment.MemberCreatorName = creator.GetProperty("fullName").GetString() ?? "";
                    comment.MemberCreatorInitials = creator.GetProperty("initials").GetString() ?? "";
                    
                    if (creator.TryGetProperty("avatarHash", out var hash) && hash.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var h = hash.GetString();
                        if (!string.IsNullOrEmpty(h))
                        {
                            comment.MemberCreatorAvatarUrl = $"https://trello-members.s3.amazonaws.com/{comment.MemberCreatorId}/{h}/50.png";
                        }
                    }
                }
                list.Add(comment);
            }
            foreach(var c in list) Comments.Add(c);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading comments: {ex.Message}";
        }
        finally
        {
            IsLoadingComments = false;
        }
    }

    // --- Checklist Logic ---

    [ObservableProperty] private bool _isChecklistPanelOpen;
    [ObservableProperty] private ObservableCollection<TrelloChecklist> _checklists = new();
    [ObservableProperty] private bool _isLoadingChecklists;

    [ObservableProperty] private bool _isDuplicateMode;
    [ObservableProperty] private TrelloChecklist? _selectedSourceChecklist;
    [ObservableProperty] private string _duplicateChecklistName = "";

    [RelayCommand]
    protected async Task ShowChecklists(TrelloCard card)
    {
        if (card == null) return;
        
        CloseAllSidePanels(); // Ensure exclusivity
        
        SelectedCard = card;
        IsChecklistPanelOpen = true;
        
        await LoadChecklists(card.Id);
    }

    [RelayCommand]
    private void CloseChecklistPanel()
    {
        IsChecklistPanelOpen = false;
        Checklists.Clear();
        SelectedSourceChecklist = null; // Explicit reset
        DuplicateChecklistName = ""; // Explicit reset
        if (SelectedCard != null) IsDetailPanelOpen = true;
    }

    [RelayCommand]
    private async Task ToggleDuplicateMode()
    {
        IsDuplicateMode = !IsDuplicateMode;
        if (IsDuplicateMode)
        {
             var userName = await _database.GetAsync<string>("User.Name");
             if (string.IsNullOrWhiteSpace(userName)) userName = "USER";
             DuplicateChecklistName = $"#EDITING {userName.ToUpper()}"; 
        }
    }

    [RelayCommand]
    private async Task DuplicateChecklist()
    {
        if (SelectedCard == null || SelectedSourceChecklist == null || string.IsNullOrWhiteSpace(DuplicateChecklistName)) return;
        
        IsLoadingChecklists = true;
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;
            
            using var client = new HttpClient();
            
            var createUrl = $"https://api.trello.com/1/checklists?idCard={SelectedCard.Id}&idChecklistSource={SelectedSourceChecklist.Id}&name={Uri.EscapeDataString(DuplicateChecklistName)}&key={apiKey}&token={token}";
            var createRes = await client.PostAsync(createUrl, null);
            if (!createRes.IsSuccessStatusCode)
            {
                 StatusMessage = "Failed to create duplicate checklist.";
                 return;
            }
            
            var json = await createRes.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            var newItemsMap = new Dictionary<string, string>(); 
            if (doc.RootElement.TryGetProperty("checkItems", out var checkItemsReq))
            {
                foreach(var item in checkItemsReq.EnumerateArray())
                {
                    var nm = item.GetProperty("name").GetString();
                    var id = item.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(nm) && !string.IsNullOrEmpty(id)) newItemsMap[nm] = id;
                }
            }
            
            var updateTasks = new List<Task>();
            foreach (var srcItem in SelectedSourceChecklist.Items)
            {
                if (srcItem.State == "complete" && newItemsMap.TryGetValue(srcItem.Name, out var newItemId))
                {
                    var updateUrl = $"https://api.trello.com/1/cards/{SelectedCard.Id}/checkItem/{newItemId}?state=complete&key={apiKey}&token={token}";
                    updateTasks.Add(client.PutAsync(updateUrl, null));
                }
            }
            if (updateTasks.Any()) await Task.WhenAll(updateTasks);
            
            await LogActivity("Checklist", "Duplicated Checklist", $"{DuplicateChecklistName} from {SelectedSourceChecklist.Name}");
            
            // Local Update for Real-time Feedback
            SelectedCard.ChecklistNames.Add(DuplicateChecklistName);
            SelectedCard.HasChecklist = true;
            SelectedCard.RefreshChecklistStatus();

            IsDuplicateMode = false;
            DuplicateChecklistName = "";
            SelectedSourceChecklist = null;
            
            // Background reload to sync IDs/Items
            _ = LoadChecklists(SelectedCard.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error duplicating: {ex.Message}";
        }
        finally
        {
            IsLoadingChecklists = false;
        }
    }

    [RelayCommand]
    private async Task DeleteChecklist(TrelloChecklist checklist)
    {
        if (checklist == null || SelectedCard == null) return;
        
        IsLoadingChecklists = true;
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;
            
            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/checklists/{checklist.Id}?key={apiKey}&token={token}";
            var response = await client.DeleteAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                await LoadChecklists(SelectedCard.Id);
                await LogActivity("Checklist", "Deleted Checklist", $"{checklist.Name} on {SelectedCard.Name}");
            }
            else
            {
                StatusMessage = "Failed to delete checklist.";
            }
        }
        catch (Exception ex)
        {
             StatusMessage = "Error deleting checklist.";
        }
        finally
        {
             IsLoadingChecklists = false;
        }
    }

    protected async Task LoadChecklists(string cardId)
    {
        IsLoadingChecklists = true;
        Checklists.Clear();
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/cards/{cardId}/checklists?key={apiKey}&token={token}";
            
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var checklist = new TrelloChecklist
                {
                    Id = element.GetProperty("id").GetString() ?? "",
                    Name = element.GetProperty("name").GetString() ?? "",
                    IdCard = element.GetProperty("idCard").GetString() ?? ""
                };

                if (element.TryGetProperty("checkItems", out var items))
                {
                    foreach(var item in items.EnumerateArray())
                    {
                        var ci = new TrelloChecklistItem
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Name = item.GetProperty("name").GetString() ?? "",
                            State = item.GetProperty("state").GetString() ?? "incomplete"
                        };
                        checklist.Items.Add(ci);
                    }
                }
                Checklists.Add(checklist);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading checklists: {ex.Message}";
        }
        finally
        {
            IsLoadingChecklists = false;
        }
    }
    
    [RelayCommand]
    private async Task ToggleCheckItem(TrelloChecklistItem item)
    {
        if (item == null || SelectedCard == null) return;
        var newState = item.IsChecked ? "complete" : "incomplete";
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
             if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

             using var client = new HttpClient();
             var url = $"https://api.trello.com/1/cards/{SelectedCard.Id}/checkItem/{item.Id}?state={newState}&key={apiKey}&token={token}";
             var response = await client.PutAsync(url, null);
             if (!response.IsSuccessStatusCode)
             {
                 item.IsChecked = !item.IsChecked; 
                 StatusMessage = "Failed to update item state.";
             }
             else 
             {
                 // Log Toggle? It's frequent. User said "semua cheklis".
                 // Let's log if it was COMPLETED (checked). Unchecking might be less important? 
                 // User said "semua".
                 await LogActivity("Checklist", "Update Item", $"{item.Name} -> {newState} on {SelectedCard.Name}");
             }
        }
        catch
        {
            item.IsChecked = !item.IsChecked;
        }
    }

    // --- Attachment Logic ---

    [ObservableProperty] private bool _isAttachmentPanelOpen;
    [ObservableProperty] private ObservableCollection<TrelloAttachment> _attachments = new();
    [ObservableProperty] private bool _isLoadingAttachments;

    [RelayCommand]
    protected async Task ShowAttachments(TrelloCard card)
    {
        if (card == null) return;
        
        CloseAllSidePanels();
        
        SelectedCard = card;
        IsAttachmentPanelOpen = true;
        
        await LoadAttachments(card.Id);
    }

    [RelayCommand]
    private void CloseAttachmentPanel()
    {
        IsAttachmentPanelOpen = false;
        Attachments.Clear();
        if (SelectedCard != null) IsDetailPanelOpen = true;
    }

    protected async Task LoadAttachments(string cardId)
    {
        IsLoadingAttachments = true;
        Attachments.Clear();
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/cards/{cardId}/attachments?key={apiKey}&token={token}";
            
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var att = new TrelloAttachment
                {
                    Id = element.GetProperty("id").GetString() ?? "",
                    Name = element.GetProperty("name").GetString() ?? "",
                    Url = element.GetProperty("url").GetString() ?? "",
                    MimeType = element.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "" : "",
                    Bytes = element.TryGetProperty("bytes", out var b) ? (long)b.GetInt64() : 0 // Safe cast
                };
                
                // Previews
                if (element.TryGetProperty("previews", out var previews) && previews.GetArrayLength() > 0)
                {
                    // Get a mid-sized preview (e.g., 300px) or the largest if small
                    // Trello returns previews sorted by size? Usually small to large.
                    // Let's pick one around index 2 or 3 if available, or last.
                    var pIndex = Math.Min(3, previews.GetArrayLength() - 1);
                    var preview = previews[pIndex];
                    if (preview.TryGetProperty("url", out var pUrl)) att.PreviewUrl = pUrl.GetString() ?? "";
                    att.IsImage = true;
                }
                else if (att.MimeType.StartsWith("image/"))
                {
                    att.IsImage = true;
                }

                Attachments.Add(att);
                
                // Load Thumbnail Helper
                if (att.IsImage && !string.IsNullOrEmpty(att.PreviewUrl))
                {
                     _ = LoadThumbnail(att);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading attachments: {ex.Message}";
        }
        finally
        {
            IsLoadingAttachments = false;
        }
    }

    private async Task LoadThumbnail(TrelloAttachment att)
    {
        try
        {
             // Get API credentials for authenticated access
             var apiKey = await _database.GetAsync<string>("Trello.ApiKey") ?? "";
             var token = await _database.GetAsync<string>("Trello.Token") ?? "";
             
             using var client = new HttpClient();
             if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(token))
             {
                 client.DefaultRequestHeaders.Add("Authorization", $"OAuth oauth_consumer_key=\"{apiKey}\", oauth_token=\"{token}\"");
             }

             var bytes = await client.GetByteArrayAsync(att.PreviewUrl);
             using var stream = new System.IO.MemoryStream(bytes);
             att.Thumbnail = new Avalonia.Media.Imaging.Bitmap(stream); 
        }
        catch 
        {
            // Ignore thumbnail errors
        }
    }

    [RelayCommand]
    private void OpenAttachment(TrelloAttachment att)
    {
        if (att == null) return;
        try 
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = att.Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cannot open link: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DownloadAttachment(TrelloAttachment attachment)
    {
        if (attachment == null || SelectedCard == null) return;
        
        attachment.IsDownloading = true;
        try
        {
            var offlinePath = await _database.GetAsync<string>("Configs.Storage.OfflinePath");
            if (string.IsNullOrEmpty(offlinePath))
            {
                 offlinePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "BMachine_Attachments");
            }

            var folder = System.IO.Path.Combine(offlinePath, "Attachments", SelectedCard.Id);
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

            var filePath = System.IO.Path.Combine(folder, attachment.Name);

            using var client = new HttpClient();
            
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"OAuth oauth_consumer_key=\"{apiKey}\", oauth_token=\"{token}\"");
            }

            var downloadUrl = attachment.Url;
            var data = await client.GetByteArrayAsync(downloadUrl);
            await System.IO.File.WriteAllBytesAsync(filePath, data);
            
            StatusMessage = $"Downloaded: {attachment.Name}";
            await LogActivity("Attachment", "Downloaded", $"{attachment.Name} from {SelectedCard.Name}");

            // Open Folder?
            System.Diagnostics.Process.Start("explorer", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            attachment.IsDownloading = false;
        }
    }

    // --- Move Card Logic ---

    [ObservableProperty] private bool _isMovePanelOpen;
    [ObservableProperty] private ObservableCollection<TrelloItem> _availableBoards = new();
    [ObservableProperty] private ObservableCollection<TrelloItem> _availableLists = new();
    [ObservableProperty] private TrelloItem? _selectedMoveBoard;
    [ObservableProperty] private TrelloItem? _selectedMoveList;
    [ObservableProperty] private bool _isLoadingMoveData;
    
    // Batch Move Properties
    [ObservableProperty] private bool _isBatchMoveMode;
    [ObservableProperty] private bool _hasSelectedCards;
    [ObservableProperty] private string _batchStatusMessage = "";
    
    [RelayCommand]
    private void SelectionChanged()
    {
        HasSelectedCards = Cards.Any(c => c.IsSelected);
    }
    
    [RelayCommand]
    private async Task ShowBatchMovePanel()
    {
        var selectedCount = Cards.Count(c => c.IsSelected);
        if (selectedCount == 0) return;
        
        IsBatchMoveMode = true;
        SelectedCard = new TrelloCard { Name = $"{selectedCount} Cards Selected" }; // Dummy card for Header
        
        CloseAllSidePanels();
        IsMovePanelOpen = true;
        
        await LoadMoveBoards();
    }

    partial void OnSelectedMoveBoardChanged(TrelloItem? value)
    {
        if (value != null) _ = LoadMoveLists(value.Id);
        else AvailableLists.Clear();
    }

    [RelayCommand]
    protected async Task ShowMovePanel(TrelloCard card)
    {
        if (card == null) return;
        SelectedCard = card;
        IsBatchMoveMode = false;
        
        // AutoMove Logic Check
        bool autoMoved = await AutoMoveCard(card);
        if (autoMoved) return;

        CloseAllSidePanels();
        IsMovePanelOpen = true;
        
        await LoadMoveBoards();
    }
    
    [RelayCommand]
    private void CloseMovePanel()
    {
        IsMovePanelOpen = false;
        
        // Reset Dropdowns
        SelectedMoveBoard = null;
        SelectedMoveList = null;
        
        AvailableBoards.Clear();
        AvailableLists.Clear();
        
        // If it was a batch move (dummy card), clear selection. Otherwise return to detail.
        if (IsBatchMoveMode) 
        {
             SelectedCard = null;
             IsBatchMoveMode = false;
        }
        else if (SelectedCard != null) 
        {
            IsDetailPanelOpen = true;
        }
    }



    private async Task<bool> DataMoveCard(TrelloCard card, string boardId, string listId, string successMessage, bool isBatchMode = false)
    {
         try
         {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            // If board/list not provided, use defaults or logic for automove?
            // Existing logic for DataMoveCard seemed to expect specific targets.
            
            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/cards/{card.Id}?idList={listId}&idBoard={boardId}&key={apiKey}&token={token}";
            
            // Handle Automove scenario if parameters are empty (though typically passed explicitly)
            if (string.IsNullOrEmpty(boardId) || string.IsNullOrEmpty(listId))
            {
                // This seems like a legacy path for AutoMove, but let's stick to the core fix
                // for the force close which happens during explicit Batch Move.
            }

            var response = await client.PutAsync(url, null);
            
            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(successMessage))
                    StatusMessage = successMessage;

                Cards.Remove(card); 
                
                if (!isBatchMode)
                    CloseMovePanel();

                await LogActivity("Move", "Card Moved", $"{card.Name} -> {successMessage}");
                return true;
            }
            return false;
         }
         catch(Exception ex) 
         {
             StatusMessage = $"Error moving card: {ex.Message}";
             return false;
         }
    }

    private async Task<bool> AutoMoveCard(TrelloCard card)
    {
        var parts = card.Name.Split('_');
        if (parts.Length < 3) return false; 
        
        string targetName = parts[2].Trim();
        IsLoadingMoveData = true;
        try
        {
            var qcBoardId = await _database.GetAsync<string>("Trello.QcBoardId");
            if (string.IsNullOrEmpty(qcBoardId)) return false; 

            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/boards/{qcBoardId}/lists?key={apiKey}&token={token}&fields=name,id";
            var json = await client.GetStringAsync(url);
            
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var lists = new List<TrelloItem>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                lists.Add(new TrelloItem 
                { 
                    Id = element.GetProperty("id").GetString() ?? "", 
                    Name = element.GetProperty("name").GetString() ?? "" 
                });
            }
            
            TrelloItem? matchedList = null;
            foreach(var list in lists)
            {
                if (targetName.Contains(list.Name, StringComparison.OrdinalIgnoreCase))
                {
                    matchedList = list;
                    break;
                }
            }
            
            if (matchedList != null) 
            {
                return await DataMoveCard(card, qcBoardId, matchedList.Id, $"Moved to QC: {matchedList.Name}");
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            IsLoadingMoveData = false;
        }
    }

    [RelayCommand]
    protected async Task StartMoveCard(TrelloCard? card)
    {
        if (card == null) return;
        
        CloseAllSidePanels();
        SelectedCard = card;
        IsBatchMoveMode = false;
        IsMovePanelOpen = true;
        
        await LoadMoveBoards();
    }

    private async Task LoadMoveBoards()
    {
        IsLoadingMoveData = true;
        AvailableBoards.Clear();
        SelectedMoveBoard = null; // FORCE RESET to ensure we don't remember previous selection
        SelectedMoveList = null;  // Also reset list
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            var qcBoardId = await _database.GetAsync<string>("Trello.QcBoardId");
            
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            
            // Always fetch ALL boards to allow user selection
            var url = $"https://api.trello.com/1/members/me/boards?key={apiKey}&token={token}&fields=name,id";
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                AvailableBoards.Add(new TrelloItem 
                { 
                    Id = element.GetProperty("id").GetString() ?? "", 
                    Name = element.GetProperty("name").GetString() ?? "" 
                });
            }

            // Default Selection Logic
            if (!string.IsNullOrEmpty(qcBoardId))
            {
                var match = AvailableBoards.FirstOrDefault(b => b.Id == qcBoardId);
                if (match != null)
                {
                    SelectedMoveBoard = match;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading boards: {ex.Message}";
        }
        finally
        {
            IsLoadingMoveData = false;
        }
    }

    private async Task LoadMoveLists(string boardId)
    {
        IsLoadingMoveData = true;
        AvailableLists.Clear();
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return;

            using var client = new HttpClient();
            var url = $"https://api.trello.com/1/boards/{boardId}/lists?key={apiKey}&token={token}&fields=name,id";
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                AvailableLists.Add(new TrelloItem 
                { 
                    Id = element.GetProperty("id").GetString() ?? "", 
                    Name = element.GetProperty("name").GetString() ?? "" 
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading lists: {ex.Message}";
        }
        finally
        {
            IsLoadingMoveData = false;
        }
    }



    [RelayCommand]
    private async Task MoveCard()
    {
        if (SelectedCard == null && !IsBatchMoveMode) return;
        if (SelectedMoveBoard == null || SelectedMoveList == null) return;
        
        IsLoadingMoveData = true;
        try
        {
             if (IsBatchMoveMode)
             {
                 var selectedCards = Cards.Where(c => c.IsSelected).ToList();
                 if (!selectedCards.Any()) return;
                 
                 int successCount = 0;
                 int total = selectedCards.Count;
                 
                 foreach(var card in selectedCards)
                 {
                     StatusMessage = $"Moving {card.Name} ({successCount + 1}/{total})...";
                     // pass isBatchMode = true to prevent panel from closing and data from clearing
                     bool success = await DataMoveCard(card, SelectedMoveBoard.Id, SelectedMoveList.Id, "", true);
                     if (success) successCount++;
                 }
                 
                 // Cache name BEFORE closing panel (which clears SelectedMoveList)
                 var targetListName = SelectedMoveList.Name;
                 CloseMovePanel();
                 StatusMessage = $"Moved {successCount} of {total} cards to {targetListName}";
                 HasSelectedCards = false; // Reset selection visibility
             }
             else if (SelectedCard != null)
             {
                 await DataMoveCard(SelectedCard, SelectedMoveBoard.Id, SelectedMoveList.Id, $"Moved to {SelectedMoveList.Name}", false);
             }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch move failed: {ex.Message}";
        }
        finally
        {
             IsLoadingMoveData = false;
        }
    }

    // Helper for syncing cards
    protected void UpdateCardsCollection(List<TrelloCard> newCards)
    {
        var toRemove = Cards.Where(existing => !newCards.Any(newC => newC.Id == existing.Id)).ToList();
        foreach (var item in toRemove) Cards.Remove(item);
        
        foreach (var newC in newCards)
        {
            var existing = Cards.FirstOrDefault(c => c.Id == newC.Id);
            if (existing != null)
            {
                existing.Name = newC.Name;
                existing.Description = newC.Description;
                existing.DueDate = newC.DueDate;
                existing.IsOverdue = newC.IsOverdue;
                existing.LabelsText = newC.LabelsText;
                existing.HasChecklist = newC.HasChecklist;
                existing.AttachmentCount = newC.AttachmentCount;
            }
            else
            {
                Cards.Add(newC);
            }
        }
    }

    protected async Task<List<TrelloCard>> FetchCards(string listId, string apiKey, string token)
    {
        var results = new List<TrelloCard>();
        var cacheKey = $"Cache.List.{listId}";
        
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15); // Increased timeout to prevent false offline
        var url = $"https://api.trello.com/1/lists/{listId}/cards?key={apiKey}&token={token}&fields=name,desc,due,labels,idMembers,badges&checklists=all";
        
        string json = "";
        
        try 
        {
            json = await client.GetStringAsync(url);
            
            // Cache Success
            await _database.SetAsync(cacheKey, json);
            IsOnline = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fetch Error (Offline?): {ex.Message}");
            IsOnline = false;
            
            // Fallback to Cache
            try 
            {
                json = await _database.GetAsync<string>(cacheKey) ?? "";
            }
            catch { /* Cache Read Error */ }
        }

        if (string.IsNullOrEmpty(json)) return results; // No API and No Cache

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return results;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
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

                if (element.TryGetProperty("badges", out var badges))
                {
                    if (badges.TryGetProperty("attachments", out var att)) card.AttachmentCount = att.GetInt32();
                }

                // Parse Checklists Names
                if (element.TryGetProperty("checklists", out var checkArr) && checkArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var checkItem in checkArr.EnumerateArray())
                    {
                        if (checkItem.TryGetProperty("name", out var cName))
                        {
                            card.ChecklistNames.Add(cName.GetString() ?? "");
                        }
                    }
                }
                card.HasChecklist = card.ChecklistNames.Count > 0; // Simple boolean fallback

                results.Add(card);
            }
        }
        catch (Exception ex)
        {
             StatusMessage = $"Parse Error: {ex.Message}";
        }

        return results;
    }
    protected async Task<bool> CheckForUpdates(string listId)
    {
        try
        {
            var apiKey = await _database.GetAsync<string>("Trello.ApiKey");
            var token = await _database.GetAsync<string>("Trello.Token");
            
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return false;

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            
            // Fetch only IDs to minimize data transfer and parsing
            var url = $"https://api.trello.com/1/lists/{listId}/cards?key={apiKey}&token={token}&fields=id";
            var json = await client.GetStringAsync(url);
            
            // If offline/error, client throws, goes to catch.
            IsOnline = true; 

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;

            var currentIds = new HashSet<string>(Cards.Select(c => c.Id));
            var newIds = new HashSet<string>();
            
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("id", out var idProp))
                {
                    newIds.Add(idProp.GetString() ?? "");
                }
            }

            // Simple comparison: Any mismatch in content or count means update needed
            // This handles adds, removes, and reorders (if we cared about order, but HashSet doesn't. 
            // For simple notification "something changed", count or set difference is enough.
            // If only Order changed, we might skip refresh? 
            // Trello cards move positions frequently. Let's use SetEquals.
            
            bool isSame = currentIds.SetEquals(newIds);
            return !isSame;
        }
        catch 
        {
            return false;
        }
    }
}

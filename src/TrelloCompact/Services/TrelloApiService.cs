using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TrelloCompact.Models;

namespace TrelloCompact.Services;

public class TrelloApiService
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settings;

    public TrelloApiService(SettingsService settings)
    {
        _settings = settings;
    }

    private string GetAuthParams()
    {
        var cfg = _settings.Load();
        if (string.IsNullOrEmpty(cfg.TrelloApiKey) || string.IsNullOrEmpty(cfg.TrelloToken))
            throw new Exception("API Key or Token is missing");
        return $"key={cfg.TrelloApiKey}&token={cfg.TrelloToken}";
    }

    public async Task<bool> TestConnectionAsync(string apiKey, string token)
    {
        try
        {
            var url = $"https://api.trello.com/1/members/me?key={apiKey}&token={token}&fields=id";
            var resp = await _http.GetAsync(url);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<TrelloBoard>> GetBoardsAsync()
    {
        var url = $"https://api.trello.com/1/members/me/boards?{GetAuthParams()}&fields=name,id";
        var json = await _http.GetStringAsync(url);
        return JsonSerializer.Deserialize<List<TrelloBoard>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public async Task<List<TrelloList>> GetListsAsync(string boardId)
    {
        var url = $"https://api.trello.com/1/boards/{boardId}/lists?{GetAuthParams()}&fields=name,id";
        var json = await _http.GetStringAsync(url);
        return JsonSerializer.Deserialize<List<TrelloList>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public async Task<List<TrelloCard>> GetCardsAsync(string listId)
    {
        var fields = "name,desc,pos,due,dueComplete,labels,idMembers,badges,idAttachmentCover,shortUrl";
        var url = $"https://api.trello.com/1/lists/{listId}/cards?{GetAuthParams()}&fields={fields}";
        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var cards = new List<TrelloCard>();

        foreach(var elem in doc.RootElement.EnumerateArray())
        {
            var card = new TrelloCard
            {
                Id = elem.GetProperty("id").GetString() ?? "",
                Name = elem.GetProperty("name").GetString() ?? "",
                Description = elem.GetProperty("desc").GetString() ?? "",
                Pos = elem.TryGetProperty("pos", out var p) ? p.GetDouble() : 0,
                ShortUrl = elem.TryGetProperty("shortUrl", out var su) && su.ValueKind != JsonValueKind.Null ? su.GetString() ?? "" : "",
            };

            // Due Date
            if (elem.TryGetProperty("due", out var d) && d.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(d.GetString(), out var date))
                    card.DueDate = date.ToLocalTime();
            }

            // Checklist stats
            if (elem.TryGetProperty("badges", out var badges) && badges.ValueKind == JsonValueKind.Object)
            {
                if (badges.TryGetProperty("checkItems", out var ci)) card.ChecklistTotal = ci.GetInt32();
                if (badges.TryGetProperty("checkItemsChecked", out var cic)) card.ChecklistCompleted = cic.GetInt32();
                if (badges.TryGetProperty("attachments", out var ac)) card.AttachmentCount = ac.GetInt32();
            }
            
            // Cover color fallback
            if (elem.TryGetProperty("cover", out var cov) && cov.ValueKind == JsonValueKind.Object)
            {
                if (cov.TryGetProperty("color", out var clr) && clr.ValueKind != JsonValueKind.Null)
                {
                    var colorName = clr.GetString();
                    card.CoverColor = colorName switch {
                        "green" => "#4bce97",
                        "yellow" => "#e2b203",
                        "orange" => "#faa53d",
                        "red" => "#f87168",
                        "purple" => "#9f8fef",
                        "blue" => "#579dff",
                        "sky" => "#6cc3e0",
                        "lime" => "#94c748",
                        "pink" => "#e774bb",
                        "black" => "#8590a2",
                        _ => "#626f86"
                    };
                }
            }

            // Labels
            if (elem.TryGetProperty("labels", out var lbls))
            {
                foreach(var l in lbls.EnumerateArray())
                {
                    card.Labels.Add(new TrelloLabel {
                        Id = l.GetProperty("id").GetString() ?? "",
                        Name = l.GetProperty("name").GetString() ?? "",
                        Color = l.TryGetProperty("color", out var lc) && lc.ValueKind != JsonValueKind.Null ? lc.GetString() ?? "" : ""
                    });
                }
            }

            cards.Add(card);
        }

        return cards;
    }

    public async Task MoveCardAsync(string cardId, string listId)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}?idList={listId}&pos=bottom&{GetAuthParams()}";
        await _http.PutAsync(url, null);
    }

    public async Task UpdateCardPositionAsync(string cardId, double pos)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}?pos={pos}&{GetAuthParams()}";
        await _http.PutAsync(url, null);
    }

    public async Task ArchiveCardAsync(string cardId)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}?closed=true&{GetAuthParams()}";
        await _http.PutAsync(url, null);
    }
    
    public async Task SendCommentAsync(string cardId, string text)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}/actions/comments?text={Uri.EscapeDataString(text)}&{GetAuthParams()}";
        await _http.PostAsync(url, null);
    }

    public async Task<List<TrelloComment>> GetCommentsAsync(string cardId)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}/actions?filter=commentCard&{GetAuthParams()}";
        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var comments = new List<TrelloComment>();

        foreach (var action in doc.RootElement.EnumerateArray())
        {
            var data = action.GetProperty("data");
            var member = action.GetProperty("memberCreator");
            
            var text = data.GetProperty("text").GetString() ?? "";
            var c = new TrelloComment
            {
                Id = action.GetProperty("id").GetString() ?? "",
                Text = text.TrimEnd('\r', '\n', ' '), // Keep internal line breaks but trime start/end
                Date = DateTime.TryParse(action.GetProperty("date").GetString(), out var d) ? d.ToLocalTime() : DateTime.MinValue,
                MemberCreatorId = member.GetProperty("id").GetString() ?? "",
                MemberCreatorName = member.GetProperty("fullName").GetString() ?? "",
                MemberCreatorInitials = member.GetProperty("initials").GetString() ?? "",
            };
            
            if (member.TryGetProperty("avatarUrl", out var av) && av.ValueKind != JsonValueKind.Null)
                c.MemberCreatorAvatarUrl = $"{av.GetString()}/50.png";

            comments.Add(c);
        }
        return comments;
    }

    public async Task<List<TrelloChecklist>> GetChecklistsAsync(string cardId)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}/checklists?checkItems=all&{GetAuthParams()}";
        var json = await _http.GetStringAsync(url);
        var res = JsonSerializer.Deserialize<List<TrelloChecklist>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return res;
    }

    public async Task ToggleCheckItemAsync(string cardId, string checkItemId, bool isChecked)
    {
        var state = isChecked ? "complete" : "incomplete";
        var url = $"https://api.trello.com/1/cards/{cardId}/checkItem/{checkItemId}?state={state}&{GetAuthParams()}";
        await _http.PutAsync(url, null);
    }

    public async Task<(TrelloChecklist? checklist, string? rawJson)> DuplicateChecklistAsync(string cardId, string sourceChecklistId, string name)
    {
        // Create a new checklist on the card, copying from the source
        var url = $"https://api.trello.com/1/checklists?idCard={cardId}&name={Uri.EscapeDataString(name)}&idChecklistSource={sourceChecklistId}&{GetAuthParams()}";
        var resp = await _http.PostAsync(url, null);
        if (!resp.IsSuccessStatusCode) return (null, null);

        var json = await resp.Content.ReadAsStringAsync();
        var checklist = JsonSerializer.Deserialize<TrelloChecklist>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return (checklist, json);
    }

    public async Task<bool> UpdateCheckItemStateAsync(string cardId, string checkItemId, bool isComplete)
    {
        var state = isComplete ? "complete" : "incomplete";
        var url = $"https://api.trello.com/1/cards/{cardId}/checkItem/{checkItemId}?state={state}&{GetAuthParams()}";
        var resp = await _http.PutAsync(url, null);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteChecklistAsync(string checklistId)
    {
        var url = $"https://api.trello.com/1/checklists/{checklistId}?{GetAuthParams()}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<TrelloAttachment>> GetAttachmentsAsync(string cardId)
    {
        var url = $"https://api.trello.com/1/cards/{cardId}/attachments?{GetAuthParams()}";
        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var attachments = new List<TrelloAttachment>();

        foreach(var elem in doc.RootElement.EnumerateArray())
        {
            var name = elem.GetProperty("name").GetString() ?? "";
            var a = new TrelloAttachment
            {
                Id = elem.GetProperty("id").GetString() ?? "",
                Name = name,
                Url = elem.GetProperty("url").GetString() ?? "",
                MimeType = elem.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "" : "",
                Bytes = elem.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0,
            };
            
            if (a.MimeType.StartsWith("image/") || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                a.IsImage = true;
                
            if (elem.TryGetProperty("previews", out var prevs) && prevs.ValueKind == JsonValueKind.Array && prevs.GetArrayLength() > 0)
            {
                var p = prevs.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("width", out var w) && w.GetInt32() >= 300);
                if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("url", out var pu))
                {
                    a.PreviewUrl = pu.GetString() ?? "";
                }
            }

            attachments.Add(a);
        }
        
        return attachments;
    }

    public async Task<byte[]> DownloadAttachmentBytesAsync(string url)
    {
        var authUrl = url;
        if (url.Contains("trello.com"))
        {
             var sep = url.Contains('?') ? "&" : "?";
             authUrl = $"{url}{sep}Authorization=OAuth oauth_consumer_key=\"{_settings.Load().TrelloApiKey}\", oauth_token=\"{_settings.Load().TrelloToken}\"";
        }
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Authorization", $"OAuth oauth_consumer_key=\"{_settings.Load().TrelloApiKey}\", oauth_token=\"{_settings.Load().TrelloToken}\"");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }
}

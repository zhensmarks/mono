using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrelloCompact.Models;

public class TrelloBoard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public class TrelloList
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public class TrelloComment
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string MemberCreatorId { get; set; } = "";
    public string MemberCreatorName { get; set; } = "";
    public string MemberCreatorAvatarUrl { get; set; } = ""; 
    public string MemberCreatorInitials { get; set; } = "";
    public DateTime Date { get; set; }
}

public partial class TrelloChecklistItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    
    [ObservableProperty]
    private bool _isChecked;
    
    public string State 
    {
        get => IsChecked ? "complete" : "incomplete";
        set => IsChecked = value == "complete";
    } 
}

public class TrelloChecklist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string IdCard { get; set; } = "";
    public List<TrelloChecklistItem> CheckItems { get; set; } = new();
}

public partial class TrelloAttachment : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string PreviewUrl { get; set; } = ""; // Thumbnail
    public bool IsImage { get; set; }
    public long Bytes { get; set; }

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isDownloading;
}

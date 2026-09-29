using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BMachine.UI.Models;

public partial class TrelloComment : ObservableObject
{
    public string Id { get; set; } = "";
    
    [ObservableProperty]
    private string _text = "";
    
    public string MemberCreatorId { get; set; } = "";
    public string MemberCreatorName { get; set; } = "";
    public string MemberCreatorAvatarUrl { get; set; } = ""; // We might compute this or get from API
    public string MemberCreatorInitials { get; set; } = "";
    
    [ObservableProperty]
    private DateTime _date;

    // --- MVVM Editing Support ---
    [ObservableProperty]
    private bool _isMine;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = "";
}

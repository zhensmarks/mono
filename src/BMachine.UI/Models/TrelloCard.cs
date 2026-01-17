using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

public partial class TrelloCard : ObservableObject
{
    public string Id { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayId))]
    private string _name = "";

    // ...

    // Computed property for logic ID (prefix before _)
    public string DisplayId 
    {
        get 
        {
            if (string.IsNullOrEmpty(Name)) return "";
            var parts = Name.Split('_');
            if (parts.Length > 0 && parts[0].Length > 2) // Simple validation
            {
                return parts[0];
            }
            return ""; 
        }
    }
    
    public bool HasDisplayId => !string.IsNullOrEmpty(DisplayId);

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private DateTime? _dueDate;

    [ObservableProperty]
    private bool _isOverdue;

    [ObservableProperty]
    private string _labelsText = "";

    [ObservableProperty]
    private string _membersText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttachments))]
    private int? _attachmentCount;

    public bool HasAttachments => _attachmentCount.HasValue && _attachmentCount.Value > 0;

    [ObservableProperty]
    private bool _hasChecklist;
    
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isManual;

    [ObservableProperty]
    private bool _isSelected;

    // Computed property for UI display
    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("dd MMM") : "";
}

using System.Collections.ObjectModel;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChecklistTooltip))]
    private int _checklistTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChecklistTooltip))]
    private int _checklistCompleted;

    public bool IsChecklistComplete => ChecklistTotal > 0 && ChecklistCompleted == ChecklistTotal;

    // Checklist Data
    public List<string> ChecklistNames { get; set; } = new();

    public bool HasEditingChecklist => ChecklistNames.Any(n => n.Trim().StartsWith("#EDITING", StringComparison.OrdinalIgnoreCase));

    public string ChecklistTooltip 
    {
        get
        {
            if (HasEditingChecklist) return "Checklist OK";
            if (HasChecklist) return "Ada checklist, tapi bukan format #EDITING";
            return "Tidak ada checklist";
        }
    }

    [ObservableProperty]
    private bool _isActive;

    // Computed property for UI display
    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("dd MMM") : "";

    public void RefreshChecklistStatus()
    {
        OnPropertyChanged(nameof(HasEditingChecklist));
        OnPropertyChanged(nameof(ChecklistTooltip));
        OnPropertyChanged(nameof(HasChecklist));
    }

    public ObservableCollection<TrelloLabel> Labels { get; set; } = new();
    
    public string Url => !string.IsNullOrEmpty(Id) ? $"https://trello.com/c/{Id}" : "";
}

public class TrelloLabel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = ""; // Trello color name: green, yellow, etc.
    public string ColorHex => GetColorHex(Color);
    public string TextColorHex => GetTextColorHex(Color);

    private string GetColorHex(string trelloColor)
    {
        return trelloColor switch
        {
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
            _ => "#626f86" // Default gray
        };
    }
    
    private string GetTextColorHex(string trelloColor)
    {
         // Most Trello labels use dark text on pastel backgrounds, or white on dark?
         // Modern Trello uses dark text on these specific pastel shades, except maybe formatting.
         // Actually, let's keep it simple: #1d2125 (Dark) for most, maybe White for others if needed.
         // The hex codes above are standard Trello "light" tokens. Text is usually dark.
         return "#1d2125"; 
    }
}

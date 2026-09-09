using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

public partial class TrelloCard : ObservableObject
{
    public string Id { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayId))]
    private string _name = "";

    [ObservableProperty]
    private double _pos;

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
    [NotifyPropertyChangedFor(nameof(EventDateText))]
    [NotifyPropertyChangedFor(nameof(HasEventDate))]
    private string _description = "";

    /// <summary>
    /// Extracts the event date from the description (e.g. "TANGGAL EVENT: SABTU 14 FEBRUARY 2026")
    /// </summary>
    public string EventDateText
    {
        get
        {
            if (string.IsNullOrEmpty(Description)) return "";
            var match = Regex.Match(Description, @"TANGGAL EVENT\s*:\s*(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
    }

    public bool HasEventDate => !string.IsNullOrEmpty(EventDateText);

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
    private bool _isAcc; // Flag to indicate if card is in ACC state

    [ObservableProperty]
    private bool _isSeparator; // Flag to indicate if this is a dummy separator card

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
    
    // --- Cover Logic ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    [NotifyPropertyChangedFor(nameof(IsCoverImage))]
    private string _coverColor = ""; // Hex string

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    [NotifyPropertyChangedFor(nameof(IsCoverImage))]
    private string _coverUrl = "";

    // Attachment filename used as cover (e.g. "BOGOR_1_(NISSA).jpg")
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverDisplayName))]
    [NotifyPropertyChangedFor(nameof(HasCoverDisplayName))]
    private string _coverAttachmentName = "";

    // Cleaned display name: no extension, underscores replaced with spaces
    public string CoverDisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(CoverAttachmentName)) return "";
            // Remove extension
            var name = System.IO.Path.GetFileNameWithoutExtension(CoverAttachmentName);
            // Replace underscores with spaces
            name = name.Replace('_', ' ');
            return name.Trim();
        }
    }

    public bool HasCoverDisplayName => !string.IsNullOrEmpty(CoverDisplayName);

    // Loaded Bitmap (Not persisted)
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _coverImage;

    public bool HasCover => !string.IsNullOrEmpty(CoverColor) || !string.IsNullOrEmpty(CoverUrl);
    public bool IsCoverImage => !string.IsNullOrEmpty(CoverUrl);

    public string Url => !string.IsNullOrEmpty(Id) ? $"https://trello.com/c/{Id}" : "";
}

public class TrelloLabel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = ""; // Trello color name: green, yellow, etc.
    public string ColorHex => BorderColorHex;
    public string BorderColorHex => GetBorderColorHex(Color);
    public string BgColorHex => GetBgColorHex(Color);
    public string TextColorHex => GetTextColorHex(Color);

    public static string GetColorHex(string? trelloColor) => GetBorderColorHex(trelloColor);

    public static string GetBorderColorHex(string? trelloColor)
    {
        if (!string.IsNullOrEmpty(trelloColor) && trelloColor.StartsWith("#") && trelloColor.Length == 7)
        {
            return trelloColor;
        }

        return trelloColor?.ToLowerInvariant() switch
        {
            "green" => "#22C55E",
            "yellow" => "#EAB308",
            "orange" => "#F97316",
            "red" => "#EF4444",
            "purple" => "#A855F7",
            "blue" => "#3B82F6",
            "sky" => "#0EA5E9",
            "lime" => "#84CC16",
            "pink" => "#EC4899",
            "black" => "#64748B",
            _ => "#64748B"
        };
    }

    public static string GetBgColorHex(string? trelloColor)
    {
        var border = GetBorderColorHex(trelloColor);
        return border.StartsWith("#") && border.Length == 7 ? $"#14{border.Substring(1)}" : "#1464748B";
    }

    public static string GetTextColorHex(string? trelloColor)
    {
        if (!string.IsNullOrEmpty(trelloColor) && trelloColor.StartsWith("#") && trelloColor.Length == 7)
        {
            return trelloColor;
        }

        return trelloColor?.ToLowerInvariant() switch
        {
            "green" => "#4ADE80",
            "yellow" => "#FDE047",
            "orange" => "#FB923C",
            "red" => "#F87168",
            "purple" => "#C084FC",
            "blue" => "#60A5FA",
            "sky" => "#38BDF8",
            "lime" => "#A3E635",
            "pink" => "#F472B6",
            "black" => "#94A3B8",
            _ => "#94A3B8"
        };
    }
}

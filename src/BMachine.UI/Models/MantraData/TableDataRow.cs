using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models.MantraData;

public enum MatchDecisionStatus
{
    Confirmed,
    Likely,
    Ambiguous,
    NotFound,
    Conflict
}

public sealed class MatchDecision
{
    public MatchDecisionStatus Status { get; init; }
    public int Confidence { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string CandidatePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Alternatives { get; init; } = System.Array.Empty<string>();
}

public partial class TableDataRow : ObservableObject
{
    [ObservableProperty]
    private int _rowNumber;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _tagColor = "#00000000"; // Hex ARGB for row highlighting

    [ObservableProperty]
    private string _matchedPhoto = "-";

    [ObservableProperty]
    private int _matchScore = 0;

    [ObservableProperty]
    private bool _isPhotoMatched = false;

    [ObservableProperty]
    private string _matchStatus = "BELUM DIPERIKSA";

    [ObservableProperty]
    private string _matchNote = string.Empty;

    [ObservableProperty]
    private string _matchCandidate = string.Empty;

    public void NotifyPhotoPreviewChanged()
    {
        OnPropertyChanged(nameof(MatchedPhoto));
        OnPropertyChanged("Item[]");
    }

    public bool IsForced { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string MatchReason { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public string SourceRowId { get; set; } = string.Empty;
    public string RawName { get; set; } = string.Empty;
    public string CleanName { get; set; } = string.Empty;
    public MatchDecisionStatus Decision { get; set; } = MatchDecisionStatus.NotFound;
    public bool NeedsConfirmation { get; set; } = true;


    public Dictionary<string, string> Values { get; set; } = new();

    public string this[string columnName]
    {
        get => Values.TryGetValue(columnName, out var v) ? v : string.Empty;
        set
        {
            Values[columnName] = value;
            OnPropertyChanged("Item[]");
            OnPropertyChanged(nameof(Values));
        }
    }
}

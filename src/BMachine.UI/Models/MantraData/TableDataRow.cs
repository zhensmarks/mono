using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models.MantraData;

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

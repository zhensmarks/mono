using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

public partial class MasterFileGroup : ObservableObject
{
    public string GroupName { get; set; } = "";
    
    [ObservableProperty]
    private bool _isExpanded = true;
    
    public ObservableCollection<MasterFileItem> Files { get; set; } = new();

    public MasterFileGroup(string groupName)
    {
        GroupName = groupName;
    }
}

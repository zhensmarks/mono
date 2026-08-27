using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.ViewModels;

public partial class EditablePathItem : ObservableObject
{
    [ObservableProperty]
    private string _path;

    public EditablePathItem(string path)
    {
        _path = path;
    }
}

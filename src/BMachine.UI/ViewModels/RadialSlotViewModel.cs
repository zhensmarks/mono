using System;
using CommunityToolkit.Mvvm.ComponentModel;
using BMachine.UI.Models;
using Avalonia.Media;
using Avalonia;

namespace BMachine.UI.ViewModels;

public partial class RadialSlotViewModel : ObservableObject
{
    [ObservableProperty] private int _slotIndex; // 0 to 7 (relative to page)
    [ObservableProperty] private int _absoluteIndex; // 0 to 15
    [ObservableProperty] private int _pageIndex; // 0 or 1
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ScriptItem? _script;
    
    public bool IsEmpty => Script == null && !IsNavigationReserved;
    
    [ObservableProperty] private double _angle;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    
    [ObservableProperty] private bool _isSelected;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isNavigationReserved;
    
    [ObservableProperty] private string _navigationLabel = "";
}

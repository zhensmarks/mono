using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public enum ToolboxTab
{
    PsdBucin,
    MantraNama,
    MantraGanda
}

public partial class ToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private ToolboxTab _currentTab = ToolboxTab.PsdBucin;

    public PsdBucinViewModel PsdBucinVM { get; } = new();
    public MantraNamaViewModel MantraNamaVM { get; } = new();
    public MantraGandaViewModel MantraGandaVM { get; } = new();

    public ToolboxViewModel()
    {
    }

    [RelayCommand]
    public void SwitchTab(ToolboxTab tab)
    {
        CurrentTab = tab;
    }

    partial void OnCurrentTabChanged(ToolboxTab value)
    {
        OnPropertyChanged(nameof(IsPsdBucinVisible));
        OnPropertyChanged(nameof(IsMantraNamaVisible));
        OnPropertyChanged(nameof(IsMantraGandaVisible));
    }

    public bool IsPsdBucinVisible 
    { 
        get => CurrentTab == ToolboxTab.PsdBucin; 
        set { if(value) CurrentTab = ToolboxTab.PsdBucin; } 
    }
    
    public bool IsMantraNamaVisible 
    { 
        get => CurrentTab == ToolboxTab.MantraNama; 
        set { if(value) CurrentTab = ToolboxTab.MantraNama; } 
    }
    
    public bool IsMantraGandaVisible 
    { 
        get => CurrentTab == ToolboxTab.MantraGanda; 
        set { if(value) CurrentTab = ToolboxTab.MantraGanda; } 
    }
}

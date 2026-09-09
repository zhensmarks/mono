using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

public enum ToolboxTab
{
    PsdBucin,
    MantraNama,
    MantraGanda,
    MantraData
}

public partial class ToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private ToolboxTab _currentTab = ToolboxTab.PsdBucin;

    public PsdBucinViewModel PsdBucinVM { get; } = new();
    public MantraNamaViewModel MantraNamaVM { get; } = new();
    public MantraGandaViewModel MantraGandaVM { get; } = new();
    public MantraDataViewModel MantraDataVM { get; }

    public ToolboxViewModel()
    {
        var db = new BMachine.Core.Database.DatabaseService();
        MantraDataVM = new MantraDataViewModel(db);
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
        OnPropertyChanged(nameof(IsMantraDataVisible));
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

    public bool IsMantraDataVisible
    {
        get => CurrentTab == ToolboxTab.MantraData;
        set { if(value) CurrentTab = ToolboxTab.MantraData; }
    }
}

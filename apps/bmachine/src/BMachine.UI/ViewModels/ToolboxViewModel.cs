using System;
using System.Collections.ObjectModel;
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

/// <summary>
/// Satu kartu modul di layar depan Toolbox (launcher).
/// Tampilan kartu hanya membaca daftar ini: menambah/mengurangi modul = ubah
/// daftar di ToolboxViewModel.Modules, grid menyesuaikan sendiri.
/// </summary>
public sealed class ToolboxModule
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolboxTab TargetTab { get; init; }

    /// <summary>Modul nonaktif (mis. maintenance) tetap tampil, tapi tidak bisa dibuka.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>blue | teal | violet | gray - warna kotak ikon. Amber dikunci untuk status maintenance.</summary>
    public string Tint { get; init; } = "blue";

    /// <summary>layers | type | copy | db - ikon dari Styles/TablerIcons.axaml.</summary>
    public string Icon { get; init; } = "layers";

    public bool IsMaintenance => !IsEnabled;

    public bool IsBlue => Tint == "blue";
    public bool IsTeal => Tint == "teal";
    public bool IsViolet => Tint == "violet";
    public bool IsGray => Tint == "gray";

    public bool IsIconLayers => Icon == "layers";
    public bool IsIconType => Icon == "type";
    public bool IsIconCopy => Icon == "copy";
    public bool IsIconDatabase => Icon == "db";
}

public partial class ToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private ToolboxTab _currentTab = ToolboxTab.PsdBucin;

    /// <summary>Layar depan (launcher) aktif. false = sedang di dalam salah satu modul.</summary>
    [ObservableProperty]
    private bool _isHomeVisible = true;

    /// <summary>Kartu modul launcher; satu sumber data untuk seluruh tampilan depan Toolbox.</summary>
    public ObservableCollection<ToolboxModule> Modules { get; } = new();

    public PsdBucinViewModel PsdBucinVM { get; } = new();
    public MantraNamaViewModel MantraNamaVM { get; } = new();
    public MantraGandaViewModel MantraGandaVM { get; } = new();
    public MantraDataViewModel MantraDataVM { get; }

    public ToolboxViewModel()
    {
        var db = new BMachine.Core.Database.DatabaseService();
        MantraDataVM = new MantraDataViewModel(db);

        Modules.Add(new ToolboxModule
        {
            Name = "MANTRA BUCIN",
            Description = "Otomatis generate master PSD dari folder foto.",
            TargetTab = ToolboxTab.PsdBucin,
            Tint = "blue",
            Icon = "layers"
        });
        Modules.Add(new ToolboxModule
        {
            Name = "MANTRA NAMA",
            Description = "Cocokkan & ganti nama antar panel master PSD.",
            TargetTab = ToolboxTab.MantraNama,
            Tint = "teal",
            Icon = "type"
        });
        Modules.Add(new ToolboxModule
        {
            Name = "MANTRA GANDA",
            Description = "Gandakan master PSD ke banyak file / folder.",
            TargetTab = ToolboxTab.MantraGanda,
            Tint = "violet",
            Icon = "copy"
        });
        PsdBucinVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PsdBucinViewModel.IsManualModeActive))
                OnPropertyChanged(nameof(IsBreadcrumbVisible));
        };

        Modules.Add(new ToolboxModule
        {
            Name = "MANTRA DATA",
            Description = "Kelola data & manifest batch.",
            TargetTab = ToolboxTab.MantraData,
            Tint = "gray",
            Icon = "db",
            IsEnabled = false
        });
    }

    [RelayCommand]
    public void SwitchTab(ToolboxTab tab)
    {
        CurrentTab = tab;
    }

    /// <summary>Buka modul dari kartu launcher. Modul nonaktif diabaikan.</summary>
    [RelayCommand]
    public void OpenModule(ToolboxModule? module)
    {
        if (module is null || !module.IsEnabled) return;
        CurrentTab = module.TargetTab;
        IsHomeVisible = false;
    }

    /// <summary>Breadcrumb hanya tampil di dalam modul, bukan di launcher dan bukan saat viewer manual.</summary>
    public bool IsBreadcrumbVisible => !IsHomeVisible && !PsdBucinVM.IsManualModeActive;

    /// <summary>Kembali dari modul ke layar depan (launcher).</summary>
    [RelayCommand]
    public void GoHome()
    {
        IsHomeVisible = true;
    }

    partial void OnIsHomeVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
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

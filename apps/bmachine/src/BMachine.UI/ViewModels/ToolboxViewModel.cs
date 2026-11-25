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
/// Menambah/mengurangi modul = ubah daftar di ToolboxViewModel.Modules; grid menyesuaikan sendiri.
/// Status proses (IsRunning / progress) hidup di kartu, sehingga kartu tetap bisa
/// memperlihatkan proses yang jalan di background walau popup modulnya sudah ditutup.
/// </summary>
public sealed partial class ToolboxModule : ObservableObject
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

    /// <summary>Modul ini punya progress numerik (Mantra Nama tidak).</summary>
    public bool HasNumericProgress { get; init; } = true;

    public bool IsMaintenance => !IsEnabled;

    // -- Status proses (disinkronkan dari ViewModel modul) --

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 1;

    /// <summary>Tombol kartu: OPEN saat idle, LIHAT saat prosesnya jalan di background.</summary>
    public bool ShowOpenPill => IsEnabled && !IsRunning;
    public bool ShowRunningPill => IsEnabled && IsRunning;
    public bool ShowLockedPill => IsMaintenance;

    public bool IsBlue => Tint == "blue";
    public bool IsTeal => Tint == "teal";
    public bool IsViolet => Tint == "violet";
    public bool IsGray => Tint == "gray";

    public bool IsIconLayers => Icon == "layers";
    public bool IsIconType => Icon == "type";
    public bool IsIconCopy => Icon == "copy";
    public bool IsIconDatabase => Icon == "db";

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOpenPill));
        OnPropertyChanged(nameof(ShowRunningPill));
    }

    partial void OnProgressValueChanged(int value) => OnPropertyChanged(nameof(ShowRunningPill));
    partial void OnProgressMaxChanged(int value) => OnPropertyChanged(nameof(ShowRunningPill));
}

public partial class ToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private ToolboxTab _currentTab = ToolboxTab.PsdBucin;

    /// <summary>Layar depan (launcher) aktif.</summary>
    [ObservableProperty]
    private bool _isHomeVisible = true;

    /// <summary>
    /// Popup modul sedang terbuka di atas launcher.
    /// false = popup tertutup, TAPI proses yang sudah jalan tetap lanjut di background.
    /// </summary>
    [ObservableProperty]
    private bool _isOverlayOpen = false;

    /// <summary>Kartu modul launcher; satu sumber data untuk seluruh tampilan depan Toolbox.</summary>
    public ObservableCollection<ToolboxModule> Modules { get; } = new();

    public PsdBucinViewModel PsdBucinVM { get; } = new();
    public MantraNamaViewModel MantraNamaVM { get; } = new();
    public MantraGandaViewModel MantraGandaVM { get; } = new();
    public MantraDataViewModel MantraDataVM { get; }

    private readonly ToolboxModule _bucinCard;
    private readonly ToolboxModule _namaCard;
    private readonly ToolboxModule _gandaCard;

    public ToolboxViewModel()
    {
        var db = new BMachine.Core.Database.DatabaseService();
        MantraDataVM = new MantraDataViewModel(db);

        _bucinCard = new ToolboxModule
        {
            Name = "MANTRA BUCIN",
            Description = "Otomatis generate master PSD dari folder foto.",
            TargetTab = ToolboxTab.PsdBucin,
            Tint = "blue",
            Icon = "layers"
        };
        _namaCard = new ToolboxModule
        {
            Name = "MANTRA NAMA",
            Description = "Cocokkan & ganti nama antar panel master PSD.",
            TargetTab = ToolboxTab.MantraNama,
            Tint = "teal",
            Icon = "type",
            HasNumericProgress = false
        };
        _gandaCard = new ToolboxModule
        {
            Name = "MANTRA GANDA",
            Description = "Gandakan master PSD ke banyak file / folder.",
            TargetTab = ToolboxTab.MantraGanda,
            Tint = "violet",
            Icon = "copy"
        };

        Modules.Add(_bucinCard);
        Modules.Add(_namaCard);
        Modules.Add(_gandaCard);

        // MANTRA DATA sengaja nonaktif: tampil redup + badge MAINTENANCE.
        Modules.Add(new ToolboxModule
        {
            Name = "MANTRA DATA",
            Description = "Kelola data & manifest batch.",
            TargetTab = ToolboxTab.MantraData,
            Tint = "gray",
            Icon = "db",
            IsEnabled = false
        });

        // Propagasi IsBreadcrumbVisible saat manual mode berubah
        PsdBucinVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PsdBucinViewModel.IsManualModeActive))
                OnPropertyChanged(nameof(IsBreadcrumbVisible));
        };

        // Sinkronkan status proses ke kartu launcher (indikator background)
        PsdBucinVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PsdBucinViewModel.IsProcessing))
            {
                _bucinCard.IsRunning = PsdBucinVM.IsProcessing;
                OnPropertyChanged(nameof(IsAnyRunning));
                OnPropertyChanged(nameof(BackgroundSummary));
            }
            else if (e.PropertyName == nameof(PsdBucinViewModel.ProgressValue))
            {
                _bucinCard.ProgressValue = PsdBucinVM.ProgressValue;
                OnPropertyChanged(nameof(BackgroundSummary));
            }
            else if (e.PropertyName == nameof(PsdBucinViewModel.ProgressMaximum))
            {
                _bucinCard.ProgressMax = Math.Max(1, PsdBucinVM.ProgressMaximum);
            }
        };

        MantraGandaVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MantraGandaViewModel.IsProcessing))
            {
                _gandaCard.IsRunning = MantraGandaVM.IsProcessing;
                OnPropertyChanged(nameof(IsAnyRunning));
                OnPropertyChanged(nameof(BackgroundSummary));
            }
            else if (e.PropertyName == nameof(MantraGandaViewModel.ProgressValue))
            {
                _gandaCard.ProgressValue = MantraGandaVM.ProgressValue;
                OnPropertyChanged(nameof(BackgroundSummary));
            }
            else if (e.PropertyName == nameof(MantraGandaViewModel.ProgressMaximum))
            {
                _gandaCard.ProgressMax = Math.Max(1, MantraGandaVM.ProgressMaximum);
            }
        };

        MantraNamaVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MantraNamaViewModel.IsProcessing))
            {
                _namaCard.IsRunning = MantraNamaVM.IsProcessing;
                OnPropertyChanged(nameof(IsAnyRunning));
                OnPropertyChanged(nameof(BackgroundSummary));
            }
        };
    }

    // -- Indikator background per modul --

    public bool IsBucinRunning => _bucinCard.IsRunning;
    public bool IsGandaRunning => _gandaCard.IsRunning;
    public bool IsNamaRunning => _namaCard.IsRunning;

    /// <summary>Ada proses apa pun yang jalan di background (dipakai footer status).</summary>
    public bool IsAnyRunning => IsBucinRunning || IsGandaRunning || IsNamaRunning;

    /// <summary>Ringkasan proses background untuk footer launcher.</summary>
    public string BackgroundSummary
    {
        get
        {
            if (!IsAnyRunning) return "SIAP";

            var parts = new System.Collections.Generic.List<string>();
            if (_bucinCard.IsRunning)
                parts.Add($"BUCIN {_bucinCard.ProgressValue}/{_bucinCard.ProgressMax}");
            if (_namaCard.IsRunning)
                parts.Add("NAMA BERJALAN");
            if (_gandaCard.IsRunning)
                parts.Add($"GANDA {_gandaCard.ProgressValue}/{_gandaCard.ProgressMax}");
            return string.Join("   \u2022   ", parts);
        }
    }

    // -- Navigasi --

    [RelayCommand]
    public void SwitchTab(ToolboxTab tab)
    {
        CurrentTab = tab;
    }

    /// <summary>
    /// Buka modul dari kartu launcher sebagai popup di atas launcher.
    /// Proses lama di background tidak terganggu.
    /// </summary>
    [RelayCommand]
    public void OpenModule(ToolboxModule? module)
    {
        if (module is null || !module.IsEnabled) return;
        CurrentTab = module.TargetTab;
        IsOverlayOpen = true;
    }

    /// <summary>
    /// Tutup popup - launcher grid kembali ke depan.
    /// Proses yang sedang berjalan TIDAK dihentikan; lanjut di background
    /// dan statusnya muncul di kartu modul.
    /// </summary>
    [RelayCommand]
    public void CloseOverlay()
    {
        IsOverlayOpen = false;
    }

    /// <summary>Judul modul yang tampil di header popup.</summary>
    public string CurrentModuleTitle => CurrentTab switch
    {
        ToolboxTab.PsdBucin => "MANTRA BUCIN",
        ToolboxTab.MantraNama => "MANTRA NAMA",
        ToolboxTab.MantraGanda => "MANTRA GANDA",
        ToolboxTab.MantraData => "MANTRA DATA",
        _ => "MODUL"
    };

    /// <summary>Breadcrumb hanya tampil di dalam popup modul, bukan di launcher dan bukan saat viewer manual.</summary>
    public bool IsBreadcrumbVisible => IsOverlayOpen && !PsdBucinVM.IsManualModeActive;

    /// <summary>Kembali dari modul ke launcher - alias CloseOverlay.</summary>
    [RelayCommand]
    public void GoHome()
    {
        IsOverlayOpen = false;
    }

    partial void OnIsOverlayOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
        IsHomeVisible = !value;
    }

    partial void OnIsHomeVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
    }

    partial void OnCurrentTabChanged(ToolboxTab value)
    {
        OnPropertyChanged(nameof(CurrentModuleTitle));
        OnPropertyChanged(nameof(IsPsdBucinVisible));
        OnPropertyChanged(nameof(IsMantraNamaVisible));
        OnPropertyChanged(nameof(IsMantraGandaVisible));
        OnPropertyChanged(nameof(IsMantraDataVisible));
    }

    public bool IsPsdBucinVisible
    {
        get => CurrentTab == ToolboxTab.PsdBucin;
        set { if (value) CurrentTab = ToolboxTab.PsdBucin; }
    }

    public bool IsMantraNamaVisible
    {
        get => CurrentTab == ToolboxTab.MantraNama;
        set { if (value) CurrentTab = ToolboxTab.MantraNama; }
    }

    public bool IsMantraGandaVisible
    {
        get => CurrentTab == ToolboxTab.MantraGanda;
        set { if (value) CurrentTab = ToolboxTab.MantraGanda; }
    }

    public bool IsMantraDataVisible
    {
        get => CurrentTab == ToolboxTab.MantraData;
        set { if (value) CurrentTab = ToolboxTab.MantraData; }
    }
}

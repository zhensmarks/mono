using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.ViewModels;

public partial class MantraContextMenuItemConfig : ObservableObject
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private string _shortcutKey = string.Empty;

    public MantraContextMenuItemConfig() { }

    public MantraContextMenuItemConfig(string key, string name, string category, bool isVisible, string shortcutKey)
    {
        Key = key;
        Name = name;
        Category = category;
        IsVisible = isVisible;
        ShortcutKey = shortcutKey;
    }
}

public partial class MantraContextMenuSettingsViewModel : ObservableObject
{
    private readonly MantraDataSettings _settings;

    [ObservableProperty]
    private ObservableCollection<MantraContextMenuItemConfig> _items = new();

    public static readonly IReadOnlyList<(string Key, string Name, string Category, string DefaultShortcut)> DefaultMenuItems = new List<(string, string, string, string)>
    {
        ("Copy", "Salin", "Clipboard & Edit", "Ctrl+C"),
        ("Paste", "Tempel", "Clipboard & Edit", "Ctrl+V"),
        ("ClearCells", "Kosongkan Sel", "Clipboard & Edit", "Delete"),

        ("CustomMerge", "Gabung Kolom...", "Olah Data", ""),
        ("TitleCase", "Rapikan Teks (Title Case)", "Olah Data", ""),
        ("DateFormatFull", "Format Tanggal (DD MMMM YYYY)", "Olah Data", ""),
        ("PhonePrefix", "Format No. HP (+62 / 08)", "Olah Data", ""),
        ("FindReplace", "Cari dan Ganti...", "Olah Data", "Ctrl+F"),
        ("SplitColumn", "Pisah Kolom (Split)...", "Olah Data", ""),

        ("InsertColumnLeft", "Sisipkan Kolom di Kiri", "Kelola Kolom", ""),
        ("InsertColumnRight", "Sisipkan Kolom di Kanan", "Kelola Kolom", ""),
        ("MoveColumnLeft", "Pindahkan Kolom ke Kiri", "Kelola Kolom", ""),
        ("MoveColumnRight", "Pindahkan Kolom ke Kanan", "Kelola Kolom", ""),
        ("RenameColumn", "Ganti Nama Kolom...", "Kelola Kolom", ""),
        ("DeleteColumn", "Hapus Kolom Ini", "Kelola Kolom", ""),
        ("AutoFit", "Otomatis Ukuran Kolom", "Kelola Kolom", ""),

        ("InsertRow", "Tambah Baris Baru", "Kelola Baris", ""),
        ("DeleteRows", "Hapus Baris Terpilih", "Kelola Baris", ""),

        ("ColorTag", "Tandai Baris (Warna)", "Tanda Warna", ""),
        ("ColorGreen", "Hijau (Valid)", "Tanda Warna", ""),
        ("ColorBlue", "Biru (Info)", "Tanda Warna", ""),
        ("ColorAmber", "Kuning (Periksa)", "Tanda Warna", ""),
        ("ColorRed", "Merah (Error)", "Tanda Warna", ""),
        ("ColorClear", "Hapus Tanda Warna", "Tanda Warna", ""),
    };

    public MantraContextMenuSettingsViewModel(MantraDataSettings settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
        Items.Clear();
        foreach (var def in DefaultMenuItems)
        {
            bool isVis = _settings.GetMenuVisibility(def.Key);
            string sc = _settings.GetMenuShortcut(def.Key);
            Items.Add(new MantraContextMenuItemConfig(def.Key, def.Name, def.Category, isVis, sc));
        }
    }

    public void SaveToSettings()
    {
        _settings.MenuVisibility.Clear();
        _settings.MenuShortcuts.Clear();
        foreach (var item in Items)
        {
            _settings.MenuVisibility[item.Key] = item.IsVisible;
            _settings.MenuShortcuts[item.Key] = item.ShortcutKey?.Trim() ?? string.Empty;
        }
        _settings.Save();
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        Items.Clear();
        foreach (var def in DefaultMenuItems)
        {
            Items.Add(new MantraContextMenuItemConfig(def.Key, def.Name, def.Category, true, def.DefaultShortcut));
        }
    }
}

using System;
using System.Linq;
using Avalonia.Controls;

namespace PixelcutCompact.Views;

/// <summary>
/// Hook TANPA UI untuk menguji workspace editor secara otomatis (harness headless).
/// Hanya memanggil method internal yang sudah ada; tidak mengubah perilaku produksi.
/// </summary>
public partial class PreviewWindow
{
    /// <summary>Masuk mode edit (tanpa butuh klik). True kalau berhasil.</summary>
    public bool EditorTestEnterEdit()
    {
        EnterEditMode();
        return _editMode;
    }

    /// <summary>Set visibilitas panel seperti lewat menu Window, lalu terapkan.</summary>
    public void EditorTestSetPanelVisible(string key, bool visible)
    {
        _settings.EditorPanelVisibility[key] = visible;
        SyncPanelMenuChecks();
        ApplyEditorDockVisibility();
    }

    /// <summary>Pindahkan panel ke sisi dock tertentu, lalu terapkan.</summary>
    public void EditorTestSetDock(string setting, string position)
    {
        switch (setting)
        {
            case "Tools": _settings.EditorToolsDock = NormalizeToolDock(position); break;
            case "Properties": _settings.EditorPropertiesDock = NormalizeDock(position); break;
            case "History": _settings.EditorHistoryDock = NormalizeDock(position); break;
            case "Layers": _settings.EditorLayersDock = NormalizeDock(position); break;
            default: return;
        }
        ArrangeDockedPanels();
        SyncDockCombos();
        ApplyEditorDockVisibility();
    }

    /// <summary>Laporan visibilitas tiap panel/host (untuk assertion teks).</summary>
    public string EditorTestVisibilityReport()
    {
        string V(string n) => (this.FindControl<Control>(n)?.IsVisible ?? false) ? "VISIBLE" : "hidden";
        string Host(string n) => (this.FindControl<StackPanel>(n)?.Children.Count ?? 0).ToString();
        return string.Join(" | ",
            $"ToolRail={V("PanelToolRail")}",
            $"OptionsBar={V("PanelOptionsBar")}",
            $"RightDock={V("PanelRightEditor")}",
            $"Props={V("PanelProperties")}",
            $"History={V("PanelHistory")}",
            $"Layers={V("PanelDocLayers")}",
            $"TopHost={V("DockTopHost")}",
            $"LeftHost={V("DockLeftHost")}",
            $"BottomHost={V("DockBottomHost")}",
            $"LayersInTop={Host("DockTopPanel")}",
            $"LayersInRight={Host("DockRightPanel")}",
            $"LayersInLeft={Host("DockLeftPanel")}",
            $"LayersInBottom={Host("DockBottomPanel")}");
    }

    /// <summary>True bila semua panel utama terlihat (dipakai untuk gate langkah tes).</summary>
    public bool EditorTestIsEditMode => _editMode;

    /// <summary>Laporan isi panel Layers (jumlah item + nama) untuk assertion.</summary>
    public string EditorTestLayerReport()
    {
        var list = this.FindControl<ItemsControl>("LayerList");
        int n = 0;
        var names = new System.Collections.Generic.List<string>();
        if (list?.ItemsSource is System.Collections.IEnumerable en)
        {
            foreach (var it in en)
            {
                n++;
                var nm = it?.GetType().GetProperty("Name")?.GetValue(it)?.ToString();
                if (!string.IsNullOrEmpty(nm)) names.Add(nm!);
            }
        }
        return $"LayerCount={n} Names=[{string.Join(",", names)}]";
    }
}

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PixelcutCompact.Views;

/// <summary>
/// Layout TETAP mode edit ala Photoshop/Affinity/Compositor:
/// menu bar → options bar kontekstual → rail tools kiri (vertikal, hanya tools edit)
/// → kanvas tengah → panel kanan → status bar bawah.
/// Tidak ada docking/pindah panel: satu hierarki yang selalu sama supaya user tidak bingung.
/// Logika session/mask/composite tidak disentuh di sini (murni presentation).
/// </summary>
public partial class PreviewWindow
{
    /// <summary>Photoshop: Tab menyembunyikan/menampilkan semua panel.</summary>
    private bool _tabPanelsHidden;

    // Visibilitas per panel dari menu Window. Key yang tidak ada = tampil (default ala Photoshop).
    // Sumber kebenaran = _settings.EditorPanelVisibility supaya pilihan bertahan lintas sesi.
    private Dictionary<string, bool> PanelVisibility => _settings.EditorPanelVisibility;

    private static readonly (string Name, string Key)[] PanelMenuItems =
    {
        ("MiWindowToolBox", "toolrail"),
        ("MiWindowOptionsBar", "optionsbar"),
        ("MiWindowProperties", "properties"),
        ("MiWindowHistory", "history"),
        ("MiWindowLayers", "layers"),
        ("MiWindowRightDock", "rightdock"),
    };

    /// <summary>Menu Window: centang = panel tampil, tidak centang = panel disembunyikan.</summary>
    private void OnTogglePanelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        var key = item.Tag as string;
        if (string.IsNullOrEmpty(key)) return;

        // ToggleType="CheckBox" sudah membalik IsChecked SEBELUM event Click naik
        // (lihat DefaultMenuInteractionHandler), jadi IsChecked di sini = nilai baru.
        PanelVisibility[key] = item.IsChecked;
        _settings.Save();
        ApplyEditorDockVisibility();
    }

    private void ConfigureEditorWorkspace()
    {
        // Selaraskan centang menu Window dengan nilai tersimpan; panel yang belum
        // pernah di-toggle dianggap tampil (default ala Photoshop).
        SyncPanelMenuChecks();
        ApplyEditorDockVisibility();
    }

    /// <summary>Samakan IsChecked tiap item menu Window dengan visibilitas tersimpan.</summary>
    private void SyncPanelMenuChecks()
    {
        foreach (var (name, key) in PanelMenuItems)
        {
            if (this.FindControl<MenuItem>(name) is { } mi)
                mi.IsChecked = Visible(key);
        }
    }

    private void ApplyEditorDockVisibility()
    {
        if (!_editMode) return;
        bool show = !_tabPanelsHidden;
        SetVisible("PanelToolRail", show && Visible("toolrail"));
        SetVisible("PanelOptionsBar", show && Visible("optionsbar"));
        SetVisible("PanelMenuBar", true);
        if (this.FindControl<Border>("PanelRightEditor") is { } right)
            right.IsVisible = show && Visible("rightdock") && _settings.EditorShowRightPanel;
        // Panel Layers ditampilkan kembali (restorasi minimal Putaran 2);
        // konten diisi oleh DocSession saat tersedia.
        SetVisible("PanelDocLayers", show && Visible("layers"));

        // Properties/History hidup di dalam dock kanan: sembunyikan per-panel,
        // dock-nya tetap berdiri supaya Properties dan History bisa di-toggle sendiri.
        // Hormati juga "sembunyikan semua panel" (Tab) agar konsisten dengan rail/dock.
        SetVisible("PanelProperties", show && Visible("properties"));
        SetVisible("PanelHistory", show && Visible("history"));
    }

    /// <summary>Key yang tidak ada di dictionary = tampil (default ala Photoshop).</summary>
    private bool Visible(string key) => !PanelVisibility.TryGetValue(key, out var on) || on;

    /// <summary>Photoshop: Tab = sembunyikan/tampilkan rail + options bar + dock kanan.</summary>
    private void ToggleEditorPanels()
    {
        if (!_editMode) return;
        _tabPanelsHidden = !_tabPanelsHidden;
        ApplyEditorDockVisibility();
    }

    private void SetEditorWorkspaceActive(bool active)
    {
        SetVisible("PanelMenuBar", active);
        if (active)
        {
            ApplyEditorDockVisibility();
            return;
        }

        SetVisible("PanelRightEditor", false);
        SetVisible("PanelToolRail", false);
        SetVisible("PanelOptionsBar", false);
    }
}

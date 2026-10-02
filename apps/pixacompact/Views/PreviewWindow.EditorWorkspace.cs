using Avalonia.Controls;

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

    private void ConfigureEditorWorkspace()
    {
        // Layout tetap; tidak ada susunan yang perlu dihitung ulang per sesi.
        ApplyEditorDockVisibility();
    }

    private void ApplyEditorDockVisibility()
    {
        if (!_editMode) return;
        bool show = !_tabPanelsHidden;
        SetVisible("PanelToolRail", show);
        SetVisible("PanelOptionsBar", show);
        SetVisible("PanelMenuBar", true);
        if (this.FindControl<Border>("PanelRightEditor") is { } right)
            right.IsVisible = show && _settings.EditorShowRightPanel;
        // Panel Layers ditampilkan kembali (restorasi minimal Putaran 2);
        // konten diisi oleh DocSession saat tersedia.
        SetVisible("PanelDocLayers", show);
    }

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

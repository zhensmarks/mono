using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

public partial class PreviewWindow
{
    private bool _editMenuWired;

    // ── Menu Bar mode edit (File/Edit/Select/View, ala Photoshop) ──
    // Rail tools hanya berisi tools edit; semua aksi dokumen & perintah tinggal di menu ini.
    // Satu aksi = satu tempat utama (menu); tombol cepat hanya untuk view-mode toggles.
    private void WireEditMenu()
    {
        if (_editMenuWired) { UpdateEditMenuShortcuts(); return; }
        _editMenuWired = true;        void Click(string name, EventHandler<RoutedEventArgs> handler)
        {
            if (this.FindControl<MenuItem>(name) is { } mi)
                mi.Click += handler;
        }
        Click("MiFillBlack", (_, _) => ApplySelectionToMask(0, "Hapus Selection"));
        Click("MiFillWhite", (_, _) => ApplySelectionToMask(255, "Restore Selection"));
        Click("MiMakeSelection", (_, _) => MakeSelectionFromPenPath());
        Click("MiMakeSelectionAdd", (_, _) => MakeSelectionFromPenPath(SelectionCombineMode.Add));
        Click("MiSelectAll", (_, _) => SelectAllSelection());
        Click("MiDeselect", (_, _) => ClearSelection());
        Click("MiInvertSelection", (_, _) => InvertSelection());
        Click("MiGrowSelection", (_, _) => GrowSelection());
        Click("MiShrinkSelection", (_, _) => ShrinkSelection());
        Click("MiCompare", OnCompareMenuClick);
        Click("MiQuickMask", OnQuickMaskMenuClick);
        Click("MiMaskView", OnMaskViewMenuClick);
        UpdateEditMenuShortcuts();
    }

    private void OnCompareMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        _compareOriginal = mi.IsChecked == true;
        ApplyCompareOverlay();
        UpdateCompareButtonState();
    }

    private void OnQuickMaskMenuClick(object? sender, RoutedEventArgs e)
    {
        OnQuickMaskClick(sender, e);
        if (sender is MenuItem mi) mi.IsChecked = _quickMask;
    }

    private void OnMaskViewMenuClick(object? sender, RoutedEventArgs e)
    {
        OnMaskViewClick(sender, e);
        if (sender is MenuItem mi) mi.IsChecked = _maskView;
    }

    /// <summary>Enable/disable menu item mengikuti state sesi (dipanggil dari UpdateEditorStatus).</summary>
    private void UpdateEditMenuState()
    {
        void Set(string name, bool enabled)
        {
            if (this.FindControl<MenuItem>(name) is { } mi) mi.IsEnabled = enabled;
        }
        if (_session == null)
        {
            foreach (var n in new[] { "MiSaveEdit", "MiSaveAsEdit", "MiRevertEdit", "MiDiscardEdit", "MiExitEdit",
                                      "MiUndo", "MiRedo", "MiFillBlack", "MiFillWhite", "MiMakeSelection", "MiMakeSelectionAdd",
                                      "MiSelectAll", "MiDeselect", "MiInvertSelection", "MiGrowSelection", "MiShrinkSelection",
                                      "MiCompare", "MiQuickMask", "MiMaskView" })
                Set(n, false);
            return;
        }
        bool hasSel = _session.Selection.HasSelection;
        bool dirty = _session.IsDirty;
        bool canMakeSel = _activeSelectionTool?.CanCommit == true;
        Set("MiSaveEdit", true);
        Set("MiSaveAsEdit", true);
        Set("MiRevertEdit", dirty);
        Set("MiDiscardEdit", dirty);
        Set("MiExitEdit", true);
        Set("MiUndo", _session.CanUndo);
        Set("MiRedo", _session.CanRedo);
        Set("MiFillBlack", true);
        Set("MiFillWhite", _session.HasRestoreSource);
        Set("MiMakeSelection", canMakeSel);
        Set("MiMakeSelectionAdd", canMakeSel);
        Set("MiSelectAll", true);
        Set("MiDeselect", hasSel);
        Set("MiInvertSelection", hasSel);
        Set("MiGrowSelection", hasSel);
        Set("MiShrinkSelection", hasSel);
        Set("MiCompare", true);
        Set("MiQuickMask", true);
        Set("MiMaskView", true);
    }

    /// <summary>Tampilkan shortcut di tiap menu item (dari EditorShortcutMap / hardcoded).</summary>
    private void UpdateEditMenuShortcuts()
    {
        string Shortcut(EditorShortcutAction action) => EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, action);
        void Gesture(string name, string gesture)
        {
            if (this.FindControl<MenuItem>(name) is not { } mi) return;
            try { mi.InputGesture = KeyGesture.Parse(gesture.Replace("Ctrl+", "Control+")); }
            catch { mi.InputGesture = null; }
        }
        Gesture("MiUndo", "Ctrl+Z");
        Gesture("MiRedo", "Ctrl+Shift+Z");
        Gesture("MiSaveEdit", "Ctrl+S");
        Gesture("MiRevertEdit", "F12");
        Gesture("MiExitEdit", "Esc");
        Gesture("MiFillBlack", Shortcut(EditorShortcutAction.MaskDelete));
        Gesture("MiFillWhite", Shortcut(EditorShortcutAction.MaskRestore));
        Gesture("MiMakeSelection", Shortcut(EditorShortcutAction.MakeSelection));
        Gesture("MiMakeSelectionAdd", "Ctrl+Shift+Enter");
        Gesture("MiSelectAll", "Ctrl+A");
        Gesture("MiDeselect", "Ctrl+D");
        Gesture("MiInvertSelection", "Ctrl+Shift+I");
        Gesture("MiGrowSelection", "Ctrl+J");
        Gesture("MiShrinkSelection", "Ctrl+Shift+J");
        Gesture("MiQuickMask", Shortcut(EditorShortcutAction.QuickMask));
        Gesture("MiMaskView", Shortcut(EditorShortcutAction.MaskView));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using PixelcutCompact.Models;
using PixelcutCompact.Services;
using IOPath = System.IO.Path;
using PixelcutCompact.Services.Ai;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

/// <summary>Shortcut keyboard editor (dipisah agar file < batas push).</summary>
public partial class PreviewWindow
{
    // ========================
    // SHORTCUT EDITOR
    // ========================

    /// <summary>Return true bila shortcut editor menangani tombol ini.</summary>
    /// <summary>
    /// Shortcut saat BELUM di mode edit (mis. E untuk membuka editor).
    /// Return true bila event sudah ditangani editor.
    /// </summary>
    private bool HandlePreviewShortcutForEditor(KeyEventArgs e)
    {
        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.None)
        {
            // Tugas A Putaran 2: EnterEditMode selalu memberi feedback (Toast)
            // bila belum bisa masuk — tidak lagi silent-fail.
            EnterEditMode();
            return true;
        }
        return false;
    }

    private void ReleaseTemporaryPan()
    {
        if (!_toolState.ReleaseSpace()) return;
        if (_editorPanDragActive)
        {
            _editorPanDragActive = false;
            _isDragging = false;
            _targetImage = null;
            _capturedPointer?.Capture(null);
            _capturedPointer = null;
        }
        UpdateCursorForTool(_activeTool);
    }

    private bool HandleEditorKey(KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        bool noModifiers = modifiers == KeyModifiers.None;
        bool ctrlOnly = modifiers == KeyModifiers.Control;
        bool ctrlShift = modifiers == (KeyModifiers.Control | KeyModifiers.Shift);

        if (ctrlOnly && e.Key == Key.Z) { OnUndoClick(this, new RoutedEventArgs()); return true; }
        if ((ctrlOnly && e.Key == Key.Y) || (ctrlShift && e.Key == Key.Z))
        { OnRedoClick(this, new RoutedEventArgs()); return true; }
        if (ctrlOnly && e.Key == Key.S) { _ = SaveInPlaceAsync(); return true; }
        // Photoshop: Ctrl+Shift+S = Simpan sebagai salinan.
        if (ctrlShift && e.Key == Key.S) { _ = SaveAsCopyInteractively(); return true; }

        // Persistent selection commands use Photoshop's standard combinations.
        if (ctrlShift && e.Key == Key.I) { InvertSelection(); return true; }
        if (ctrlOnly && e.Key == Key.J) { GrowSelection(); return true; }
        if (ctrlShift && e.Key == Key.J) { ShrinkSelection(); return true; }
        if (ctrlOnly && e.Key == Key.A) { SelectAllSelection(); return true; }
        if (ctrlOnly && e.Key == Key.D) { ClearSelection(); return true; }
        // Photoshop: Ctrl+Shift+Enter = tambah path pen ke selection yang sudah ada.
        if (ctrlShift && e.Key == Key.Enter) { MakeSelectionFromPenPath(SelectionCombineMode.Add); return true; }
        // Photoshop: Shift+[ / Shift+] = brush hardness (lembut/tajam).
        bool shiftOnly = modifiers == KeyModifiers.Shift;
        if (shiftOnly && e.Key == Key.OemOpenBrackets) { AdjustBrushHardness(-0.05); return true; }
        if (shiftOnly && e.Key == Key.OemCloseBrackets) { AdjustBrushHardness(0.05); return true; }
        // Photoshop: 1..0 = opacity brush 10%..100% (saat brush/eraser aktif).
        if (noModifiers && TryOpacityDigit(e.Key, out double digitOpacity)
            && _activeTool is EditToolKind.Brush or EditToolKind.Eraser)
        {
            _settings.EditorBrushOpacity = digitOpacity;
            _settings.Save();
            SetSlider("SldBrushOpacity", digitOpacity * 100.0);
            UpdateOptionLabels();
            return true;
        }
        // Photoshop: arrow keys = nudge selection 1px (Shift+arrows = 10px).
        if ((noModifiers || shiftOnly) && e.Key is Key.Up or Key.Down or Key.Left or Key.Right
            && e.Source is not Slider)
        {
            int step = shiftOnly ? 10 : 1;
            int ndx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            int ndy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            if (NudgeSelection(ndx, ndy)) return true;
        }
        // Photoshop: Tab = sembunyikan/tampilkan semua panel.
        if (noModifiers && e.Key == Key.Tab) { ToggleEditorPanels(); return true; }
        // Photoshop: Ctrl++ / Ctrl+- = zoom in/out, Ctrl+0 = fit, Ctrl+1 = 100%.
        if (ctrlOnly && e.Key is Key.OemPlus or Key.Add) { EditorZoomStep(1.25); return true; }
        if (ctrlShift && e.Key == Key.OemPlus) { EditorZoomStep(1.25); return true; }
        if (ctrlOnly && e.Key is Key.OemMinus or Key.Subtract) { EditorZoomStep(1.0 / 1.25); return true; }
        if (ctrlOnly && e.Key is Key.D0 or Key.NumPad0) { EditorZoomFit(); return true; }
        if (ctrlOnly && e.Key is Key.D1 or Key.NumPad1)
        {
            EditorZoomStep(1.0 / Math.Max(0.05, _viewPort.Zoom));
            return true;
        }

        // Photoshop: F12 = Revert — buang semua perubahan, kembali ke gambar di disk.
        if (noModifiers && e.Key == Key.F12) { _ = RevertEditsAsync(); return true; }

        if (noModifiers && e.Key == Key.Escape)
        {
            // Give an unfinished lasso/pen path one Escape to cancel before exiting.
            if (_activeSelectionTool is { IsActive: true } or { CanCommit: true })
            {
                _activeSelectionTool.Cancel();
                RenderOverlay();
                return true;
            }

            EndEditMode(silent: false);
            return true;
        }
        if (noModifiers && e.Key == Key.Enter)
        {
            if (_activeSelectionTool is PenTool pt)
            {
                // Photoshop: Enter menutup/menyelesaikan path pen — TIDAK membuat selection.
                // Path tertutup dipertahankan di overlay sampai Make Selection (Ctrl+Enter),
                // tool diganti, path baru dimulai, atau Esc.
                if (!pt.IsClosed && pt.Anchors.Count >= 3)
                {
                    pt.ClosePath();
                    RenderOverlay();
                    UpdateEditorStatus();
                }
                return true;
            }
            if (_activeSelectionTool is PolygonalLassoSelectionTool polygon && polygon.CanCommit)
            {
                polygon.ClosePath();
                CommitSelectionToState(polygon, e.KeyModifiers);
                return true;
            }
            return false;
        }
        if ((noModifiers || modifiers == KeyModifiers.Shift) && (e.Key == Key.Back || e.Key == Key.Delete))
        {
            // Context-aware ala Photoshop: saat selection tool sedang menggambar,
            // Delete/Backspace memangkas titik terakhir (perilaku lama dipertahankan);
            // di luar itu tombol ini menjalankan aksi masking sesuai shortcut.
            if (_activeSelectionTool is { IsActive: true } or { CanCommit: true })
            {
                if (noModifiers)
                {
                    _activeSelectionTool.RemoveLastPoint();
                    RenderOverlay();
                }
                return true;
            }
            if (EditorShortcutMap.TryGetAction(_settings.EditorShortcuts, e.Key, modifiers, out var maskAction)
                || EditorShortcutMap.TryGetAction(_settings.EditorShortcuts,
                    e.Key == Key.Back ? Key.Delete : Key.Back, modifiers, out maskAction))
            {
                // Backspace/Delete diperlakukan sebagai pasangan (ala Photoshop):
                // bila tombol yang ditekan tidak ter-bind, pakai binding pasangannya.
                if (maskAction == EditorShortcutAction.MaskDelete)
                    ApplySelectionToMask(0, "Hapus Selection");
                else if (maskAction == EditorShortcutAction.MaskRestore)
                    ApplySelectionToMask(255, "Restore Selection");
            }
            return true;
        }

        if (EditorShortcutMap.TryGetAction(_settings.EditorShortcuts, e.Key, modifiers, out var shortcutAction))
        {
            switch (shortcutAction)
            {
                case EditorShortcutAction.Pan: SetActiveTool(EditToolKind.Pan); break;
                case EditorShortcutAction.Move: SetActiveTool(EditToolKind.Move); break;
                case EditorShortcutAction.Lasso: SetActiveTool(EditToolKind.Lasso); break;
                case EditorShortcutAction.PolygonLasso: SetActiveTool(EditToolKind.PolyLasso); break;
                case EditorShortcutAction.MagicWand: SetActiveTool(EditToolKind.MagicWand); break;
                case EditorShortcutAction.Pen: SetActiveTool(EditToolKind.Pen); break;
                case EditorShortcutAction.Brush: SetActiveTool(EditToolKind.Brush); break;
                case EditorShortcutAction.Eraser: SetActiveTool(EditToolKind.Eraser); break;
                case EditorShortcutAction.RefineEdge: SetActiveTool(EditToolKind.RefineEdge); break;
                case EditorShortcutAction.RectMarquee: SetActiveTool(EditToolKind.RectMarquee); break;
                case EditorShortcutAction.EllipseMarquee: SetActiveTool(EditToolKind.EllipseMarquee); break;
                case EditorShortcutAction.BrushSizeDown: AdjustBrushSize(-1); break;
                case EditorShortcutAction.BrushSizeUp: AdjustBrushSize(1); break;
                case EditorShortcutAction.ToggleBrushMode:
                    if (_activeTool == EditToolKind.Brush)
                    {
                        _settings.EditorBrushRestore = !_settings.EditorBrushRestore;
                        _settings.Save();
                        UpdateOptionLabels();
                    }
                    break;
                case EditorShortcutAction.QuickMask: OnQuickMaskClick(this, new RoutedEventArgs()); break;
                case EditorShortcutAction.MaskView: OnMaskViewClick(this, new RoutedEventArgs()); break;
                case EditorShortcutAction.MaskDelete: ApplySelectionToMask(0, "Hapus Selection"); break;
                case EditorShortcutAction.MaskRestore: ApplySelectionToMask(255, "Restore Selection"); break;
                case EditorShortcutAction.MakeSelection: MakeSelectionFromPenPath(); break;
                case EditorShortcutAction.Compare:
                    _compareOriginal = !_compareOriginal;
                    ApplyCompareOverlay();
                    UpdateCompareButtonState();
                    break;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Photoshop: ubah path pen aktif menjadi selection. Path yang belum tertutup
    /// ditutup dulu; path dipertahankan di overlay sampai di-commit di sini.
    /// <summary>
    /// Buat selection dari path pen tertutup (trigger eksplisit: Ctrl+Enter, menu, atau panel).
    /// forceMode memaksa combine mode — Ctrl+Shift+Enter = Add, ala Photoshop.
    /// Juga melayani selection tool lain yang sedang punya path committable.
    /// </summary>
    private void MakeSelectionFromPenPath(SelectionCombineMode? forceMode = null)
    {
        if (_session == null) return;

        if (_activeSelectionTool is PenTool pen && pen.Anchors.Count >= 3)
        {
            if (!pen.IsClosed)
                pen.ClosePath();
            CommitSelectionToState(pen, KeyModifiers.None, forceMode);
            return;
        }
        if (_activeSelectionTool is { CanCommit: true } tool and not PenTool)
        {
            CommitSelectionToState(tool, KeyModifiers.None, forceMode);
            return;
        }
        Toast(T("Toast_NoPath"), warning: true);
    }

    private void AdjustBrushSize(int delta)
    {
        _settings.EditorBrushSize = Math.Clamp(_settings.EditorBrushSize + delta * Math.Max(1, _settings.EditorBrushSize / 10), 1, 500);
        SetSlider("SldBrushSize", _settings.EditorBrushSize);
        UpdateOptionLabels();
    }
}

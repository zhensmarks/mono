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

/// <summary>
/// Bagian editor mask/selection native dari <see cref="PreviewWindow"/>.
/// Dipisah dari file utama agar perubahan additive: viewer lama tidak tersentuh.
/// Semua kontrol di-resolve lewat FindControl (konvensi code-behind project ini).
/// </summary>
public partial class PreviewWindow
{
    // ========================
    // STATE EDITOR
    // ========================

    private MaskEditSession? _session;
    private readonly TemporaryPanToolState _toolState = new();
    private EditToolKind _activeTool
    {
        get => _toolState.SelectedTool;
        set => _toolState.SelectTool(value);
    }
    private EditToolKind _effectiveTool => _toolState.EffectiveTool;
    private readonly EditorViewPort _viewPort = new();
    private bool _editMode;
    private bool _editorPanDragActive;
    private bool _updatingZoomControl;
    private bool _quickMask;
    private EditToolKind _toolBeforeQuickMask = EditToolKind.Brush;
    private bool _suppressOptionEvents;
    private SelectionTool? _activeSelectionTool;
    private bool _strokeActive;
    private Vec2 _lastStrokeImage;
    private BrushStrokeAccumulator? _strokeAccum;
    private bool _strokeToSelection; // true = goresan ditulis ke Selection.Coverage (Quick Mask)
    private BrushStamp _lastStamp;
    private int _penDragIndex = -1;
    private long _lastPenClickMs;
    private ColumnDefinitions? _savedColumns;
    private bool _compareOriginal;
    private bool _optionsWired;
    private bool _renderPending;
    private long _lastRenderMs;

    // ---- Cache buffer pratinjau (hindari alokasi + encode PNG tiap frame) ----
    private WriteableBitmap? _resultWb;
    private PixelBuffer? _resultBuf;
    private WriteableBitmap? _qmWb;
    private PixelBuffer? _qmBuf;
    private int _previewW = -1, _previewH = -1;
    private PixelBounds? _resultDirtyBounds;
    private PixelBounds? _quickMaskDirtyBounds;
    private bool _pendingResultRefresh;
    private bool _pendingQuickMaskRefresh;
    private List<List<Vec2>>? _antsLoops;
    private int _antsVersion = -1;
    private Ellipse? _cursorOuter;
    private Ellipse? _cursorInner;
#if DEBUG
    private const bool EditorBetaEnabled = true;
#else
    private const bool EditorBetaEnabled = false;
#endif
    private readonly RefineHairService _refineHair = new();
    private CancellationTokenSource? _refineCts;

    // ---- Seleksi persisten (lapisan selection + marching ants) ----
    /// <summary>Lapisan seleksi aktif (= <c>_session.Selection</c>); null bila belum siap.</summary>
    private SelectionState? Selection => _session?.Selection;
    private SelectionCombineMode _selMode = SelectionCombineMode.Replace;
    /// <summary>Mode gabung yang dibaca saat PointerDown (Photoshop: modifier dibaca saat mulai, bukan saat lepas).</summary>
    private SelectionCombineMode? _pendingSelMode;
    private DispatcherTimer? _antsTimer;
    private bool _antsOn;
    private bool _movingSelection;
    private Vec2 _moveStartImage;
    private byte[]? _moveBaseCoverage;      // snapshot coverage pra-drag (non-destruktif)
    private byte[]? _moveBaseMask;          // snapshot mask pra-drag (Shift+drag pindah isi)
    private bool _moveWithMask;
    private bool _refineEdgeActive;
    private bool _moveEditStarted;
    private Vec2 _lastRefineImage;
    private bool _suppressHistoryEvents;

    /// <summary>Event saat hasil disimpan ke disk (in-place), agar galeri refresh thumbnail.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>
    /// Guard pindah gambar: bila sesi editor punya perubahan belum disimpan, minta konfirmasi user.
    /// Return true  = aman lanjut (tidak ada perubahan, atau sudah disimpan/salinan).
    /// Return false = user menekan Batal; pemanggil harus membatalkan navigasi.
    /// </summary>
    internal Task<bool> ConfirmEditorDiscardAsync()
    {
        if (_session is not { IsDirty: true }) return Task.FromResult(true);
        return ConfirmDiscardChanges();
    }

    /// <summary>Dipanggil oleh LoadImages utama setelah bitmap dimuat, untuk reset/edit session.</summary>
    private async Task<bool> PrepareEditorAsync(string originalPath, string resultPath, PixelBuffer? existingResult = null)
    {
        try
        {
            await Task.CompletedTask;
            if (!_settings.EditorBetaMode) return false;
            EndEditMode(silent: true);
            _session?.Dispose();
        _session = null;
        DisposePreviewBuffers();
            _refineHair.Reset();

            var srcPath = File.Exists(resultPath) ? resultPath : originalPath;
            if (!File.Exists(srcPath)) return false;

            PixelBuffer? result = existingResult;
            PixelBuffer? original = null;

            // Result sudah dipakai viewer bila tersedia. Hanya decode ulang bila perlu.
            if (result == null)
            {
                try
                {
                    using var bmp = new Bitmap(srcPath);
                    result = PixelBuffer.FromBitmap(bmp);
                }
                catch { result = null; }
            }

            // Original enhancement berjalan terpisah; tidak menghalangi session result.
            if (File.Exists(originalPath) && !string.Equals(originalPath, srcPath, StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        using var ob = new Bitmap(originalPath);
                        return PixelBuffer.FromBitmap(ob);
                    }
                    catch { return null; }
                }).ContinueWith(t =>
                {
                    // Original dipakai hanya untuk Restore/Refine; jangan mengganti session
                    // bila user sudah pindah gambar atau session sudah disposed.
                    if (t.Status == TaskStatus.RanToCompletion && t.Result != null &&
                        ReferenceEquals(_session?.Result, result) && _session?.SetOriginalIfMissing(t.Result) == true)
                    {
                        UpdateRestoreAvailability();
                        MarkResultDirty(PixelBounds.Full(_session.Width, _session.Height));
                        ScheduleComposite(true);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            if (result == null) return false;

            _session = new MaskEditSession(result, original,
                _settings.EditorUndoSteps, _settings.EditorUndoMemoryMb);
            UpdateRestoreAvailability();

            SetVisible("BtnEnterEdit", true);
            SetVisible("BtnEnterEditHeader", true);
            SetVisible("BtnExitEditHeader", false);
            UpdateEditorStatus();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PrepareEditor gagal: {ex.Message}");
            return false;
        }
    }

    private async void OnEditorBetaChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<ToggleSwitch>("TglEditorBeta") is not { } toggle) return;
        _settings.EditorBetaMode = toggle.IsChecked == true;
        _settings.Save();

        if (!_settings.EditorBetaMode)
        {
            EndEditMode(silent: true);
            _session?.Dispose();
            _session = null;
            SetVisible("BtnEnterEdit", false);
            SetVisible("BtnEnterEditHeader", false);
            return;
        }

        if (_session == null && !string.IsNullOrWhiteSpace(_resultPath))
            await PrepareEditorAsync(_originalPath, _resultPath);
        WireEditorControls();
    }

    // MASUK / KELUAR MODE EDIT
    // ========================

    private void OnEnterEditClick(object? sender, RoutedEventArgs e) => EnterEditMode();

    private void EnterEditMode()
    {
        if (!_settings.EditorBetaMode || _session == null) return;

        _editMode = true;

        SetVisible("PanelToolRail", true);
        SetVisible("PanelOptionsBar", true);
        SetVisible("PanelEditorStatus", true);
        SetVisible("BtnEnterEdit", false);
        SetVisible("BtnEnterEditHeader", false);
        SetVisible("BtnExitEditHeader", true);
        SetVisible("ChipEditMode", true);
        SetVisible("BtnFooterMode", true);

        // Aktifkan canvas overlay untuk drawing selection
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay != null)
        {
            overlay.IsVisible = true;
            overlay.IsHitTestVisible = false; // Overlay tidak menangkap pointer events
        }
        // Siapkan overlay "Bandingkan" dengan sumber original.
        var cmpImg = this.FindControl<Image>("ImgOriginalCompare");
        if (cmpImg != null)
        {
            cmpImg.Source = this.FindControl<Image>("ImgOriginal")?.Source;
            cmpImg.IsVisible = false;
        }
        _compareOriginal = false;
        UpdateCompareButtonState();

        // Layar penuh: sembunyikan kolom Original + splitter supaya editor lega.
        SetPreviewSplit(editing: true);

        // Mode seleksi dan susunan workspace dipulihkan dari preferensi.
        _selMode = (SelectionCombineMode)Math.Clamp(_settings.EditorSelectionMode,
            (int)SelectionCombineMode.Replace, (int)SelectionCombineMode.Intersect);
        ConfigureEditorWorkspace();

        // Pulihkan tool terakhir (bila valid), jika tidak default Pan.
        var startTool = _settings.ActiveEditTool >= (int)EditToolKind.Pan &&
                        _settings.ActiveEditTool <= (int)EditToolKind.EllipseMarquee
            ? (EditToolKind)_settings.ActiveEditTool : EditToolKind.Pan;
        SetActiveTool(startTool);
        ApplyEditorOptionsToUi();
        UpdateSelectionModeButtons();
        RefreshHistory();
        RenderQuickMask();
        UpdateFooterModeButton();

        // Layer V2 — aktifkan dengan thumbnail caching (ImageLayer.GetThumbnail sudah cached).
        try { if (_session?.Result != null) EnterDocEditMode(_session.Result); } catch (Exception ex) { Console.WriteLine($"LayerV2: {ex.Message}"); }
        ApplyEditorDockVisibility();
        ApplyCheckerboardBackground();
    }

    private void OnExitEditClick(object? sender, RoutedEventArgs e) => EndEditMode(silent: false);

    private void EndEditMode(bool silent)
    {
        ReleaseTemporaryPan();
        _editMode = false;
        _strokeActive = false;
        _quickMask = false;
        _compareOriginal = false;
        _movingSelection = false;
        _refineEdgeActive = false;
        _activeSelectionTool?.Cancel();
        _activeSelectionTool = null;
        _refineCts?.Cancel();
        StopAntsTimer();

        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay != null)
        {
            overlay.IsVisible = false;
            ClearOverlayDynamic(overlay);
        }
        SetVisible("EditOverlay", false);
        try { ExitDocEditMode(); } catch { }
        SetVisible("PanelEditorStatus", false);
        SetEditorWorkspaceActive(false);
        SetVisible("EditOverlay", false);
        SetVisible("PanelRefineBusy", false);
        SetVisible("ImgQuickMask", false);
        SetVisible("ImgOriginalCompare", false);
        SetVisible("ChipEditMode", false);
        SetVisible("BtnExitEditHeader", false);
        SetVisible("BtnEnterEditHeader", _session != null);

        // Kembalikan tata letak berdampingan (Original + splitter).
        SetPreviewSplit(editing: false);

        var btnEnter = this.FindControl<Button>("BtnEnterEdit");
        if (btnEnter != null) btnEnter.IsVisible = _session != null;

        RenderAnts();

        UpdateCompareButtonState();
        Cursor = Cursor.Default;

        UpdateFooterModeButton();
    }

    /// <summary>
    /// Mode Edit memakai seluruh lebar window: kolom Original & splitter diciutkan ke 0,
    /// panel hasil mengambil semua ruang. Saat keluar, definisi kolom awal dipulihkan.
    /// </summary>
    private void SetPreviewSplit(bool editing)
    {
        var grid = this.FindControl<Grid>("PanelNormalPreview");
        if (grid == null) return;

        if (editing)
        {
            _savedColumns ??= grid.ColumnDefinitions;
            grid.ColumnDefinitions = new ColumnDefinitions("0,0,*");
            SetVisible("PanelOriginalColumn", false);
            SetVisible("SplitterPreview", false);
        }
        else
        {
            if (_savedColumns != null) grid.ColumnDefinitions = _savedColumns;
            SetVisible("PanelOriginalColumn", true);
            SetVisible("SplitterPreview", true);
        }
    }

    /// <summary>Toggle "Bandingkan": tampilkan overlay Original transparan di atas kanvas editor.</summary>
    private void OnCompareToggled(object? sender, RoutedEventArgs e)
    {
        _compareOriginal = sender is ToggleButton { IsChecked: true };
        ApplyCompareOverlay();
    }

    private void ApplyCompareOverlay()
    {
        var img = this.FindControl<Image>("ImgOriginalCompare");
        if (img == null) return;

        // Sumber = gambar original (bukan hasil), agar user bisa peek sebelum/sesudah.
        if (_compareOriginal && img.Source == null)
        {
            var orig = this.FindControl<Image>("ImgOriginal")?.Source;
            if (orig != null) img.Source = orig;
        }

        img.IsVisible = _editMode && _compareOriginal;
        UpdateCompareButtonState();
    }

    private void UpdateCompareButtonState()
    {
        if (this.FindControl<ToggleButton>("BtnCompare") is { } btn)
        {
            if (btn.IsChecked != _compareOriginal)
            {
                _suppressOptionEvents = true;
                btn.IsChecked = _compareOriginal;
                _suppressOptionEvents = false;
            }
        }
    }
    private void OnFooterModeClick(object? sender, RoutedEventArgs e)
    {
        if (_editMode)
        {
            EndEditMode(silent: false);
            return;
        }
        EnterEditMode();
    }

    private void UpdateFooterModeButton()
    {
        var text = this.FindControl<TextBlock>("TxtFooterMode");
        var icon = this.FindControl<PathIcon>("IconFooterMode");
        var button = this.FindControl<Button>("BtnFooterMode");
        var headerEnter = this.FindControl<Button>("BtnEnterEditHeader");
        var headerExit = this.FindControl<Button>("BtnExitEditHeader");

        // Satu tombol kontekstual di PreviewWindow. Tombol header lama tidak
        // boleh tampil bersamaan karena membuat dua jalur masuk editor.
        if (headerEnter != null) headerEnter.IsVisible = false;
        if (headerExit != null) headerExit.IsVisible = false;
        if (text != null) text.Text = _editMode ? "MODE EDITOR" : "BUKA EDITOR";
        if (button != null) ToolTip.SetTip(button, _editMode ? "Keluar dari mode editor" : "Buka editor mask dan selection");
        if (icon != null)
            icon.Data = StreamGeometry.Parse(_editMode
                ? "M3,5H21V19H3V5M5,7V17H19V7H5M7,9H9V15H7V9M11,9H13V15H11V9M15,9H17V15H15V9Z"
                : "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M11,6H13V13H11V6M11,15H13V17H11V15Z");
    }

    private void SetVisible(string name, bool visible)
    {
        var c = this.FindControl<Control>(name);
        if (c != null) c.IsVisible = visible;
    }

    // ========================
    // PILIH TOOL
    // ========================

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (!Enum.TryParse<EditToolKind>(tag, out var kind)) return;
        SetActiveTool(kind);
    }

    private void SetActiveTool(EditToolKind kind)
    {
        _activeTool = kind;

        // Lepaskan handler dari tool lama agar tidak menumpuk (event leak).
        if (_activeSelectionTool != null)
            _activeSelectionTool.Changed -= _onToolChanged;

        _activeSelectionTool?.Cancel();
        _activeSelectionTool = null;
        _strokeActive = false;
        _penDragIndex = -1;
        // Batalkan state drag yang mungkin tertinggal (mis. pointer lepas di luar kanvas).
        _movingSelection = false;
        _moveBaseCoverage = null;
        _moveBaseMask = null;
        _moveWithMask = false;
        _refineEdgeActive = false;
        _pendingSelMode = null;

        if (kind == EditToolKind.Lasso) _activeSelectionTool = new LassoSelectionTool();
        else if (kind == EditToolKind.RectMarquee) _activeSelectionTool = new MarqueeSelectionTool { Ellipse = false };
        else if (kind == EditToolKind.EllipseMarquee) _activeSelectionTool = new MarqueeSelectionTool { Ellipse = true };
        else if (kind == EditToolKind.PolyLasso) _activeSelectionTool = new PolygonalLassoSelectionTool();
        else if (kind == EditToolKind.Pen) _activeSelectionTool = new PenTool();

        if (_activeSelectionTool != null)
            _activeSelectionTool.Changed += _onToolChanged;

         HighlightActiveTool(kind);
         UpdateOptionsBarVisibility(kind);
         UpdateBrushModeButton();
         UpdateToolHint(kind);
         UpdateCursorForTool(kind);

         var overlay = this.FindControl<Canvas>("EditOverlay");
         if (overlay != null) ClearOverlayDynamic(overlay);

         UpdateEditorStatus();
     }

    /// <summary>Handler tetap agar bisa di-unsubscribe (menghindari kebocoran event).</summary>
    private void _onToolChanged() => RenderOverlay();

    private void HighlightActiveTool(EditToolKind kind)
    {
        var map = new (string Name, EditToolKind Kind)[]
        {
            ("BtnToolPan", EditToolKind.Pan),
            ("BtnToolLasso", EditToolKind.Lasso),
            ("BtnToolRectMarquee", EditToolKind.RectMarquee),
            ("BtnToolEllipseMarquee", EditToolKind.EllipseMarquee),
            ("BtnToolPolyLasso", EditToolKind.PolyLasso),
            ("BtnToolWand", EditToolKind.MagicWand),
            ("BtnToolPen", EditToolKind.Pen),
            ("BtnToolBrush", EditToolKind.Brush),
            ("BtnToolEraser", EditToolKind.Eraser),
            ("BtnToolMove", EditToolKind.Move),
            ("BtnToolRefineEdge", EditToolKind.RefineEdge),
        };
         foreach (var (name, k) in map)
         {
             var b = this.FindControl<Button>(name);
             if (b == null)
             {
                 Console.WriteLine($"[DEBUG] HighlightActiveTool: Button '{name}' tidak ditemukan!");
                 continue;
             }
             bool isActive = k == kind;
             if (isActive)
             {
                 if (!b.Classes.Contains("active")) b.Classes.Add("active");
             }
             else
             {
                 b.Classes.Remove("active");
             }
             Console.WriteLine($"[DEBUG] HighlightActiveTool: {name} -> {(isActive ? "AKTIF" : "nonaktif")}");
         }
         Console.WriteLine($"[DEBUG] HighlightActiveTool selesai untuk tool: {kind}");
     }

    private void UpdateOptionsBarVisibility(EditToolKind kind)
    {
        SetVisible("OptBrushGroup", kind is EditToolKind.Brush or EditToolKind.Eraser);
        SetVisible("OptWandGroup", kind == EditToolKind.MagicWand);
        SetVisible("OptSelectionGroup", kind is EditToolKind.Lasso or EditToolKind.PolyLasso or EditToolKind.Pen
            or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee);
        SetVisible("OptRefineEdgeGroup", kind == EditToolKind.RefineEdge);
        // Mode seleksi + Grow/Shrink selalu tampil saat mode edit (tool seleksi & wand). 
        SetVisible("OptSelectionModeGroup", kind is EditToolKind.Lasso or EditToolKind.PolyLasso 
            or EditToolKind.Pen or EditToolKind.MagicWand or EditToolKind.Move or EditToolKind.RefineEdge 
            or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee); 
        SetVisible("OptSelGrowGroup", kind is EditToolKind.Lasso or EditToolKind.PolyLasso 
            or EditToolKind.Pen or EditToolKind.MagicWand or EditToolKind.Move 
            or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee); 
    }

    private void UpdateToolHint(EditToolKind kind)
    {
        UpdateEditorShortcutToolTips();
        string Shortcut(EditorShortcutAction action) => EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, action);
        string hint = kind switch
        {
            EditToolKind.Pan => $"{Shortcut(EditorShortcutAction.Pan)} · pan the image; choose another tool to edit.",
            EditToolKind.Lasso => $"{Shortcut(EditorShortcutAction.Lasso)} · Shift: add · Alt: subtract · Ctrl+Shift: intersect.",
            EditToolKind.PolyLasso => $"{Shortcut(EditorShortcutAction.PolygonLasso)} · Enter closes · Backspace/Delete removes · Esc cancels.",
            EditToolKind.MagicWand => $"{Shortcut(EditorShortcutAction.MagicWand)} · Shift: add · Alt: subtract · Ctrl+Shift: intersect.",
            EditToolKind.Pen => $"{Shortcut(EditorShortcutAction.Pen)} · click/drag anchors · Enter closes · Backspace/Delete removes · Esc cancels.",
            EditToolKind.Brush => $"{Shortcut(EditorShortcutAction.Brush)} · {Shortcut(EditorShortcutAction.BrushSizeDown)} / {Shortcut(EditorShortcutAction.BrushSizeUp)} size · {Shortcut(EditorShortcutAction.ToggleBrushMode)} erase/restore.",
            EditToolKind.Eraser => $"{Shortcut(EditorShortcutAction.Eraser)} · always erases · {Shortcut(EditorShortcutAction.BrushSizeDown)} / {Shortcut(EditorShortcutAction.BrushSizeUp)} size.",
            EditToolKind.Move => $"{Shortcut(EditorShortcutAction.Move)} · drag selection; Shift-drag moves mask pixels.",
            EditToolKind.RefineEdge => $"{Shortcut(EditorShortcutAction.RefineEdge)} · refine a selection edge.",
            EditToolKind.RectMarquee => $"{Shortcut(EditorShortcutAction.RectMarquee)} · Shift: square/add · Alt: center/subtract · Ctrl+Shift: intersect.",
            EditToolKind.EllipseMarquee => $"{Shortcut(EditorShortcutAction.EllipseMarquee)} · Shift: circle/add · Alt: center/subtract · Ctrl+Shift: intersect.",
            _ => ""
        };
        var t = this.FindControl<TextBlock>("TxtEditorHint");
        if (t != null) t.Text = hint;
    }

    private void UpdateEditorShortcutToolTips()
    {
        string Shortcut(EditorShortcutAction action) => EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, action);
        void SetTip(string name, string text)
        {
            if (this.FindControl<Control>(name) is { } control)
                ToolTip.SetTip(control, text);
        }

        SetTip("BtnToolPan", $"Pan tool ({Shortcut(EditorShortcutAction.Pan)}) — pan the image");
        SetTip("BtnToolMove", $"Move tool ({Shortcut(EditorShortcutAction.Move)}) — drag the selection");
        SetTip("BtnToolLasso", $"Freehand lasso ({Shortcut(EditorShortcutAction.Lasso)})");
        SetTip("BtnToolPolyLasso", $"Polygon lasso ({Shortcut(EditorShortcutAction.PolygonLasso)}) — Enter closes; Esc cancels");
        SetTip("BtnToolWand", $"Magic wand ({Shortcut(EditorShortcutAction.MagicWand)})");
        SetTip("BtnToolPen", $"Pen ({Shortcut(EditorShortcutAction.Pen)}) — Enter closes; Esc cancels");
        SetTip("BtnToolBrush", $"Brush ({Shortcut(EditorShortcutAction.Brush)}) — {Shortcut(EditorShortcutAction.BrushSizeDown)} / {Shortcut(EditorShortcutAction.BrushSizeUp)} size; {Shortcut(EditorShortcutAction.ToggleBrushMode)} erase/restore");
        SetTip("BtnToolEraser", $"Eraser ({Shortcut(EditorShortcutAction.Eraser)}) — {Shortcut(EditorShortcutAction.BrushSizeDown)} / {Shortcut(EditorShortcutAction.BrushSizeUp)} size");
        SetTip("BtnToolRefineEdge", $"Refine Edge ({Shortcut(EditorShortcutAction.RefineEdge)})");
        SetTip("BtnToolRectMarquee", $"Rectangular marquee ({Shortcut(EditorShortcutAction.RectMarquee)})");
        SetTip("BtnToolEllipseMarquee", $"Elliptical marquee ({Shortcut(EditorShortcutAction.EllipseMarquee)})");
        SetTip("BtnQuickMask", $"Quick Mask ({Shortcut(EditorShortcutAction.QuickMask)})");
        SetTip("BtnBrushRestore", $"Toggle brush restore/erase mode ({Shortcut(EditorShortcutAction.ToggleBrushMode)}; Brush tool only)");
    }

    private void UpdateCursorForTool(EditToolKind kind)
    {
        Cursor = kind switch
        {
            EditToolKind.Pan => new Cursor(StandardCursorType.SizeAll),
            EditToolKind.MagicWand => new Cursor(StandardCursorType.Cross),
            EditToolKind.Pen => new Cursor(StandardCursorType.Cross),
            EditToolKind.Lasso or EditToolKind.PolyLasso or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee => new Cursor(StandardCursorType.Cross),
            EditToolKind.Brush or EditToolKind.Eraser => new Cursor(StandardCursorType.None),
            EditToolKind.Move => new Cursor(StandardCursorType.SizeAll),
            EditToolKind.RefineEdge => new Cursor(StandardCursorType.None),
            _ => Cursor.Default
        };
    }

    private void OnEditorZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_editMode || _updatingZoomControl || e.NewValue is not decimal value) return;
        SyncViewPort();
        double currentZoom = double.IsFinite(_viewPort.Zoom) && _viewPort.Zoom > 0 ? _viewPort.Zoom : 1;
        double zoom = Math.Clamp((double)value, EditorViewPort.MinimumZoom, EditorViewPort.MaximumZoom);
        var center = new Point(_viewPort.ViewWidth / 2.0, _viewPort.ViewHeight / 2.0);
        if (!_viewPort.ZoomAtViewportPoint(center, zoom / currentZoom)) return;
        ApplyEditorViewTransform();
        OnViewTransformChanged();
    }

    private void OnToggleEditorPanel(object? sender, RoutedEventArgs e)
    {
        _settings.EditorShowRightPanel = !_settings.EditorShowRightPanel;
        _settings.Save();
        ApplyEditorDockVisibility();
        var button = this.FindControl<Button>("BtnToggleEditorPanel");
        if (button != null) button.Content = _settings.EditorShowRightPanel ? "Panel" : "Panel +";
    }

    // ========================
    // OPTIONS BAR
    // ========================

    private void ApplyEditorOptionsToUi()
    {
        if (!_optionsWired)
        {
            if (this.FindControl<Slider>("SldBrushSize") is { } s1) s1.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldBrushHardness") is { } s2) s2.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldBrushOpacity") is { } s3) s3.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldBrushFlow") is { } s3f) s3f.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldWandTolerance") is { } s4) s4.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldSelectionFeather") is { } s5) s5.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldRefineEdgeSize") is { } s6) s6.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldRefineEdgeFeather") is { } s7) s7.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldSelGrowPx") is { } s8) s8.PropertyChanged += OnOptionChanged;
            if (this.FindControl<Slider>("SldSelGrowPxPanel") is { } s9) s9.PropertyChanged += OnOptionChanged;

            foreach (var name in new[] { "ChkWandContiguous", "ChkWandSampleAlpha", "ChkWand8Conn", "ChkAntiAlias" })
            {
                if (this.FindControl<CheckBox>(name) is { } cb)
                    cb.PropertyChanged += OnOptionChanged;
            }

            if (this.FindControl<Button>("BtnBrushRestore") is { } tb)
            {
                tb.PropertyChanged += OnOptionChanged;
                // Click sudah terpasang di XAML; jangan pasang ulang di sini.
            }

            Bind("BtnUndo", OnUndoClick);
            Bind("BtnRedo", OnRedoClick);
            Bind("BtnBrushAdvanced", (_, _) => SetVisible("OptBrushAdvancedGroup",
                !(this.FindControl<Control>("OptBrushAdvancedGroup")?.IsVisible ?? false)));
            Bind("BtnQuickMask", OnQuickMaskClick);
            Bind("BtnRefineHair", OnRefineHairClick);
            Bind("BtnRefineCancel", OnRefineCancelClick);
            Bind("BtnSaveEdit", OnSaveEditClick);
            Bind("BtnSaveAsEdit", OnSaveAsEditClick);
            // Aksi selection → mask (top bar + panel kanan).
            Bind("BtnSelectAll", (_, _) => SelectAllSelection());
            Bind("BtnSelectNone", (_, _) => ClearSelection());
            Bind("BtnInvertSelection", (_, _) => InvertSelection());
            Bind("BtnApplyErase", (_, _) => ApplySelectionToMask(0, "Hapus Selection"));
            Bind("BtnApplyRestore", (_, _) => ApplySelectionToMask(255, "Restore Selection"));
            Bind("BtnApplyFill", (_, _) => ApplySelectionToMask(255, "Isi Mask"));
            Bind("BtnApplyErasePanel", (_, _) => ApplySelectionToMask(0, "Hapus Selection"));
            Bind("BtnApplyRestorePanel", (_, _) => ApplySelectionToMask(255, "Restore Selection"));
            Bind("BtnApplyFillPanel", (_, _) => ApplySelectionToMask(255, "Isi Mask"));
            Bind("BtnSelAllPanel", (_, _) => SelectAllSelection());
            Bind("BtnSelNonePanel", (_, _) => ClearSelection());
            Bind("BtnSelInvertPanel", (_, _) => InvertSelection());
            Bind("BtnSelGrow", (_, _) => GrowSelection());
            Bind("BtnSelShrink", (_, _) => ShrinkSelection());
            Bind("BtnSelGrowPanel", (_, _) => GrowSelection());
            Bind("BtnSelShrinkPanel", (_, _) => ShrinkSelection());
            Bind("BtnSelFeatherPanel", (_, _) => FeatherSelection());
            Bind("BtnSelSave", OnSaveSelectionClick);
            Bind("BtnSelLoad", OnLoadSelectionClick);
            Bind("BtnSelSavePanel", OnSaveSelectionClick);
            Bind("BtnSelLoadPanel", OnLoadSelectionClick);

            // Mask ops (top bar + panel kanan).
            Bind("BtnExpand", (_, _) => WithSession(s => { s.ShiftEdge(2, true); AfterMaskChanged("Expand"); }));
            Bind("BtnContract", (_, _) => WithSession(s => { s.ShiftEdge(2, false); AfterMaskChanged("Contract"); }));
            Bind("BtnFeatherMask", (_, _) => WithSession(s => { s.FeatherMask(Math.Max(1, (int)Math.Round(_settings.EditorSelectionFeather))); AfterMaskChanged("Feather"); }));
            Bind("BtnDefringe", (_, _) => WithSession(s => { s.Defringe(2); AfterMaskChanged("Defringe"); }));
            Bind("BtnMaskExpandPanel", (_, _) => WithSession(s => { s.ShiftEdge(2, true); AfterMaskChanged("Expand"); }));
            Bind("BtnMaskContractPanel", (_, _) => WithSession(s => { s.ShiftEdge(2, false); AfterMaskChanged("Contract"); }));
            Bind("BtnMaskFeatherPanel", (_, _) => WithSession(s => { s.FeatherMask(Math.Max(1, (int)Math.Round(_settings.EditorSelectionFeather))); AfterMaskChanged("Feather"); }));
            Bind("BtnMaskDefringePanel", (_, _) => WithSession(s => { s.Defringe(2); AfterMaskChanged("Defringe"); }));
            Bind("BtnWandToMask", OnWandToMaskClick);

            // Mode seleksi (radio ToggleButton).
            foreach (var name in new[] { "BtnModeReplace", "BtnModeAdd", "BtnModeSubtract", "BtnModeIntersect" })
            {
                var tb2 = this.FindControl<ToggleButton>(name);
                if (tb2 == null) continue;
                tb2.IsCheckedChanged -= OnSelectionModeClick;
                tb2.IsCheckedChanged += OnSelectionModeClick;
            }

            // History panel.
            if (this.FindControl<ListBox>("LstHistory") is { } hist)
            {
                hist.SelectionChanged -= OnHistorySelected;
                hist.SelectionChanged += OnHistorySelected;
            }
            _optionsWired = true;
        }


        // Sinkronkan slider baru (top bar + panel) dari settings tanpa memicu
        // event berantai; ini menjaga slider grow/refine edge konsisten.
        _suppressOptionEvents = true;
        SetSlider("SldBrushSize", _settings.EditorBrushSize);
        SetSlider("SldBrushHardness", (1.0 - _settings.EditorBrushHardness) * 100.0);
        SetSlider("SldBrushOpacity", _settings.EditorBrushOpacity * 100.0);
        SetSlider("SldBrushFlow", _settings.EditorBrushFlow * 100);
        SetSlider("SldRefineEdgeSize", _settings.EditorRefineEdgeSize);
        SetSlider("SldRefineEdgeFeather", _settings.EditorRefineEdgeFeather);
        SetSlider("SldSelGrowPx", _settings.EditorSelGrowPx);
        SetSlider("SldSelGrowPxPanel", _settings.EditorSelGrowPx);
        _suppressOptionEvents = false;
        UpdateOptionLabels();
    }

    /// <summary>Toggle mode Erase/Restore brush (dipakai juga saat startup & shortcut X).</summary>
    private void UpdateBrushModeButton()
    {
        if (this.FindControl<Button>("BtnBrushRestore") is { } tb)
        {
            tb.IsVisible = _activeTool != EditToolKind.Eraser;
            tb.Content = _settings.EditorBrushRestore ? "Pulihkan" : "Hapus";
            tb.Background = _settings.EditorBrushRestore
                ? new SolidColorBrush(Color.Parse("#3348D17A"))
                : new SolidColorBrush(Color.Parse("#15FFFFFF"));
        }
    }

    private void UpdateRestoreAvailability()
    {
        bool available = _session?.HasRestoreSource == true;
        foreach (var name in new[] { "BtnApplyRestore", "BtnApplyRestorePanel", "BtnApplyFill", "BtnApplyFillPanel" })
        {
            if (this.FindControl<Button>(name) is { } button)
                button.IsEnabled = available;
        }
    }

    private void OnBrushModeClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTool == EditToolKind.Eraser) return;
        if (!_settings.EditorBrushRestore && _session?.HasRestoreSource != true)
        {
            Toast("Piksel gambar asli belum siap; mode Pulihkan belum tersedia.");
            return;
        }
        _settings.EditorBrushRestore = !_settings.EditorBrushRestore;
        _settings.Save();
        UpdateBrushModeButton();
        UpdateToolHint(_activeTool);
    }

    private void Bind(string name, EventHandler<RoutedEventArgs> handler)
    {
        var b = this.FindControl<Button>(name);
        if (b != null)
        {
            // XAML mungkin sudah memasang Click=; lepas dulu agar tidak fire dua kali.
            b.Click -= handler;
            b.Click += handler;
        }
    }

    private void SetSlider(string name, double value)
    {
        if (this.FindControl<Slider>(name) is { } s) s.Value = value;
    }

    private void OnOptionChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressOptionEvents) return;
        if (e.Property != RangeBase.ValueProperty &&
            e.Property != ToggleButton.IsCheckedProperty &&
            e.Property != CheckBox.IsCheckedProperty) return;

        // Slider kembar (top bar <-> panel kanan): cerminkan nilai agar keduanya
        // menggerakkan settings yang sama tanpa saling menimpa.
        MirrorSliderTwin(sender as Slider);

        ReadOptionsFromUi();
        UpdateOptionLabels();
    }

    /// <summary>Salin nilai slider ke pasangan kembarnya (SldSelGrowPx <-> Panel).</summary>
    private void MirrorSliderTwin(Slider? changed)
    {
        if (changed?.Name == "SldSelGrowPx" &&
            this.FindControl<Slider>("SldSelGrowPxPanel") is { } twinA && twinA.Value != changed.Value)
        {
            _suppressOptionEvents = true;
            twinA.Value = changed.Value;
            _suppressOptionEvents = false;
        }
        else if (changed?.Name == "SldSelGrowPxPanel" &&
            this.FindControl<Slider>("SldSelGrowPx") is { } twinB && twinB.Value != changed.Value)
        {
            _suppressOptionEvents = true;
            twinB.Value = changed.Value;
            _suppressOptionEvents = false;
        }
    }

    private void ReadOptionsFromUi()
    {
        if (this.FindControl<Slider>("SldBrushSize") is { } s1) _settings.EditorBrushSize = (int)Math.Round(s1.Value);
        if (this.FindControl<Slider>("SldBrushHardness") is { } s2) _settings.EditorBrushHardness = 1.0 - s2.Value / 100.0;
        if (this.FindControl<Slider>("SldBrushOpacity") is { } s3) _settings.EditorBrushOpacity = s3.Value / 100.0;
        if (this.FindControl<Slider>("SldBrushFlow") is { } s3f) _settings.EditorBrushFlow = s3f.Value / 100.0;
        if (this.FindControl<Slider>("SldWandTolerance") is { } s4) _settings.EditorWandTolerance = (int)Math.Round(s4.Value);
        if (this.FindControl<Slider>("SldSelectionFeather") is { } s5) _settings.EditorSelectionFeather = (int)Math.Round(s5.Value);
        if (this.FindControl<Slider>("SldRefineEdgeSize") is { } s6) _settings.EditorRefineEdgeSize = (int)Math.Round(s6.Value);
        if (this.FindControl<Slider>("SldRefineEdgeFeather") is { } s7) _settings.EditorRefineEdgeFeather = (int)Math.Round(s7.Value);
        // Sumber kebenaran tunggal: slider top bar. Slider panel disinkronkan.
        if (this.FindControl<Slider>("SldSelGrowPx") is { } s8) _settings.EditorSelGrowPx = (int)Math.Round(s8.Value);
        if (this.FindControl<CheckBox>("ChkWandContiguous") is { } c1) _settings.EditorWandContiguous = c1.IsChecked == true;
        if (this.FindControl<CheckBox>("ChkWandSampleAlpha") is { } c2) _settings.EditorWandSampleAlpha = c2.IsChecked == true;
        if (this.FindControl<CheckBox>("ChkWand8Conn") is { } c4) _settings.EditorWand8Connected = c4.IsChecked == true;
        _settings.Save();
        // BtnBrushRestore adalah Button toggle-manual; state disimpan di _settings
        // (lihat OnBrushModeClick). Tidak ada properti IsChecked.
    }

    private void UpdateOptionLabels()
    {
        SetText("TxtBrushSize", $"{_settings.EditorBrushSize} px");
        SetText("TxtBrushHardness", $"{(int)Math.Round((1.0 - _settings.EditorBrushHardness) * 100)}%");
        SetText("TxtBrushOpacity", $"{(int)Math.Round(_settings.EditorBrushOpacity * 100)}%");
        SetText("TxtBrushFlow", $"{(int)Math.Round(_settings.EditorBrushFlow * 100)}%");
        SetText("TxtWandTolerance", _settings.EditorWandTolerance.ToString());
        SetText("TxtSelectionFeather", $"{_settings.EditorSelectionFeather} px");
        SetText("TxtRefineEdgeSize", $"{_settings.EditorRefineEdgeSize} px");
        SetText("TxtRefineEdgeFeather", $"{_settings.EditorRefineEdgeFeather} px");
        SetText("TxtSelGrowPx", $"{_settings.EditorSelGrowPx} px");
        SetText("TxtSelGrowPxPanel", $"{_settings.EditorSelGrowPx} px");

        UpdateBrushModeButton();
    }

    private void SetText(string name, string text)
    {
        var t = this.FindControl<TextBlock>(name);
        if (t != null) t.Text = text;
    }

    private void WithSession(Action<MaskEditSession> action)
    {
        if (_session == null) return;
        try { action(_session); }
        catch (Exception ex) { Console.WriteLine($"Editor op gagal: {ex.Message}"); }
    }

    // ========================
    // GAMBAR ULANG KANVAS
    // ========================


    /// <summary>Perbarui hanya area mask yang berubah pada bitmap persisten.</summary>
    private void RefreshResultBitmap()
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;

        try
        {
            EnsureResultBuffers();
            if (_resultBuf == null || _resultWb == null) return;

            var bounds = (_resultDirtyBounds ?? PixelBounds.Full(_session.Width, _session.Height))
                .ClampTo(_session.Width, _session.Height);
            _resultDirtyBounds = null;
            if (bounds.IsEmpty) return;

            _session.CompositeInto(_resultBuf, bounds);
            _resultBuf.WriteToUnpremul(_resultWb, bounds);
            if (!ReferenceEquals(img.Source, _resultWb)) img.Source = _resultWb;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Refresh komposit gagal: " + ex.Message);
        }
    }

    /// <summary>Siapkan ulang buffer pratinjau bila ukuran gambar berubah.</summary>
    private void EnsureResultBuffers()
    {
        if (_session == null) return;
        int w = _session.Width, h = _session.Height;
        if (_resultBuf != null && _previewW == w && _previewH == h) return;

        _resultBuf = new PixelBuffer(w, h);
        var pf = PixelFormat.Bgra8888;
        var sz = new Avalonia.PixelSize(w, h);
        var dpi = new Avalonia.Vector(96, 96);
        _resultWb?.Dispose();
        _resultWb = new WriteableBitmap(sz, dpi, pf, AlphaFormat.Unpremul);
        _previewW = w; _previewH = h;
        _resultDirtyBounds = PixelBounds.Full(w, h);
    }

    /// <summary>Coalesce refresh komposit (~30 FPS) saat stroke; force=true
    /// memaksa refresh final segera (dipakai saat stroke selesai).</summary>
    private void ScheduleComposite(bool force, bool updateResult = true, bool updateQuickMask = false)
    {
        _pendingResultRefresh |= updateResult;
        _pendingQuickMaskRefresh |= updateQuickMask;
        long now = Environment.TickCount64;
        if (force || (now - _lastRenderMs >= 33 && !_renderPending))
        {
            _lastRenderMs = now;
            _renderPending = false;
            FlushScheduledComposite();
            return;
        }
        if (_renderPending) return;
        _renderPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _renderPending = false;
            _lastRenderMs = Environment.TickCount64;
            FlushScheduledComposite();
        }, TimeSpan.FromMilliseconds(34));
    }

    private void FlushScheduledComposite()
    {
        if (_pendingResultRefresh)
        {
            _pendingResultRefresh = false;
            RefreshResultBitmap();
        }
        if (_pendingQuickMaskRefresh)
        {
            _pendingQuickMaskRefresh = false;
            RenderQuickMask(_quickMaskDirtyBounds);
        }
    }

    private void MarkResultDirty(PixelBounds bounds)
    {
        if (_session == null || bounds.IsEmpty) return;
        _resultDirtyBounds = _resultDirtyBounds.HasValue
            ? _resultDirtyBounds.Value.Union(bounds).ClampTo(_session.Width, _session.Height)
            : bounds.ClampTo(_session.Width, _session.Height);
    }

    private void MarkQuickMaskDirty(PixelBounds bounds)
    {
        if (_session == null || bounds.IsEmpty) return;
        _quickMaskDirtyBounds = _quickMaskDirtyBounds.HasValue
            ? _quickMaskDirtyBounds.Value.Union(bounds).ClampTo(_session.Width, _session.Height)
            : bounds.ClampTo(_session.Width, _session.Height);
    }

    /// <summary>
    /// Hapus elemen overlay dinamis, tetapi PERTAHANKAN path marching ants
    /// (Tag="ants") yang dideklarasikan di XAML agar tidak ikut terhapus.
    /// </summary>
    private static void ClearOverlayDynamic(Canvas overlay)
    {
        for (int i = overlay.Children.Count - 1; i >= 0; i--)
        {
            if (overlay.Children[i] is Control { Tag: "ants" }) continue;
            overlay.Children.RemoveAt(i);
        }
    }

    private void RenderOverlay()
    {
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay == null || !_editMode) return;
        ClearOverlayDynamic(overlay);
        SyncViewPort();
        RenderAnts();
        var tool = _activeSelectionTool; 
        if (tool is PolygonalLassoSelectionTool poly) 
        { 
            if (poly.CurrentPath.Count >= 1) 
                DrawPathOutline(overlay, poly.CurrentPath, closed: false); 
            if (poly.IsActive && poly.CurrentPath.Count >= 1) 
            { 
                var lastP = ImageToOverlay(poly.CurrentPath[poly.CurrentPath.Count - 1]); 
                var curP = ImageToOverlay(poly.Cursor); 
                DrawLine(overlay, lastP, curP, "#80FFE24A", dash: true); 
            } 
            if (poly.IsActive && poly.CurrentPath.Count >= 3) 
            { 
                var firstP = ImageToOverlay(poly.CurrentPath[0]); 
                var refP = ImageToOverlay(new Vec2(poly.CurrentPath[0].X + poly.CloseHitRadius, poly.CurrentPath[0].Y)); 
                double rad = Math.Max(6.0, Math.Sqrt((refP.X - firstP.X) * (refP.X - firstP.X) + (refP.Y - firstP.Y) * (refP.Y - firstP.Y))); 
                var curP2 = ImageToOverlay(poly.Cursor); 
                double ddx = curP2.X - firstP.X, ddy = curP2.Y - firstP.Y; 
                bool nearClose = Math.Sqrt(ddx * ddx + ddy * ddy) <= rad; 
                var circle = new Ellipse 
                { 
                    Width = rad * 2, Height = rad * 2, 
                    Stroke = Brushes.White, StrokeThickness = 1.5, 
                    Fill = nearClose ? new SolidColorBrush(Color.Parse("#80FFE24A")) : null 
                }; 
                Canvas.SetLeft(circle, firstP.X - rad); 
                Canvas.SetTop(circle, firstP.Y - rad); 
                overlay.Children.Add(circle); 
            } 
        } 
        else if (tool != null && tool.CurrentPath.Count >= 2 && tool is not PenTool) 
        { 
            DrawPathOutline(overlay, tool.CurrentPath, closed: tool.CanCommit); 
            if (tool.IsActive) 
            { 
                var last = ImageToOverlay(tool.CurrentPath[tool.CurrentPath.Count - 1]); 
                var first = ImageToOverlay(tool.CurrentPath[0]); 
                DrawLine(overlay, last, first, "#80FFE24A", dash: true); 
            } 
        } 
        if (tool is PenTool pen)
        {
            if (pen.CurrentPath.Count >= 2)
                DrawPathOutline(overlay, pen.CurrentPath, pen.IsClosed, penStyle: true);
            DrawPenOverlay(overlay, pen);
        }
    }

    /// <summary>
    /// Gambar anchor, handle Bézier, dan rubber band Pen Tool dengan gaya Photoshop.
    /// Handle disimpan sebagai vektor RELATIF terhadap anchor, jadi titik absolutnya
    /// adalah <c>a.Point + a.HandleIn/Out</c>.
    /// </summary>
    private void DrawPenOverlay(Canvas overlay, PenTool pen)
    {
        var anchors = pen.Anchors;
        if (anchors.Count == 0) return;

        // Garis handle + knob untuk tiap anchor yang punya handle.
        foreach (var a in anchors)
        {
            var p = ImageToOverlay(a.Point);
            var hin = a.Point + a.HandleIn;
            var hout = a.Point + a.HandleOut;
            bool hasIn = HasHandle(a.HandleIn);
            bool hasOut = HasHandle(a.HandleOut);

            if (hasIn)
            {
                var sh = ImageToOverlay(hin);
                DrawLine(overlay, p, sh, "#99A9C7FF");
                DrawHandleKnob(overlay, sh);
            }
            if (hasOut)
            {
                var sh = ImageToOverlay(hout);
                DrawLine(overlay, p, sh, "#99A9C7FF");
                DrawHandleKnob(overlay, sh);
            }
        }

        // Rubber band: garis putus-putus dari anchor terakhir ke kursor.
        if (pen.IsActive && !pen.IsClosed && anchors.Count > 0)
        {
            var last = ImageToOverlay(anchors[anchors.Count - 1].Point);
            var cur = ImageToOverlay(pen.Cursor);
            DrawLine(overlay, last, cur, "#80FFE24A", dash: true);
        }

        // Anchor: kotak (corner) atau diamond (smooth/has handle).
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            var p = ImageToOverlay(a.Point);
            bool smooth = HasHandle(a.HandleIn) || HasHandle(a.HandleOut);
            bool isFirst = i == 0;
            bool nearFirst = isFirst && anchors.Count >= 3 && pen.IsActive;

            var shape = smooth
                ? (Shape)new Polygon
                {
                    Points = new AvaloniaList<Point>
                    {
                        new Point(p.X, p.Y - 5), new Point(p.X + 5, p.Y),
                        new Point(p.X, p.Y + 5), new Point(p.X - 5, p.Y)
                    },
                    Fill = nearFirst ? new SolidColorBrush(Color.Parse("#FFE24A")) : Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                }
                : (Shape)new Rectangle
                {
                    Width = 8, Height = 8,
                    Fill = nearFirst ? new SolidColorBrush(Color.Parse("#FFE24A")) : Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

            double ox = p.X - (smooth ? 5 : 4);
            double oy = p.Y - (smooth ? 5 : 4);
            Canvas.SetLeft(shape, ox);
            Canvas.SetTop(shape, oy);
            overlay.Children.Add(shape);
        }
    }

    private void DrawHandleKnob(Canvas overlay, Point p)
    {
        var knob = new Ellipse
        {
            Width = 6, Height = 6,
            Fill = new SolidColorBrush(Color.Parse("#FF3B82F6")),
            Stroke = Brushes.White,
            StrokeThickness = 1
        };
        Canvas.SetLeft(knob, p.X - 3);
        Canvas.SetTop(knob, p.Y - 3);
        overlay.Children.Add(knob);
    }

    private void DrawPathOutline(Canvas overlay, IReadOnlyList<Vec2> pts, bool closed, bool penStyle = false)
    {
        if (pts.Count < 2) return;
        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#FFE24A")),
            StrokeThickness = penStyle ? 1.2 : 1.5
        };
        if (!closed) poly.StrokeDashArray = new AvaloniaList<double> { 4, 3 };

        foreach (var p in pts)
        {
            poly.Points.Add(ImageToOverlay(p));
        }
        overlay.Children.Add(poly);

        if (closed && pts.Count >= 3)
        {
            var s0 = ImageToOverlay(pts[0]);
            var sl = ImageToOverlay(pts[pts.Count - 1]);
            DrawLine(overlay, sl, s0, "#FFE24A");
        }
    }

    private void DrawLine(Canvas overlay, Point a, Point b, string color, bool dash = false)
    {
        var line = new Line
        {
            StartPoint = a,
            EndPoint = b,
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = 1
        };
        if (dash) line.StrokeDashArray = new AvaloniaList<double> { 4, 3 };
        overlay.Children.Add(line);
    }

    /// <summary>
    /// Pusat canvas overlay dalam koordinat lokal canvas.
    /// <see cref="EditorViewPort.ImageToScreen"/> memakai origin di TENGAH gambar/viewport,
    /// sedangkan Canvas menggambar dari kiri-atas, sehingga perlu penambahan setengah ukuran.
    /// </summary>
    private Point OverlayCenter()
    {
        var canvas = this.FindControl<Canvas>("EditOverlay");
        double w = canvas?.Bounds.Width ?? 0;
        double h = canvas?.Bounds.Height ?? 0;

        // Fallback: bila bounds belum terukur (0), pakai ukuran ImageResult.
        if (w <= 0 || h <= 0)
        {
            var img = this.FindControl<Image>("ImgResult");
            w = img?.Bounds.Width ?? 0;
            h = img?.Bounds.Height ?? 0;
        }
        return new Point(w / 2.0, h / 2.0);
    }

    /// <summary>True bila vektor handle cukup besar untuk digambar (bukan nol).</summary>
    private static bool HasHandle(Vec2 v) => v.X * v.X + v.Y * v.Y > 0.25;

    /// <summary>
    /// Konversi koordinat gambar (piksel, origin tengah) ke koordinat lokal Canvas overlay.
    /// Mencakup letterbox Stretch=Uniform DAN RenderTransform (zoom/pan/rotate).
    /// EditOverlay tidak ikut RenderTransform, jadi kita terapkan transform manual.
    /// </summary>
    private Point ImageToOverlay(Vec2 img)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null || _session == null) return new Point(img.X, img.Y);
        var b = image.Bounds;
        double fit = Math.Min(b.Width / Math.Max(1.0, _session.Width), b.Height / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        double x = (b.Width - _session.Width * fit) / 2.0 + (img.X + _session.Width / 2.0) * fit;
        double y = (b.Height - _session.Height * fit) / 2.0 + (img.Y + _session.Height / 2.0) * fit;
        return ApplyViewTransform(new Point(x, y));
    }
    /// <summary>
    /// Sama seperti ImageToOverlay, tapi koordinat Grid parent (untuk Path ants).
    /// </summary>
    private Point ImageToGrid(Vec2 img)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null || _session == null) return new Point(img.X, img.Y);
        var b = image.Bounds;
        double fit = Math.Min(b.Width / Math.Max(1.0, _session.Width), b.Height / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        double x = (b.Width - _session.Width * fit) / 2.0 + (img.X + _session.Width / 2.0) * fit;
        double y = (b.Height - _session.Height * fit) / 2.0 + (img.Y + _session.Height / 2.0) * fit;
        var o = ApplyViewTransform(new Point(x, y));
        return new Point(o.X + b.X, o.Y + b.Y);
    }
    /// <summary>
    /// Terapkan RenderTransform ImgResult (zoom/pan/rotate) ke titik lokal Image.
    /// RenderTransformOrigin 50 persen berarti translate(-O) * M * translate(O).
    /// </summary>
    private Point ApplyViewTransform(Point local)
    {
        var image = this.FindControl<Image>("ImgResult");
        if (image == null) return local;
        if (image.RenderTransform is not TransformGroup tg) return local;
        var m = tg.Value;
        var b = image.Bounds;
        double ox = b.Width / 2.0, oy = b.Height / 2.0;
        var v = new Point(local.X - ox, local.Y - oy);
        var t = m.Transform(v);
        return new Point(t.X + ox, t.Y + oy);
    }


    /// <summary>
    /// Buat checkerboard 16x16 bitmap dan set sebagai ImageBrush background BrdResult.
    /// Avalonia DrawingBrush tidak mendukung TileMode, jadi kita pakai WriteableBitmap.
    /// </summary>
    private void ApplyCheckerboardBackground()
    {
        var brd = this.FindControl<Border>("BrdResult");
        if (brd == null) return;
        try
        {
            const int sz = 16, half = 8;
            var bmp = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(sz, sz),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    uint* ptr = (uint*)fb.Address;
                    uint dark = 0xFF303030, light = 0xFF3D3D3D;
                    for (int y = 0; y < sz; y++)
                        for (int x = 0; x < sz; x++)
                        {
                            bool isLight = (x < half) ^ (y < half);
                            ptr[y * fb.RowBytes / 4 + x] = isLight ? light : dark;
                        }
                }
            }
            var brush = new Avalonia.Media.ImageBrush(bmp)
            {
                TileMode = Avalonia.Media.TileMode.Tile,
                Stretch = Avalonia.Media.Stretch.None,
                AlignmentX = Avalonia.Media.AlignmentX.Left,
                AlignmentY = Avalonia.Media.AlignmentY.Top,
            };
            brd.Background = brush;
        }
        catch (Exception ex) { Console.WriteLine($"Checkerboard: {ex.Message}"); }
    }

    private void SyncViewPort()
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;

        _viewPort.ImageWidth = _session.Width;
        _viewPort.ImageHeight = _session.Height;
        _viewPort.RotationDegrees = RotationResult;

        double zoom = 1, tx = 0, ty = 0;
        if (img.RenderTransform is TransformGroup tg)
        {
            foreach (var t in tg.Children)
            {
                if (t is ScaleTransform st) zoom = st.ScaleX;
                else if (t is TranslateTransform tt) { tx = tt.X; ty = tt.Y; }
            }
        }

        var bounds = img.Bounds;

        // ViewWidth dan ViewHeight adalah ukuran control Image (di mana bitmap ditampilkan)
        // EditorViewPort.ImageToScreen menganggap origin di pusat viewport
        _viewPort.Zoom = zoom;
        _viewPort.ViewWidth = bounds.Width;
        _viewPort.ViewHeight = bounds.Height;
        _viewPort.PanX = tx;
        _viewPort.PanY = ty;
    }

    private void ApplyEditorViewTransform()
    {
        foreach (var name in new[] { "ImgOriginal", "ImgResult", "ImgOriginalCompare", "ImgQuickMask" })
        {
            var image = this.FindControl<Image>(name);
            if (image?.RenderTransform is not TransformGroup group) continue;
            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale)
                {
                    scale.ScaleX = _viewPort.Zoom;
                    scale.ScaleY = _viewPort.Zoom;
                }
                else if (transform is TranslateTransform translate)
                {
                    translate.X = _viewPort.PanX;
                    translate.Y = _viewPort.PanY;
                }
            }
        }

        if (this.FindControl<NumericUpDown>("ZoomControl") is not { } zoomControl) return;
        var zoomValue = (decimal)_viewPort.Zoom;
        if (zoomControl.Value == zoomValue) return;
        _updatingZoomControl = true;
        try { zoomControl.Value = zoomValue; }
        finally { _updatingZoomControl = false; }
    }

    private bool HandleEditorWheelZoom(Image image, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!_editMode || image.Name != "ImgResult") return false;
        if (e.Delta.Y == 0) return true;

        SyncViewPort();
        var bounds = image.Bounds;
        Point cursor;
        if (image.Parent is Avalonia.Visual parent)
        {
            var pointerInViewport = e.GetPosition(parent);
            cursor = new Point(pointerInViewport.X - bounds.X, pointerInViewport.Y - bounds.Y);
        }
        else
        {
            cursor = e.GetPosition(image);
        }
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        if (_viewPort.ZoomAtViewportPoint(cursor, factor))
        {
            ApplyEditorViewTransform();
            OnViewTransformChanged();
        }
        return true;
    }

    private void ApplyEditorPan(Avalonia.Vector screenDelta)
    {
        if (!_editMode) return;
        SyncViewPort();
        _viewPort.PanByScreenDelta(screenDelta.X, screenDelta.Y);
        ApplyEditorViewTransform();
        OnViewTransformChanged();
    }

    /// tidak ikut transform: harus digambar ulang agar tetap presisi.
    /// </summary>
    private void OnViewTransformChanged()
    {
        if (!_editMode) return;
        UpdateZoomText();
        RenderOverlay();
    }

    /// <summary>
    /// Konversi posisi pointer (koordinat lokal Image/overlay, pra-transform)
    /// ke koordinat gambar (piksel, berbasis TENGAH gambar).
    ///
    /// Kebalikan eksak dari ImageToOverlay; keduanya memakai transform yang sama.
    /// <summary>
    /// Konversi posisi pointer (koordinat lokal Image, pre-RenderTransform)
    /// ke koordinat gambar (piksel, berbasis TENGAH gambar = origin 0,0 di tengah).
    ///
    /// Avalonia GetPosition(img) mengembalikan posisi pre-RenderTransform,
    /// sehingga zoom/pan visual TIDAK perlu diinverse di sini.
    /// Hanya kompensasi Stretch=Uniform letterboxing + Margin yang diperlukan.
    /// </summary>
    private Vec2 PointerToImage(Point posInImage)
    {
        if (_session == null) return new Vec2(0, 0);
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return new Vec2(0, 0);
        var b = img.Bounds;
        double w = b.Width, h = b.Height;
        // Fit scale: sama seperti Stretch=Uniform
        double fit = Math.Min(w / Math.Max(1.0, _session.Width), h / Math.Max(1.0, _session.Height));
        if (!double.IsFinite(fit) || fit <= 0) fit = 1;
        // Offset letterbox kiri-atas
        double left = (w - _session.Width * fit) / 2.0;
        double top  = (h - _session.Height * fit) / 2.0;
        // Posisi dalam gambar piksel (0..Width, 0..Height), lalu geser ke origin tengah
        double px = (posInImage.X - left) / fit;
        double py = (posInImage.Y - top) / fit;
        return new Vec2(px - _session.Width / 2.0, py - _session.Height / 2.0);
    }

     private bool EditorPointerPressed(Image img, PointerPressedEventArgs e)
     {

         if (!_editMode || _session == null) return false;
         if (img.Name != "ImgResult") return false;
        var pt = e.GetCurrentPoint(img);
        var pos = pt.Position;

        var imagePos = PointerToImage(pos);

        // Titik tengah dalam koordinat gambar (untuk brush).
        double halfW = _session.Width / 2.0;
        double halfH = _session.Height / 2.0;
        var centered = new Vec2(imagePos.X + halfW, imagePos.Y + halfH);
        bool left = pt.Properties.IsLeftButtonPressed;
        // Klik kanan tidak boleh mengubah mask/selection. Pan tetap ditangani viewer.
        if (!left) return false;

        if (SelectionInteractionPolicy.HandlesPointerPress(_activeTool))
        {
            if (_activeSelectionTool == null) return false;
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            _pendingSelMode = EffectiveMode(e.KeyModifiers);
            if (_activeSelectionTool is PenTool penTool)
            {
                long now = Environment.TickCount64;
                bool dbl = (now - _lastPenClickMs) < 350;
                if (dbl && penTool.CurrentPath.Count >= 3)
                {
                    penTool.ClosePath();
                    if (penTool.CanCommit) CommitSelectionToState(penTool, e.KeyModifiers);
                    else RenderOverlay();
                    return true;
                }
                if (ctrl && !alt)
                {
                    int hit = penTool.HitTestAnchor(imagePos, 10);
                    if (hit >= 0) { _penDragIndex = hit; e.Pointer.Capture(img); return true; }
                }
                e.Pointer.Capture(img);
                penTool.PointerDown(imagePos, alt);
                _lastPenClickMs = now;
            }
            else
            {
                e.Pointer.Capture(img);
                _activeSelectionTool.PointerDown(imagePos);
                if (_activeTool == EditToolKind.PolyLasso &&
                    !_activeSelectionTool.IsActive && _activeSelectionTool.CanCommit)
                {
                    CommitSelectionToState(_activeSelectionTool, e.KeyModifiers);
                    return true;
                }
            }
            RenderOverlay();
            return true;
        }

        switch (_activeTool)
        {
            case EditToolKind.Brush:
            case EditToolKind.Eraser:
                BeginStroke(centered, e);
                return true;

            case EditToolKind.Move:
                e.Pointer.Capture(img);   // agar release selalu terdeteksi walau keluar kontrol
                BeginMoveSelection(imagePos, e.KeyModifiers);
                return true;

            case EditToolKind.RefineEdge:
                e.Pointer.Capture(img);
                BeginRefineEdge(imagePos);
                return true;

            case EditToolKind.MagicWand:
                ApplyWand(centered, e.KeyModifiers);
                return true;

        }

        return false;
    }

    private bool EditorPointerMoved(Image img, PointerEventArgs e)
    {
        if (!_editMode || _session == null) return false;
        if (img.Name != "ImgResult") return false;

        var pos = e.GetPosition(img);
        var imagePos = PointerToImage(pos);
        double halfW = _session.Width / 2.0;
        double halfH = _session.Height / 2.0;
        var centered = new Vec2(imagePos.X + halfW, imagePos.Y + halfH);

        if (_strokeActive)
        {
            ContinueStroke(centered);
            return true;
        }

        if (_movingSelection)
        {
            ContinueMoveSelection(imagePos);
            return true;
        }

        if (_refineEdgeActive)
        {
            ContinueRefineEdge(imagePos);
            return true;
        }

        // Drag anchor pen (Ctrl+klik pada anchor, direct-selection).
        if (_penDragIndex >= 0 && _activeSelectionTool is PenTool penDrag)
        {
            penDrag.MoveAnchor(_penDragIndex, imagePos);
            RenderOverlay();
            return true;
        }

        // Selama jalur seleksi sedang dibangun, teruskan gerakan (pen/lasso/poly).
        if (_activeSelectionTool is SelectionTool st &&
            (st.IsActive || st is PenTool { IsClosed: false } && st.CurrentPath.Count > 0))
        {
            bool pressed = e.GetCurrentPoint(img).Properties.IsLeftButtonPressed;
            if (st is MarqueeSelectionTool mq)
            {
                mq.Constrain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                mq.FromCenter = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            }
            st.PointerMove(imagePos, pressed);
            RenderOverlay();
            return true;
        }

        // Brush/eraser cursor tetap terlihat saat pointer hanya hover.
        if (_activeTool is EditToolKind.Brush or EditToolKind.Eraser)
        {
            DrawBrushCursor(centered);
            return true;
        }

        // Cursor kuas Refine Edge.
        if (_activeTool == EditToolKind.RefineEdge)
        {
            DrawRefineEdgeCursor(imagePos);
            return true;
        }

        return false;
    }

    private bool EditorPointerReleased(Image img, PointerReleasedEventArgs e)
    {
        if (!_editMode || _session == null) return false;
        if (img.Name != "ImgResult") return false;

        if (_strokeActive)
        {
            EndStroke();
            e.Pointer.Capture(null);
            return true;
        }

        if (_movingSelection)
        {
            EndMoveSelection();
            e.Pointer.Capture(null);
            return true;
        }

        if (_refineEdgeActive)
        {
            EndRefineEdge();
            e.Pointer.Capture(null);
            return true;
        }

        if (_penDragIndex >= 0)
        {
            _penDragIndex = -1;
            e.Pointer.Capture(null);
            RenderOverlay();
            return true;
        }

        if (_activeSelectionTool is SelectionTool t && t.IsActive)
        {
            var pos = e.GetPosition(img);
            var imagePos = PointerToImage(pos);
            t.PointerUp(imagePos);

            // Pen tidak auto-commit saat mouse dilepas, sesuai perilaku Photoshop.
            if (!SelectionInteractionPolicy.CommitsOnPointerRelease(_activeTool))
            {
                e.Pointer.Capture(null);
                RenderOverlay();
                return true;
            }

            if (t.CanCommit) CommitSelectionToState(t, e.KeyModifiers);
            else { t.Cancel(); RenderOverlay(); }
            e.Pointer.Capture(null);
            return true;
        }

        e.Pointer.Capture(null);
        return false;
    }

    // ========================
    // BRUSH
    // ========================

    private void BeginStroke(Vec2 img, PointerPressedEventArgs e)
    {
        if (_session == null) return;
        bool restore = BrushTool.ShouldRestore(_activeTool, _settings.EditorBrushRestore);
        if (!_quickMask && restore && !_session.HasRestoreSource)
        {
            Toast("Piksel gambar asli belum siap; tunggu sebentar sebelum memakai Pulihkan.");
            e.Handled = true;
            return;
        }

        _strokeActive = true;
        _lastStrokeImage = img;
        _strokeToSelection = _quickMask;

        // Akumulasi Flow dibuat per-tile hanya saat brush menyentuhnya.
        _strokeAccum = new BrushStrokeAccumulator(_session.Width, _session.Height);

        // Quick Mask menulis ke lapisan seleksi (bukan mask), jadi tidak masuk
        // riwayat mask; sebaliknya brush normal di-snapshot sebagai satu undo.
        if (!_strokeToSelection)
            _session.BeginEdit(_activeTool == EditToolKind.Eraser ? "Eraser" : restore ? "Brush Restore" : "Brush Erase");

        var stamp = MakeStamp(img);
        _lastStamp = stamp;
        if (_strokeToSelection)
            BrushTool.StampFlow(_session.Selection.Coverage, _strokeAccum, _session.Width, _session.Height, stamp);
        else
            BrushTool.StampFlow(_session.Mask, _strokeAccum, _session.Width, _session.Height, stamp);
        var dirty = BrushTool.GetStampBounds(stamp, _session.Width, _session.Height);
        if (_strokeToSelection) MarkQuickMaskDirty(dirty);
        else MarkResultDirty(dirty);

        e.Pointer.Capture(this.FindControl<Image>("ImgResult"));
        ScheduleComposite(false, updateResult: !_strokeToSelection, updateQuickMask: _strokeToSelection);
        DrawBrushCursor(img);
        e.Handled = true;
    }

    private void ContinueStroke(Vec2 img)
    {
        if (_session == null) return;

        double spacing = Math.Max(1.0, _settings.EditorBrushSize * 0.2);
        double dx = img.X - _lastStrokeImage.X;
        double dy = img.Y - _lastStrokeImage.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < spacing) return;

        int steps = (int)Math.Min(100_000, Math.Ceiling(dist / spacing));
        var prev = _lastStamp;
        var dest = _strokeToSelection ? _session.Selection.Coverage : _session.Mask;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            var p = new Vec2(_lastStrokeImage.X + dx * t, _lastStrokeImage.Y + dy * t);
            var st = MakeStamp(p);
            BrushTool.StampFlow(dest, _strokeAccum, _session.Width, _session.Height, st);
            var dirty = BrushTool.GetStampBounds(st, _session.Width, _session.Height);
            if (_strokeToSelection) MarkQuickMaskDirty(dirty);
            else MarkResultDirty(dirty);
            prev = st;
        }

        _lastStamp = prev;
        _lastStrokeImage = img;

        ScheduleComposite(false, updateResult: !_strokeToSelection, updateQuickMask: _strokeToSelection);
        DrawBrushCursor(img);
    }

    private void EndStroke()
    {
        _strokeActive = false;
        _strokeAccum = null;

        if (_strokeToSelection)
        {
            // Quick Mask: goresan ditulis ke seleksi, bukan mask (tanpa undo mask).
            _session?.Selection.NotifyChanged();
            RenderAnts();
            ScheduleComposite(true, updateResult: false, updateQuickMask: true);
            UpdateEditorStatus();
        }
        else
        {
            string label = _activeTool == EditToolKind.Eraser ? "Eraser"
                : BrushTool.ShouldRestore(_activeTool, _settings.EditorBrushRestore) ? "Pulihkan Brush" : "Hapus Brush";
            AfterMaskChanged(label, fullRefresh: false);
        }
        _strokeToSelection = false;
    }

    private BrushStamp MakeStamp(Vec2 p) => new()
    {
        X = p.X,
        Y = p.Y,
        Radius = _settings.EditorBrushSize / 2.0,
        Hardness = _settings.EditorBrushHardness,
        Opacity = _settings.EditorBrushOpacity,
        Flow = _settings.EditorBrushFlow,
        Restore = BrushTool.ShouldRestore(_activeTool, _settings.EditorBrushRestore)
    };

    private void DrawBrushCursor(Vec2 img)
    {
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay == null || _session == null) return;

        // Lepas elemen cursor lama (objek di-cache agar tidak alokasi tiap gerakan).
        for (int i = overlay.Children.Count - 1; i >= 0; i--)
        {
            if (overlay.Children[i] is Ellipse el && el.Tag as string == "cursor")
                overlay.Children.RemoveAt(i);
        }

        // Posisi center-image (berbasis tengah) ke koordinat canvas overlay.
        var s = ImageToOverlay(new Vec2(img.X - _session.Width / 2.0, img.Y - _session.Height / 2.0));
        double r = _settings.EditorBrushSize / 2.0 * _viewPort.Zoom;
        if (r < 2) r = 2;

        if (_cursorOuter == null)
        {
            _cursorOuter = new Ellipse
            {
                Stroke = Brushes.White, StrokeThickness = 1, Tag = "cursor", Opacity = 0.9
            };
            _cursorInner = new Ellipse
            {
                Stroke = new SolidColorBrush(Color.Parse("#80FFFFFF")), StrokeThickness = 1,
                Tag = "cursor", Opacity = 0.7, StrokeDashArray = new AvaloniaList<double> { 2, 2 }
            };
        }

        // Lingkaran luar (ukuran brush).
        _cursorOuter.Width = r * 2;
        _cursorOuter.Height = r * 2;
        Canvas.SetLeft(_cursorOuter, s.X - r);
        Canvas.SetTop(_cursorOuter, s.Y - r);
        overlay.Children.Add(_cursorOuter);

        // Cincin dalam: radius hardness (batas area 100% opacity ala Photoshop).
        double hr = r * Math.Clamp(_settings.EditorBrushHardness, 0.0, 1.0);
        if (hr > 1)
        {
            _cursorInner!.Width = hr * 2;
            _cursorInner.Height = hr * 2;
            Canvas.SetLeft(_cursorInner, s.X - hr);
            Canvas.SetTop(_cursorInner, s.Y - hr);
            overlay.Children.Add(_cursorInner);
        }
    }

    /// <summary>Cursor kuas Refine Edge: lingkaran sesuai Edge Size + cincin feather.</summary>
    private void DrawRefineEdgeCursor(Vec2 imagePos)
    {
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay == null || _session == null) return;

        for (int i = overlay.Children.Count - 1; i >= 0; i--)
        {
            if (overlay.Children[i] is Ellipse el && el.Tag as string == "refinecursor")
                overlay.Children.RemoveAt(i);
        }

        var s = ImageToOverlay(imagePos);
        double r = Math.Max(2, _settings.EditorRefineEdgeSize / 2.0 * _viewPort.Zoom);

        var circle = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Stroke = new SolidColorBrush(Color.Parse("#FF7FD4FF")),
            StrokeThickness = 1,
            Tag = "refinecursor",
            Opacity = 0.9
        };
        Canvas.SetLeft(circle, s.X - r);
        Canvas.SetTop(circle, s.Y - r);
        overlay.Children.Add(circle);
    }

    // ========================
    // MAGIC WAND
    // ========================

    private void ApplyWand(Vec2 img, KeyModifiers mods)
    {
        if (_session == null) return;

        // Caller sudah mengirim koordinat piksel kiri-atas (imagePos + W/2).
        int x = (int)Math.Round(img.X);
        int y = (int)Math.Round(img.Y);
        if ((uint)x >= (uint)_session.Width || (uint)y >= (uint)_session.Height) return;

        var buf = _session.Composite(false); // RGB sumber tanpa mask
        byte[]? region;
        try
        {
            region = MagicWandTool.Compute(
                buf.Bgra, _session.Width, _session.Height, x, y,
                _settings.EditorWandTolerance, _settings.EditorWandContiguous,
                _settings.EditorWandSampleAlpha, 1.0,
                _settings.EditorWand8Connected);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MagicWand gagal: {ex.Message}");
            return;
        }
        if (region == null) return;

        // Wand menulis ke LAPISAN SELEKSI (persisten), bukan langsung ke mask.
        _session.Selection.CombineRaw(region, EffectiveMode(mods));
        AfterSelectionChanged("Wand");
    }

    private void OnWandToMaskClick(object? sender, RoutedEventArgs e)
    {
        // Kompatibilitas: terapkan selection sekarang ke mask sebagai "hapus".
        ApplySelectionToMask(0, "Wand Erase");
    }

    // ========================
    // SELECTION → STATE (persisten)
    // ========================

    /// <summary>
    /// Mode gabung efektif dari modifier: Shift → Add, Alt → Subtract,
    /// Ctrl+Shift → Intersect, selain itu mode tombol (<see cref="_selMode"/>).
    /// </summary>
    private SelectionCombineMode EffectiveMode(KeyModifiers mods)
    {
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool ctrl = mods.HasFlag(KeyModifiers.Control);
        if (ctrl && shift) return SelectionCombineMode.Intersect;
        if (alt) return SelectionCombineMode.Subtract;
        if (shift) return SelectionCombineMode.Add;
        return _selMode;
    }

    /// <summary>
    /// Commit tool seleksi (lasso/poly/pen) ke lapisan seleksi persisten.
    /// TIDAK menulis mask dan TIDAK menambah entri undo; user menerapkan
    /// seleksi ke mask lewat aksi eksplisit (Hapus/Restore/Isi).
    /// </summary>
    private void CommitSelectionToState(SelectionTool tool, KeyModifiers mods)
    {
        if (_session == null) return;
        var region = tool.Commit();
        if (region == null || region.IsEmpty)
        {
            tool.Cancel();
            RenderOverlay();
            return;
        }

        // PointerToImage and the editable path overlay use an image-centered origin;
        // MaskRasterizer indexes from the image's top-left. Convert only at commit.
        region.Translate(_session.Width / 2.0, _session.Height / 2.0);

        int feather = (int)Math.Round(_settings.EditorSelectionFeather);
        var mode = _pendingSelMode ?? EffectiveMode(mods);
        _pendingSelMode = null;
        _session.Selection.Combine(region, mode, _settings.EditorAntiAlias, feather);

        tool.Cancel();
        AfterSelectionChanged("Selection");
    }

    // ========================
    // SELEKSI: AKSI & MARCHING ANTS
    // ========================

    /// <summary>Terapkan lapisan seleksi ke mask (0 = hapus, 255 = restore/isi).</summary>
    private void ApplySelectionToMask(byte value, string label)
    {
        if (_session == null) return;
        if (!_session.Selection.HasSelection) { Toast("Belum ada selection"); return; }
        if (value > 0 && !_session.HasRestoreSource)
        {
            Toast("Piksel gambar asli belum siap; Pulihkan belum tersedia.");
            return;
        }

        _session.ApplySelectionToMask(value, label);
        AfterMaskChanged(label);
    }

    /// <summary>Dipicu setiap seleksi berubah: perbarui ants, status, tombol history.</summary>
    private void AfterSelectionChanged(string label)
    {
        RenderAnts();
        RenderOverlay();
        if (_quickMask && _session != null)
        {
            MarkQuickMaskDirty(PixelBounds.Full(_session.Width, _session.Height));
            ScheduleComposite(false, updateResult: false, updateQuickMask: true);
        }
        UpdateEditorStatus();
    }

    /// <summary>Gambar ulang marching ants dari kontur coverage seleksi.</summary>
    private void RenderAnts()
    {
        var white = this.FindControl<Avalonia.Controls.Shapes.Path>("SelectionAnts");
        var black = this.FindControl<Avalonia.Controls.Shapes.Path>("SelectionAntsShadow");
        if (white == null || black == null) return;

        var sel = Selection;
        // Quick Mask menyembunyikan marching ants (Photoshop).
        if (_quickMask) { white.Data = null; black.Data = null; StopAntsTimer(); return; }
        if (!_editMode || sel == null || !sel.HasSelection)
        {
            white.Data = null;
            black.Data = null;
            StopAntsTimer();
            return;
        }

        SyncViewPort();
        // Kontur seleksi (ruang gambar) di-cache per versi seleksi supaya
        // tidak menelusuri marching-squares O(W*H) tiap gerakan/zoom.
        if (_antsLoops == null || _antsVersion != sel.Version)
        {
            _antsLoops = SelectionContour.Trace(sel.Coverage, sel.Width, sel.Height);
            _antsVersion = sel.Version;
        }
        var loops = _antsLoops!;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            foreach (var loop in loops)
            {
                if (loop.Count < 3) continue;
                var p0 = ImageToGrid(new Vec2(loop[0].X - sel.Width / 2.0, loop[0].Y - sel.Height / 2.0));
                ctx.BeginFigure(p0, false);
                for (int i = 1; i < loop.Count; i++)
                {
                    var p = ImageToGrid(new Vec2(loop[i].X - sel.Width / 2.0, loop[i].Y - sel.Height / 2.0));
                    ctx.LineTo(p);
                }
                ctx.EndFigure(true);
            }
        }

        white.Data = geo;
        black.Data = geo;
        white.StrokeDashArray = new AvaloniaList<double> { 4, 4 };
        black.StrokeDashArray = new AvaloniaList<double> { 4, 4 };

        StartAntsTimer();
        TickAnts();
    }

    private void StartAntsTimer()
    {
        if (_antsTimer != null) return;
        int interval = Math.Max(40, _settings.EditorAntsAnimationMs);
        _antsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _antsTimer.Tick += (_, _) => TickAnts();
        _antsTimer.Start();
    }

    private void StopAntsTimer()
    {
        _antsTimer?.Stop();
        _antsTimer = null;
    }

    private void TickAnts()
    {
        var white = this.FindControl<Avalonia.Controls.Shapes.Path>("SelectionAnts");
        var black = this.FindControl<Avalonia.Controls.Shapes.Path>("SelectionAntsShadow");
        if (white == null || black == null) return;
        // Dua warna: garis hitam dan putih di-offset setengah periode (4 dari 8)
        // supaya bagian "kosong" putih terisi hitam dan sebaliknya. Animasi
        // hanya menggeser base offset (murah).
        _antsOn = !_antsOn;
        double baseOffset = _antsOn ? 0 : 4;
        white.StrokeDashOffset = baseOffset;
        black.StrokeDashOffset = (baseOffset + 4) % 8;
    }

    // ========================
    // GROW / SHRINK / FEATHER SELECTION
    // ========================

    private void GrowSelection()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast("Belum ada selection"); return; }
        int px = Math.Clamp(_settings.EditorSelGrowPx, 1, EditorSettings.MaxSelGrowPx);
        sel.Grow(px);
        AfterSelectionChanged("Grow");
    }

    private void ShrinkSelection()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast("Belum ada selection"); return; }
        int px = Math.Clamp(_settings.EditorSelGrowPx, 1, EditorSettings.MaxSelGrowPx);
        sel.Shrink(px);
        AfterSelectionChanged("Shrink");
    }

    private void FeatherSelection()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast("Belum ada selection"); return; }
        int px = Math.Max(1, (int)Math.Round(_settings.EditorSelectionFeather));
        sel.Feather(px);
        AfterSelectionChanged("Feather");
    }

    private void InvertSelection()
    {
        var sel = Selection;
        if (sel == null) return;
        sel.Invert();
        AfterSelectionChanged("Invert");
    }

    private void ClearSelection()
    {
        var sel = Selection;
        if (sel == null) return;
        sel.Clear();
        AfterSelectionChanged("None");
    }

    private void SelectAllSelection()
    {
        var sel = Selection;
        if (sel == null) return;
        sel.SelectAll();
        AfterSelectionChanged("All");
    }

    // ========================
    // MODE SELEKSI (radio ToggleButton)
    // ========================

    private void SetSelectionMode(SelectionCombineMode mode)
    {
        _selMode = mode;
        _settings.EditorSelectionMode = (int)mode;
        UpdateSelectionModeButtons();
    }

    private void UpdateSelectionModeButtons()
    {
        var map = new (string Name, SelectionCombineMode Mode)[]
        {
            ("BtnModeReplace", SelectionCombineMode.Replace),
            ("BtnModeAdd", SelectionCombineMode.Add),
            ("BtnModeSubtract", SelectionCombineMode.Subtract),
            ("BtnModeIntersect", SelectionCombineMode.Intersect),
        };
        _suppressOptionEvents = true;
        foreach (var (name, mode) in map)
            if (this.FindControl<ToggleButton>(name) is { } tb) tb.IsChecked = mode == _selMode;
        _suppressOptionEvents = false;
    }

    private void OnSelectionModeClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressOptionEvents) return;
        if (sender is not ToggleButton tb || tb.Name is not { } name) return;
        var mode = name switch
        {
            "BtnModeAdd" => SelectionCombineMode.Add,
            "BtnModeSubtract" => SelectionCombineMode.Subtract,
            "BtnModeIntersect" => SelectionCombineMode.Intersect,
            _ => SelectionCombineMode.Replace
        };
        SetSelectionMode(mode);
    }

    // ========================
    // MOVE SELECTION
    // ========================

    private void BeginMoveSelection(Vec2 image, KeyModifiers mods)
    {
        if (_session == null) return;
        _movingSelection = true;
        _moveStartImage = image;
        // Snapshot pra-drag: seleksi digeser dari snapshot (non-destruktif, tidak
        // terpotong permanen saat melewati tepi). Shift+drag juga memindah isi mask.
        _moveBaseCoverage = (byte[])_session.Selection.Coverage.Clone();
        _moveWithMask = mods.HasFlag(KeyModifiers.Shift);
        _moveBaseMask = _moveWithMask ? (byte[])_session.Mask.Clone() : null;
        // Snapshot dibuat saat drag benar-benar bergerak; klik tanpa drag bukan edit.
        _moveEditStarted = false;
    }

    private void ContinueMoveSelection(Vec2 image)
    {
        var sel = Selection;
        if (_session == null || sel == null || !_movingSelection || _moveBaseCoverage == null) return;

        // Delta kumulatif dari titik awal drag (bukan inkremental) agar tepi
        // tidak terpotong saat drag bolak-balik.
        int dx = (int)Math.Round(image.X - _moveStartImage.X);
        int dy = (int)Math.Round(image.Y - _moveStartImage.Y);
        if (dx == 0 && dy == 0) return;
        if (!_moveEditStarted)
        {
            _session.BeginEdit(_moveWithMask ? "Move Selection + Mask" : "Move Selection");
            _moveEditStarted = true;
        }

        ShiftBuffer(_moveBaseCoverage, sel.Coverage, sel.Width, sel.Height, dx, dy);
        sel.NotifyChanged();

        if (_moveWithMask && _moveBaseMask != null)
        {
            var shifted = new byte[_moveBaseMask.Length];
            ShiftBuffer(_moveBaseMask, shifted, sel.Width, sel.Height, dx, dy);
            _session.SetMaskRaw(shifted);
            _session.MarkDirty();
            RefreshResultBitmap();
        }
        RenderAnts();
    }

    /// <summary>Geser isi buffer (dx,dy) piksel dari <paramref name="src"/> ke
    /// <paramref name="dst"/> (ukuran sama); area di luar kanvas dipotong.</summary>
    internal static void ShiftBuffer(byte[] src, byte[] dst, int w, int h, int dx, int dy)
    {
        Array.Clear(dst, 0, dst.Length);
        for (int y = 0; y < h; y++)
        {
            int sy = y - dy;
            if (sy < 0 || sy >= h) continue;
            int dstRow = y * w;
            int srcRow = sy * w;
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if (sx < 0 || sx >= w) continue;
                dst[dstRow + x] = src[srcRow + sx];
            }
        }
    }

    private void EndMoveSelection()
    {
        _movingSelection = false;
        _moveBaseCoverage = null;
        _moveBaseMask = null;
        _moveWithMask = false;
        if (_moveEditStarted)
            AfterSelectionChanged("Move Selection");
        _moveEditStarted = false;
    }

    // ========================
    // REFINE EDGE BRUSH (non-AI)
    // ========================

    private void BeginRefineEdge(Vec2 image)
    {
        if (_session == null) return;
        _refineEdgeActive = true;
        _lastRefineImage = image;
        _session.BeginEdit("Refine Edge");
        ApplyRefineEdgeAt(image);
    }

    private void ContinueRefineEdge(Vec2 image)
    {
        if (_session == null || !_refineEdgeActive) return;
        double spacing = Math.Max(1.0, _settings.EditorRefineEdgeSize * 0.25);
        double dx = image.X - _lastRefineImage.X;
        double dy = image.Y - _lastRefineImage.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < spacing) return;

        int steps = (int)(dist / spacing);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            ApplyRefineEdgeAt(new Vec2(_lastRefineImage.X + dx * t, _lastRefineImage.Y + dy * t));
        }
        _lastRefineImage = image;
        ScheduleComposite(false);
        RenderOverlay();
    }

    private void EndRefineEdge()
    {
        _refineEdgeActive = false;
        AfterMaskChanged("Refine Edge");
        RefreshHistory();
    }

    private void ApplyRefineEdgeAt(Vec2 image)
    {
        if (_session == null) return;
        // `image` berbasis TENGAH gambar; RefineEdgeBand butuh koordinat kiri-atas.
        double cx = image.X + _session.Width / 2.0;
        double cy = image.Y + _session.Height / 2.0;
        double radius = Math.Max(2, _settings.EditorRefineEdgeSize / 2.0);
        int shift = Math.Max(1, _settings.EditorRefineEdgeSize / 20);
        MaskOperations.RefineEdgeBand(
            _session.Mask, _session.Width, _session.Height,
            cx, cy, radius, shift, expand: true,
            featherPx: _settings.EditorRefineEdgeFeather, hardness: 0.5);
    }

    // ========================
    // HISTORY PANEL
    // ========================

    private void RefreshHistory()
    {
        if (this.FindControl<ListBox>("LstHistory") is not { } list) return;
        if (_session == null) { list.ItemsSource = null; return; }

        var labels = _session.Undo.Labels;
        _suppressHistoryEvents = true;
        list.ItemsSource = labels;
        list.SelectedIndex = Math.Clamp(_session.Undo.CurrentIndex, 0, Math.Max(0, labels.Count - 1));
        _suppressHistoryEvents = false;
    }

    private void OnHistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressHistoryEvents || _session == null) return;
        if (sender is not ListBox lb) return;
        int index = lb.SelectedIndex;
        if (index < 0) return;
        if (index == _session.Undo.CurrentIndex) return;

        var mask = _session.Undo.JumpTo(index, _session.Mask, out var label);
        if (mask == null) return;
        _session.SetMaskRaw(mask);
        _session.MarkDirty();
        RefreshResultBitmap();
        RenderQuickMask();
        RenderAnts();
        RenderOverlay();
        UpdateEditorStatus();
        RefreshHistory();
    }

    // ========================
    // SAVE / LOAD SELECTION
    // ========================

    private async void OnSaveSelectionClick(object? sender, RoutedEventArgs e)
        => await SaveSelectionInteractively();

    private async Task SaveSelectionInteractively()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast("Belum ada selection untuk disimpan"); return; }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Simpan selection",
                SuggestedFileName = "selection.png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
            });
            if (file?.TryGetLocalPath() is not { } path || string.IsNullOrEmpty(path)) return;

            await SelectionIo.SaveAsync(sel, path);
            Toast("Selection disimpan");
        }
        catch (Exception ex)
        {
            Toast($"Gagal menyimpan selection: {ex.Message}");
        }
    }

    private async void OnLoadSelectionClick(object? sender, RoutedEventArgs e)
        => await LoadSelectionInteractively();

    private async Task LoadSelectionInteractively()
    {
        if (_session == null) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Muat selection",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
            });
            if (files.Count == 0) return;
            if (files[0].TryGetLocalPath() is not { } path || string.IsNullOrEmpty(path)) return;

            var loaded = await SelectionIo.LoadAsync(path, _session.Width, _session.Height);
            _session.Selection.CopyFrom(loaded);
            AfterSelectionChanged("Load Selection");
            Toast("Selection dimuat");
        }
        catch (Exception ex)
        {
            Toast($"Gagal memuat selection: {ex.Message}");
        }
    }

    // ========================
    // ZOOM INFO
    // ========================

    private void UpdateZoomText()
    {
        SyncViewPort();
        SetText("TxtZoom", $"Zoom {_viewPort.Zoom * 100:0}%");
    }

    // ========================
    // UNDO / REDO
    // ========================

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        var label = _session.UndoAction();
        if (label != null)
        {
            // History can change pixels outside the most recent stroke's dirty bounds.
            MarkResultDirty(PixelBounds.Full(_session.Width, _session.Height));
            RefreshResultBitmap();
            RenderQuickMask();
            RenderOverlay();
            UpdateEditorStatus();
            RefreshHistory();
        }
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        var label = _session.RedoAction();
        if (label != null)
        {
            // Recompose the full mask so no cached pixels from a later edit survive.
            MarkResultDirty(PixelBounds.Full(_session.Width, _session.Height));
            RefreshResultBitmap();
            RenderQuickMask();
            RenderOverlay();
            UpdateEditorStatus();
            RefreshHistory();
        }
    }

    // ========================
    // QUICK MASK
    // ========================

    private void OnQuickMaskClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        _quickMask = !_quickMask;

        if (_quickMask)
        {
            // Masuk Quick Mask: ingat tool lama, pakai Brush agar bisa melukis seleksi.
            _toolBeforeQuickMask = _activeTool;
            if (_activeTool != EditToolKind.Brush)
                SetActiveTool(EditToolKind.Brush);
            Toast("Quick Mask: lukis hitam=kurangi, putih=tambah seleksi (Q keluar)");
        }
        else
        {
            // Keluar Quick Mask: kembalikan tool sebelumnya bila masih brush/eraser.
            if (_activeTool == EditToolKind.Brush | _activeTool == EditToolKind.Eraser)
                SetActiveTool(_toolBeforeQuickMask);
        }

        UpdateQuickMaskButton();
        RenderAnts();
        RenderQuickMask();
        UpdateEditorStatus();
    }

    private void RenderQuickMask(PixelBounds? requestedBounds = null)
    {
        var img = this.FindControl<Image>("ImgQuickMask");
        if (img == null) return;

        if (!_quickMask || _session == null)
        {
            img.IsVisible = false;
            img.Source = null;
            return;
        }

        try
        {
            int w = _session.Width, h = _session.Height;
            bool created = _qmBuf == null || _qmBuf.Width != w || _qmBuf.Height != h;
            if (created)
            {
                _qmBuf = new PixelBuffer(w, h);
                _qmWb?.Dispose();
                _qmWb = new WriteableBitmap(new Avalonia.PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            }
            var bounds = (created ? PixelBounds.Full(w, h) :
                requestedBounds ?? _quickMaskDirtyBounds ?? PixelBounds.Full(w, h)).ClampTo(w, h);
            _quickMaskDirtyBounds = null;
            CompositeRenderer.RenderMaskOverlayInto(_qmBuf!, _session.Selection.Coverage, w, h,
                Color.Parse("#FFFF0000"), bounds, maxAlpha: 128, invert: false);
            _qmBuf!.WriteToUnpremul(_qmWb!, bounds);
            if (!ReferenceEquals(img.Source, _qmWb)) img.Source = _qmWb;
            img.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Quick mask gagal: " + ex.Message);
            img.IsVisible = false;
        }
    }

    // ========================
    // SAVE
    // ========================

    private void OnSaveEditClick(object? sender, RoutedEventArgs e) => SaveInPlace();

    private bool SaveInPlace()
    {
        if (_session == null) return false;
        if (_session.HasPendingRestore && !_session.HasRestoreSource)
        {
            Toast("Piksel gambar asli masih dimuat; tunggu sebelum menyimpan hasil restore.");
            return false;
        }

        try
        {
            // Backup sekali sebelum overwrite file asli.
            if (File.Exists(_resultPath) && !File.Exists(_resultPath + ".bak"))
                File.Copy(_resultPath, _resultPath + ".bak", false);

            _session.BakeToFile(_resultPath);
            _session.MarkSaved();

            Toast("Perubahan berhasil disimpan");
            Saved?.Invoke(this, _resultPath);
            UpdateEditorStatus();
            return true;
        }
        catch (Exception ex)
        {
            Toast($"Gagal menyimpan: {ex.Message}");
            return false;
        }
    }

    private async void OnSaveAsEditClick(object? sender, RoutedEventArgs e)
        => await SaveAsCopyInteractively();

    /// <summary>
    /// Tanya user apakah perubahan belum disimpan ditulis dulu (Simpan / Simpan Salinan),
    /// atau dibatalkan (Batal).
    /// </summary>
    /// <returns>
    /// true  = perubahan sudah disimpan in-place (aman lanjut tanpa dialog lagi);
    /// false = tulis sebagai salinan, atau user membatalkan → pemanggil lanjut dengan
    ///         pembersihan diri sendiri.
    /// </returns>
    private async Task<bool> ConfirmDiscardChanges()
    {
        if (_session is not { IsDirty: true }) return true;

        var tcs = new TaskCompletionSource<int>(); // 0 batal, 1 simpan in-place, 2 salinan
        var dialog = new Window
        {
            Title = "Perubahan belum disimpan",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1A1C20"))
        };

        var save = new Button { Content = "Simpan", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 0, 0, 0) };
        var copy = new Button { Content = "Simpan Salinan…", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Batal", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8) };

        save.Click += (_, _) => { tcs.TrySetResult(1); dialog.Close(); };
        copy.Click += (_, _) => { tcs.TrySetResult(2); dialog.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(0); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(0);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Simpan perubahan sebelum pindah gambar?", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                new TextBlock { Text = "Bila tidak disimpan, semua hasil edit (mask, brush, Refine Hair) akan hilang.", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#99FFFFFF")), TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, copy, save }
                }
            }
        };

        await dialog.ShowDialog(this);
        var choice = await tcs.Task;

        if (choice == 1) return SaveInPlace();
        if (choice == 2) { await SaveAsCopyInteractively(); return false; }
        return false;
    }

    /// <summary>
    /// Buka file picker "Simpan sebagai" dan tulis hasil edit sebagai salinan.
    /// Dipakai oleh tombol Save As dan oleh guard pindah gambar.
    /// </summary>
    private async Task SaveAsCopyInteractively()
    {
        if (_session == null) return;
        if (_session.HasPendingRestore && !_session.HasRestoreSource)
        {
            Toast("Piksel gambar asli masih dimuat; tunggu sebelum menyimpan hasil restore.");
            return;
        }

        try
        {
            var suggested = IOPath.GetFileNameWithoutExtension(string.IsNullOrEmpty(_resultPath) ? "hasil" : _resultPath)
                            + _settings.EditorOutputSuffix + ".png";

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Simpan sebagai",
                SuggestedFileName = suggested,
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } }
                }
            });

            if (file?.TryGetLocalPath() is not { } path || string.IsNullOrEmpty(path)) return;

            _session.BakeToFile(path);
            _session.MarkSaved();
            Toast("Salinan berhasil disimpan");
            Saved?.Invoke(this, path);
        }
        catch (Exception ex)
        {
            Toast($"Gagal menyimpan salinan: {ex.Message}");
        }
    }

    // ========================
    // REFINE HAIR
    // ========================
    /// <summary>
    /// Ambil region selection aktif (bila ada dan sudah membentuk path tertutup)
    /// untuk membatasi area Refine Hair. Null = seluruh gambar.
    /// </summary>
    private MaskRegion? BuildSelectionRegionForRefine()
    {
        var tool = _activeSelectionTool;
        if (tool is not { CanCommit: true } || tool.CurrentPath.Count < 3) return null;

        try
        {
            var region = new MaskRegion(tool.CurrentPath);
            return region.IsEmpty ? null : region;
        }
        catch
        {
            return null;
        }
    }

    private async void OnRefineHairClick(object? sender, RoutedEventArgs e)
    {
        if (_session == null) return;

        var spec = OnnxModelManager.Find(_settings.EditorRefineHairModel) ?? OnnxModelManager.ModNet;

        try
        {
            SetVisible("PanelRefineBusy", true);
            SetText("TxtRefineBusy", "Memeriksa model…");

            if (!OnnxModelManager.HasVerifiedSha256(spec))
            {
                Toast($"Refine Hair belum tersedia: checksum SHA-256 untuk {spec.DisplayName} belum diverifikasi; tidak ada model yang diunduh atau dijalankan.", warning: true);
                return;
            }

            if (!OnnxModelManager.IsInstalled(spec))
            {
                var confirm = await ConfirmDownload(spec);
                if (!confirm)
                {
                    SetVisible("PanelRefineBusy", false);
                    return;
                }

                SetText("TxtRefineBusy", "Mengunduh model…");
                var progress = new Progress<InstallProgressInfo>(p =>
                {
                    SetText("TxtRefineBusy", $"Mengunduh {spec.DisplayName}… {p.Percentage}%");
                });
                await OnnxModelManager.EnsureAvailableAsync(spec, progress, CancellationToken.None);
            }

            // Bila ada selection aktif yang sudah membentuk region, batasi refine
            // ke dalam selection saja (lebih cepat + sesuai maksud user).
            var selectionRegion = BuildSelectionRegionForRefine();
            if (selectionRegion != null && selectionRegion.IsEmpty) selectionRegion = null;

            _refineCts = new CancellationTokenSource();
            SetText("TxtRefineBusy", "Memproses Refine Hair…");

            var result = await _refineHair.RefineAsync(
                _session, spec, _settings.EditorRefineHairBandRadius,
                _settings.EditorRefineHairFeather, selectionRegion, _refineCts.Token);

            if (result.NoChange)
            {
                Toast("Tidak ada tepi yang perlu dirapikan");
            }
            else
            {
                _session.ReplaceMask(result.Mask, "Refine Hair");
                AfterMaskChanged("Refine Hair");
                Toast($"Refine Hair selesai ({result.Elapsed.TotalSeconds:0.0}s, {result.ExecutionProvider})");
            }

            UpdateEditorStatus();
        }
        catch (OperationCanceledException)
        {
            Toast("Refine Hair dibatalkan");
        }
        catch (Exception ex)
        {
            Toast($"Refine Hair gagal: {ex.Message}");
        }
        finally
        {
            SetVisible("PanelRefineBusy", false);
            _refineCts?.Dispose();
            _refineCts = null;
        }
    }

    private void OnRefineCancelClick(object? sender, RoutedEventArgs e) => _refineCts?.Cancel();

    private async Task<bool> ConfirmDownload(MattingModelSpec spec)
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = "Unduh model AI",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1A1C20"))
        };

        var yes = new Button { Content = "Unduh", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8) };
        var no = new Button { Content = "Batal", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 0, 0, 0) };
        yes.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        no.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"Model \"{spec.DisplayName}\" belum terpasang.", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                new TextBlock { Text = $"Ukuran unduhan ≈ {spec.SizeBytes / 1_048_576.0:0.#} MB · Lisensi {spec.License}", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#99FFFFFF")) },
                new TextBlock { Text = $"Unduh sekarang? Model disimpan di cache pengguna: {OnnxModelManager.ModelsDirectory}", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#99FFFFFF")), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { no, yes } }
            }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    // ========================
    // STATUS & TOAST
    // ========================

    private void AfterMaskChanged(string label, bool fullRefresh = true)
    {
        // Render overlay/status segera; komposit bitmap di-coalesce maksimal sekitar 30 FPS.
        if (fullRefresh && _session != null)
            MarkResultDirty(PixelBounds.Full(_session.Width, _session.Height));
        RenderOverlay();
        UpdateEditorStatus();
        RefreshHistory();
        ScheduleComposite(false);
    }


    private void UpdateEditorStatus()
    {
        if (_session == null) return;

        SetText("TxtEditorImageSize", $"{_session.Width} × {_session.Height}");
        SetText("TxtEditorTool", _activeTool.ToString());
        SetText("TxtEditorUndo", $"Undo {_session.Undo.UndoCount} / Redo {_session.Undo.RedoCount}");
        SetText("TxtEditorProvider", $"ONNX: {_refineHair.DescribeProvider()}");

        var sel = _session.Selection;
        string selInfo = sel.HasSelection
            ? $"Selection: {sel.CountSelected() / 1_000_000.0:0.##} Mpx · {_selMode}"
            : "Selection: -";
        SetText("TxtSelInfo", selInfo);
        SetText("TxtSelStatus", sel.HasSelection
            ? $"Ada selection · {sel.CountSelected() / 1_000_000.0:0.##} Mpx"
            : "Belum ada selection");
        UpdateZoomText();

        var undoBtn = this.FindControl<Button>("BtnUndo");
        if (undoBtn != null) undoBtn.IsEnabled = _session.CanUndo;
        var redoBtn = this.FindControl<Button>("BtnRedo");
        if (redoBtn != null) redoBtn.IsEnabled = _session.CanRedo;

        UpdateQuickMaskButton();
    }

    private void UpdateQuickMaskButton()
    {
        var b = this.FindControl<Button>("BtnQuickMask");
        if (b == null) return;
        if (_quickMask)
        {
            if (!b.Classes.Contains("active")) b.Classes.Add("active");
        }
        else
        {
            b.Classes.Remove("active");
        }
    }

    private void Toast(string message, bool warning = false)
    {
        var toast = this.FindControl<Border>("ToastNotification");
        if (toast == null) return;

        toast.Background = new SolidColorBrush(Color.Parse(warning ? "#B45309" : "#18A05A"));
        toast.BorderBrush = new SolidColorBrush(Color.Parse(warning ? "#FDBA74" : "#40FFFFFF"));
        var icon = this.FindControl<PathIcon>("ToastIcon");
        if (icon != null)
            icon.Data = Avalonia.Media.Geometry.Parse(warning
                ? "M10,2H14L13,15H11L10,2M11,18H13V22H11Z"
                : "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.58L21,7Z");

        if (toast.Child is StackPanel sp)
        {
            foreach (var c in sp.Children)
                if (c is TextBlock tb) tb.Text = message;
        }

        toast.IsVisible = true;
        DispatcherTimer.RunOnce(() => toast.IsVisible = false, TimeSpan.FromSeconds(2.5));
    }

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
            if (_session == null) return false;
            EnterEditMode();
            return _editMode;
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
        if (ctrlOnly && e.Key == Key.S) { SaveInPlace(); return true; }

        // Persistent selection commands use Photoshop's standard combinations.
        if (ctrlShift && e.Key == Key.I) { InvertSelection(); return true; }
        if (ctrlOnly && e.Key == Key.J) { GrowSelection(); return true; }
        if (ctrlShift && e.Key == Key.J) { ShrinkSelection(); return true; }
        if (ctrlOnly && e.Key == Key.A) { SelectAllSelection(); return true; }
        if (ctrlOnly && e.Key == Key.D) { ClearSelection(); return true; }

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
            if (_activeSelectionTool is PenTool pt && pt.CanCommit)
            {
                CommitSelectionToState(pt, e.KeyModifiers);
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
        if (noModifiers && (e.Key == Key.Back || e.Key == Key.Delete))
        {
            _activeSelectionTool?.RemoveLastPoint();
            RenderOverlay();
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
            }
            return true;
        }

        return false;
    }

    private void AdjustBrushSize(int delta)
    {
        _settings.EditorBrushSize = Math.Clamp(_settings.EditorBrushSize + delta * Math.Max(1, _settings.EditorBrushSize / 10), 1, 500);
        SetSlider("SldBrushSize", _settings.EditorBrushSize);
        UpdateOptionLabels();
    }
    // ========================
    // WIRING KONTROL
    // ========================

    /// <summary>
    /// Pasang semua event handler + nilai awal untuk kontrol editor.
    /// Dipanggil sekali per gambar setelah PrepareEditorAsync, aman dipanggil ulang
    /// (handler memakai delegate method, bukan lambda anonim yang bisa dobel).
    /// </summary>
    private void WireEditorControls()
    {
        // Tombol masuk/keluar mode edit (kanvas + header).
        Bind("BtnEnterEdit", OnEnterEditClick);
        Bind("BtnEnterEditHeader", OnEnterEditClick);
        if (this.FindControl<NumericUpDown>("ZoomControl") is { } zoomControl)
        {
            zoomControl.ValueChanged -= OnEditorZoomChanged;
            zoomControl.ValueChanged += OnEditorZoomChanged;
        }
        Bind("BtnExitEdit", OnExitEditClick);
        Bind("BtnExitEditHeader", OnExitEditClick);

        // Toggle "Bandingkan" (Original transparan di atas hasil).
        if (this.FindControl<ToggleButton>("BtnCompare") is { } cmp)
        {
            cmp.IsCheckedChanged -= OnCompareToggled;
            cmp.IsCheckedChanged += OnCompareToggled;
        }
        // Rail tool: satu handler untuk semua, tool dibaca dari Tag.
        foreach (var name in new[] { "BtnToolPan", "BtnToolMove", "BtnToolLasso", "BtnToolPolyLasso",
                                     "BtnToolWand", "BtnToolPen", "BtnToolBrush", "BtnToolEraser", "BtnToolRefineEdge" })
        {
            var btn = this.FindControl<Button>(name);
            if (btn == null) continue;
            btn.Click -= OnToolClick;
            btn.Click += OnToolClick;
        }

        // Semua kontrol options bar (slider/tombol/checkbox) + state awal.
        ApplyEditorOptionsToUi();

        // Sinkron status awal.
        HighlightActiveTool(_activeTool);
        UpdateOptionsBarVisibility(_activeTool);
        UpdateToolHint(_activeTool);
        UpdateEditorStatus();
    }

    /// <summary>Simpan preferensi editor ke file settings (dipanggil saat window close).</summary>
    private void PersistEditorSettings()
    {
        try
        {
            ReadOptionsFromUi();
            _settings.EditorSelectionMode = (int)_selMode;
            _settings.ActiveEditTool = (int)_activeTool;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PersistEditorSettings gagal: {ex.Message}");
        }
    }

    /// <summary>Lepas buffer bitmap pratinjau (dipanggil saat ganti gambar / dispose).</summary>
    private void DisposePreviewBuffers()
    {
        _resultWb?.Dispose(); _resultWb = null;
        _resultBuf = null;
        _qmWb?.Dispose(); _qmWb = null;
        _qmBuf = null;
        _previewW = -1; _previewH = -1;
        _resultDirtyBounds = null;
        _quickMaskDirtyBounds = null;
        _pendingResultRefresh = false;
        _pendingQuickMaskRefresh = false;
        _antsLoops = null; _antsVersion = -1;
        _cursorOuter = null; _cursorInner = null;
    }

    // ========================
    // DISPOSE
    // ========================

    private void DisposeEditor()
    {
        StopAntsTimer();
        try { _refineCts?.Cancel(); } catch { }
        try { _refineHair.Dispose(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        DisposePreviewBuffers();
    }
}

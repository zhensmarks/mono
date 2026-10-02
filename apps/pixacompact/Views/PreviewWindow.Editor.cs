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

    /// <summary>
    /// Alasan restore masking tidak tersedia (null = tersedia). Diisi saat sesi
    /// disiapkan bila gambar Original gagal dimuat; dipakai untuk pesan eksplisit
    /// ke user (tidak lagi diam-diam).
    /// </summary>
    private string? _restoreUnavailableReason;
    /// <summary>
    /// true saat penyiapan sesi editor berjalan di background (Putaran 2 #11).
    /// Selama true, tombol masuk-editor dinonaktifkan + berlabel "Menyiapkan..."
    /// agar klik sebelum session siap tidak silent-fail (Tugas A Putaran 2).
    /// </summary>
    private bool _sessionPreparing;
    /// <summary>
    /// Alasan FATAL terakhir sesi editor gagal disiapkan (null = belum dicoba /
    /// sukses). Dipakai untuk Toast eksplisit + percobaan ulang otomatis.
    /// </summary>
    private string? _sessionFatalError;
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

    /// <summary>
    /// Mask view ala Photoshop (\): tampilkan mask SESUNGGUHNYA sebagai hitam-putih
    /// (hitam = tersembunyi, putih = tampil), bukan gambar komposit.
    /// Berbeda dengan Quick Mask (Q) yang menampilkan SELEKSI sebagai overlay merah.
    /// </summary>
    private bool _maskView;
    private EditToolKind _toolBeforeQuickMask = EditToolKind.Brush;
    private bool _suppressOptionEvents;
    private SelectionTool? _activeSelectionTool;
    /// <summary>Undo/redo untuk Pen path (daftar anchor). Minimal untuk Putaran 2.</summary>
    private readonly Stack<List<PenTool.Anchor>> _pathUndo = new();
    private readonly Stack<List<PenTool.Anchor>> _pathRedo = new();
    private bool _strokeActive;
    private Vec2 _lastStrokeImage;
    private BrushStrokeAccumulator? _strokeAccum;
    private bool _strokeToSelection; // true = goresan ditulis ke Selection.Coverage (Quick Mask)
    private BrushStamp _lastStamp;
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
    // ---- Brush HUD ala Photoshop (Alt + klik kanan + geser) ----
    private bool _brushHudActive;
    private Point _brushHudStart;
    private int _brushHudSize0;
    private double _brushHudHardness0;
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
    /// <summary>Putar PixelBuffer 90° searah jarum jam (untuk restore saat dimensi tertukar).</summary>
    private static PixelBuffer Rotate90Clockwise(PixelBuffer src)
    {
        // src: W×H -> hasil: H×W. rotated(xr, yr) = src(yr, H-1-xr).
        var dst = new PixelBuffer(src.Height, src.Width);
        var s = src.Bgra; var d = dst.Bgra;
        int w = src.Width, h = src.Height;
        for (int yr = 0; yr < w; yr++)
        {
            for (int xr = 0; xr < h; xr++)
            {
                int xo = yr;
                int yo = h - 1 - xr;
                int si = (yo * w + xo) * 4;
                int di = (yr * h + xr) * 4;
                d[di] = s[si]; d[di+1] = s[si+1]; d[di+2] = s[si+2]; d[di+3] = s[si+3];
            }
        }
        return dst;
    }

    private async Task<bool> PrepareEditorAsync(string originalPath, string resultPath, PixelBuffer? existingResult = null)
    {
        try
        {
            if (!_settings.EditorBetaMode) return false;
            // Putaran 2 #11: decode berat di background thread agar UI tidak freeze.
            // UI sudah tampil dengan placeholder; session diisi saat decode selesai.
            var (result, original, restoreIssue) = await Task.Run(() =>
            {
                PixelBuffer? r = existingResult;
                PixelBuffer? o = null;
                string? issue = null;
                var srcPath = File.Exists(resultPath) ? resultPath : originalPath;
                if (!File.Exists(srcPath)) return ((PixelBuffer?)null, (PixelBuffer?)null, (string?)"file tidak ditemukan");
                if (r == null)
                {
                    try
                    {
                        using var bmp = new Bitmap(srcPath);
                        r = PixelBuffer.FromBitmap(bmp);
                    }
                    catch { r = null; }
                }
                if (r == null) return ((PixelBuffer?)null, (PixelBuffer?)null, (string?)"gagal decode hasil");
                bool sameFile = string.Equals(originalPath, srcPath, StringComparison.OrdinalIgnoreCase);                if (sameFile)
                {
                    o = r;
                }
                else if (File.Exists(originalPath))
                {
                    try
                    {
                        using var ob = new Bitmap(originalPath);
                        var obuf = PixelBuffer.FromBitmap(ob);
                        if (obuf.Width == r.Width && obuf.Height == r.Height)
                            o = obuf;
                        else if (obuf.Width == r.Height && obuf.Height == r.Width)
                            o = Rotate90Clockwise(obuf);
                        else
                            issue = $"ukuran gambar asli ({obuf.Width}×{obuf.Height}) tidak sama dengan hasil ({r.Width}×{r.Height})";
                    }
                    catch (Exception ex)
                    {
                        issue = $"gagal memuat gambar asli ({ex.Message})";
                    }
                }
                else
                {
                    issue = "file gambar asli tidak ditemukan";
                }
                return (r, o, issue);
            });
            if (result == null)
            {
                // Gagal FATAL (bukan sekadar restore tak tersedia): catat alasannya
                // agar UI bisa memberi Toast eksplisit + menawarkan retry (Tugas A).
                // Saat result==null, 'restoreIssue' berisi alasan fatal dari Task.Run
                // ("file tidak ditemukan" / "gagal decode hasil").
                _sessionFatalError = restoreIssue ?? "gagal menyiapkan sesi editor";
                return false;
            }
            EndEditMode(silent: true);
            _session?.Dispose();
            _session = null;
            DisposePreviewBuffers();
            _refineHair.Reset();

            _session = new MaskEditSession(result, original,
                _settings.EditorUndoSteps, _settings.EditorUndoMemoryMb);
            _sessionFatalError = null;
            _restoreUnavailableReason = original != null ? null : restoreIssue;
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
            _sessionFatalError = $"gagal menyiapkan sesi editor ({ex.Message})";
            return false;
        }
    }

    /// <summary>
    /// Memulai penyiapan sesi editor dengan status "preparing" yang terlihat di UI
    /// (Tugas A Putaran 2). Mencegah silent-fail: selama penyiapan berjalan, tombol
    /// masuk-editor dinonaktifkan dan berlabel "Menyiapkan...". Bila gagal, alasan
    /// fatal dicatat (<see cref="_sessionFatalError"/>) dan Toast ditampilkan sekali.
    /// Aman dipanggil dari thread manapun; pembaruan UI selalu di-dispatch.
    /// </summary>
    private async Task BeginPrepareEditorSessionAsync(string originalPath, string resultPath, PixelBuffer? existingResult = null)
    {
        if (_sessionPreparing) return;
        _sessionPreparing = true;
        _sessionFatalError = null;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateFooterModeButton);
        bool ok;
        try { ok = await PrepareEditorAsync(originalPath, resultPath, existingResult); }
        catch (Exception ex)
        {
            _sessionFatalError = ex.Message;
            ok = false;
        }
        _sessionPreparing = false;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateFooterModeButton();
            WireEditorControls();
            if (!ok && !_editMode && _sessionFatalError != null)
                Toast(T("Toast_EditorSessionFailed", _sessionFatalError), warning: true);
        });
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
            await BeginPrepareEditorSessionAsync(_originalPath, _resultPath);
        WireEditorControls();
    }

    // MASUK / KELUAR MODE EDIT
    // ========================

    private void OnEnterEditClick(object? sender, RoutedEventArgs e) => EnterEditMode();

    private void EnterEditMode()
    {
        // Tugas A Putaran 2: JANGAN silent-fail. Setiap kondisi yang menghalangi
        // masuk editor WAJIB memberi feedback (Toast) ke user.
        if (!_settings.EditorBetaMode)
        {
            Toast(T("Toast_EditorBetaOff"), warning: true);
            return;
        }
        if (_sessionPreparing)
        {
            Toast(T("Toast_EditorPreparing"), warning: true);
            return;
        }
        if (_session == null)
        {
            if (_sessionFatalError != null)
                Toast(T("Toast_EditorSessionFailed", _sessionFatalError), warning: true);
            else
                Toast(T("Toast_EditorNoSession"), warning: true);
            // Tawarkan percobaan ulang otomatis: siapkan sesi di background.
            if (!string.IsNullOrWhiteSpace(_resultPath))
                _ = BeginPrepareEditorSessionAsync(_originalPath, _resultPath);
            return;
        }

        _editMode = true;
        _tabPanelsHidden = false;

        SetVisible("PanelToolRail", true);
        SetVisible("PanelOptionsBar", true);
        SetVisible("PanelMenuBar", true);
        SetVisible("PanelEditorStatus", true);
        SetVisible("BtnEnterEdit", false);
        SetVisible("BtnEnterEditHeader", false);
        SetVisible("BtnExitEditHeader", true);
        SetVisible("ChipEditMode", true);
        SetVisible("BtnFooterMode", true);

        // Mode edit: panel floating Bandingkan/Zoom di atas kanvas disembunyikan
        // (diganti toggle Bandingkan di options bar); label "Result" juga disembunyikan
        // agar tampilan rapat ala Photoshop. Preview mode tidak berubah.
        SetVisible("PanelViewControls", false);
        SetVisible("TxtResultLabel", false);

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
        UpdateMaskViewButton();

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

        // Layer V2 dinonaktifkan di mode masking: panel Layers disembunyikan karena
        // tombol-tombolnya tidak menerima input dan tidak relevan untuk workflow masking.
        // (Lihat PIXELCUT_EDIT_FIX_SUMMARY.md Tahap 2.)
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
        _maskView = false;
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

        // Kembalikan panel floating + label Result untuk preview mode.
        SetVisible("PanelViewControls", true);
        SetVisible("TxtResultLabel", true);

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
        void Sync(ToggleButton? btn)
        {
            if (btn == null) return;
            if (btn.IsChecked != _compareOriginal)
            {
                _suppressOptionEvents = true;
                btn.IsChecked = _compareOriginal;
                _suppressOptionEvents = false;
            }
        }
        Sync(this.FindControl<ToggleButton>("BtnCompare"));
        Sync(this.FindControl<ToggleButton>("BtnCompareOptions"));
        Sync(this.FindControl<ToggleButton>("BtnCompareBottom"));
        if (this.FindControl<MenuItem>("MiCompare") is { } mi && mi.IsChecked != _compareOriginal)
            mi.IsChecked = _compareOriginal;
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
        // Tugas A Putaran 2: selama session disiapkan di background, tombol
        // dinonaktifkan + berlabel "Menyiapkan..." (tidak silent-fail).
        if (button != null) button.IsEnabled = !_sessionPreparing || _editMode;
        if (text != null)
            text.Text = _editMode ? T("Footer_EditorMode")
                : _sessionPreparing ? T("Editor_Preparing")
                : T("Footer_OpenEditor");
        if (button != null) ToolTip.SetTip(button, _editMode ? T("Tip_ToggleEditor") : T("Tip_OpenEditor"));
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
        _penDragHandleIndex = -1;
        _penHoverKind = PenHoverKind.None;
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
         SetText("TxtOptionsTitle", ToolDisplayName(kind));
         SetText("TxtEditorTool", ToolDisplayName(kind));

         var overlay = this.FindControl<Canvas>("EditOverlay");
         if (overlay != null) ClearOverlayDynamic(overlay);

         UpdateEditorStatus();
     }

    /// <summary>Handler tetap agar bisa di-unsubscribe (menghindari kebocoran event).</summary>
    private void _onToolChanged() => RenderOverlay();

    private string ToolDisplayName(EditToolKind kind) => kind switch
    {
        EditToolKind.Pan => T("Tool_Pan"),
        EditToolKind.Move => T("Tool_Move"),
        EditToolKind.Lasso => T("Tool_Lasso"),
        EditToolKind.PolyLasso => T("Tool_PolyLasso"),
        EditToolKind.MagicWand => T("Tool_MagicWand"),
        EditToolKind.Pen => T("Tool_Pen"),
        EditToolKind.Brush => T("Tool_Brush"),
        EditToolKind.Eraser => T("Tool_Eraser"),
        EditToolKind.RefineEdge => T("Tool_RefineEdge"),
        EditToolKind.RectMarquee => T("Tool_RectMarquee"),
        EditToolKind.EllipseMarquee => T("Tool_EllipseMarquee"),
        _ => kind.ToString()
    };

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
        // NOTE: highlight aktif via class "active"; jika style multi-class bermasalah,
        // fallback ke properti langsung.
        foreach (var (name, k) in map)
        {
            var b = this.FindControl<Button>(name);
            if (b == null) continue;
            bool isActive = k == kind;
            if (isActive)
            {
                if (!b.Classes.Contains("active")) b.Classes.Add("active");
                b.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x3A, 0x52));
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x31, 0xA8, 0xFF));
            }
            else
            {
                b.Classes.Remove("active");
                b.ClearValue(Button.BackgroundProperty);
                b.ClearValue(Button.BorderBrushProperty);
            }
        }
     }


    private void SetText(string name, string text)
    {
        var t = this.FindControl<TextBlock>(name);
        if (t != null) t.Text = text;
    }

    /// <summary>Ambil string lokalilasi dari ResourceDictionary bahasa aktif (Strings.id/en.axaml).</summary>
    protected string T(string key, params object[] args)
    {
        string s = key;
        if (this.TryFindResource(key, out var v) && v is string str && !string.IsNullOrEmpty(str))
            s = str;
        return args.Length == 0 ? s : string.Format(s, args);
    }


    /// <summary>
    /// <summary>IServiceProvider minimal agar ctor ResourceInclude tidak NRE.</summary>
    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>
    /// Ganti bahasa UI mode edit ("id"/"en"): tukar merged dictionary Strings,
    /// simpan ke settings, lalu segarkan semua teks yang di-set dari code.
    /// Berlaku langsung tanpa restart (DynamicResource di axaml ikut ter-update).
    /// </summary>
    private void ApplyEditorLanguage(string lang, bool save = true)
    {
        lang = lang == "en" ? "en" : "id";
        // Dictionary bahasa tinggal di level Application agar semua window (termasuk dialog Preferensi) ikut.
        if (Application.Current?.Resources is ResourceDictionary res)
        {
            string want = $"Strings.{lang}.axaml";
            bool already = false;
            for (int i = res.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (res.MergedDictionaries[i] is ResourceInclude inc
                    && inc.Source?.ToString().Contains("/Strings.") == true)
                {
                    if (inc.Source.ToString().EndsWith(want, StringComparison.OrdinalIgnoreCase))
                        already = true;
                    else
                        res.MergedDictionaries.RemoveAt(i);
                }
            }
            if (!already)
                // NB: ResourceInclude(Uri) hanya mengisi _baseUri, bukan Source
                // (get_Loaded butuh Source) — dan ctor IServiceProvider butuh
                // provider non-null. Pakai provider minimal + set Source eksplisit.
                res.MergedDictionaries.Add(new ResourceInclude(NullServiceProvider.Instance)
                {
                    Source = new Uri($"avares://PixelcutCompact/Resources/Strings.{lang}.axaml")
                });
        }
        if (save)
        {
            _settings.EditorLanguage = lang;
            _settings.Save();
        }
        else
        {
            _settings.EditorLanguage = lang;
        }
        RefreshLocalizedTexts();
    }

    /// <summary>Segarkan semua teks yang di-set dari code-behind setelah ganti bahasa.</summary>
    private void RefreshLocalizedTexts()
    {
        UpdateToolHint(_activeTool);
        UpdateEditorStatus();
        UpdateOptionLabels();
        SetText("TxtOptionsTitle", ToolDisplayName(_activeTool));
        SetText("TxtEditorTool", ToolDisplayName(_activeTool));
        UpdateBrushModeButton();
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

            if (_maskView)
                RenderMaskGrayscaleInto(_resultBuf, bounds);
            else
                _session.CompositeInto(_resultBuf, bounds);
            _resultBuf.WriteToUnpremul(_resultWb, bounds);
            if (!ReferenceEquals(img.Source, _resultWb)) img.Source = _resultWb;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Refresh komposit gagal: " + ex.Message);
        }
    }

    /// <summary>
    /// Gambar mask sebagai grayscale ke buffer pratinjau (untuk Mask view):
    /// hitam (0) = tersembunyi, putih (255) = tampil. Alpha selalu penuh agar
    /// mask terlihat solid seperti Alt+klik thumbnail mask di Photoshop.
    /// </summary>
    private void RenderMaskGrayscaleInto(PixelBuffer buf, PixelBounds bounds)
    {
        if (_session == null) return;
        var mask = _session.Mask;
        var dest = buf.Bgra;
        int w = _session.Width;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            int pixel = y * w + bounds.X;
            int end = pixel + bounds.Width;
            for (; pixel < end; pixel++)
            {
                byte v = pixel < mask.Length ? mask[pixel] : (byte)0;
                int bi = pixel * 4;
                dest[bi] = v;
                dest[bi + 1] = v;
                dest[bi + 2] = v;
                dest[bi + 3] = 255;
            }
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




    private void DrawPathOutline(Canvas overlay, IReadOnlyList<Vec2> pts, bool closed, bool penStyle = false)
    {
        if (pts.Count < 2) return;
        double thickness = penStyle ? PenPathThickness() : 1.5;
        IBrush stroke = penStyle
            ? new SolidColorBrush(PenPathColor())
            : new SolidColorBrush(Color.Parse("#FFE24A"));

        if (penStyle)
        {
            // Underlay gelap tipis: garis path selalu terbaca di atas kanvas terang maupun gelap.
            AddPathPolyline(overlay, pts, closed, Brushes.Black, thickness + 2.0, 0.7);
        }
        AddPathPolyline(overlay, pts, closed, stroke, thickness, 1.0);

        if (closed && pts.Count >= 3)
        {
            var s0 = ImageToOverlay(pts[0]);
            var sl = ImageToOverlay(pts[pts.Count - 1]);
            if (penStyle)
            {
                DrawLine(overlay, sl, s0, "#000000", width: thickness + 2.0);
                DrawLine(overlay, sl, s0, PenPathStyle.NormalizeColor(_settings.EditorPenPathColor), width: thickness);
            }
            else DrawLine(overlay, sl, s0, "#FFE24A");
        }
    }

    private void AddPathPolyline(Canvas overlay, IReadOnlyList<Vec2> pts, bool closed, IBrush stroke, double thickness, double opacity)
    {
        var poly = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            Opacity = opacity,
        };
        if (!closed) poly.StrokeDashArray = new AvaloniaList<double> { 4, 3 };

        foreach (var p in pts)
        {
            poly.Points.Add(ImageToOverlay(p));
        }
        overlay.Children.Add(poly);
    }

    private void DrawLine(Canvas overlay, Point a, Point b, string color, bool dash = false, double width = 1)
    {
        var line = new Line
        {
            StartPoint = a,
            EndPoint = b,
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = width
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

    /// <summary>Photoshop: Ctrl++ / Ctrl+- = zoom in/out dari tengah kanvas.</summary>
    private void EditorZoomStep(double factor)
    {
        if (_session == null) return;
        var img = this.FindControl<Image>("ImgResult");
        if (img == null) return;
        SyncViewPort();
        var bounds = img.Bounds;
        var center = new Point(bounds.Width / 2.0, bounds.Height / 2.0);
        if (_viewPort.ZoomAtViewportPoint(center, factor))
        {
            ApplyEditorViewTransform();
            OnViewTransformChanged();
        }
    }

    /// <summary>Photoshop: Ctrl+0 = fit to screen.</summary>
    private void EditorZoomFit()
    {
        if (_session == null) return;
        SyncViewPort();
        _viewPort.FitToView();
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
        // Photoshop: Alt + klik kanan + geser = HUD ukuran/hardness brush.
        // Klik kanan biasa tetap tidak melakukan apa-apa, jadi tidak ada konflik.
        if (pt.Properties.IsRightButtonPressed
            && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !_strokeActive
            && _activeTool is EditToolKind.Brush or EditToolKind.Eraser)
        {
            BeginBrushHud(pos);
            e.Pointer.Capture(img);
            return true;
        }
        // Klik kanan tidak boleh mengubah mask/selection. Pan tetap ditangani viewer.
        if (!left) return false;

        if (SelectionInteractionPolicy.HandlesPointerPress(_activeTool))
        {
            if (_activeSelectionTool == null) return false;
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // Mode kombinasi (add/sub/…) hanya di-latch untuk tool yang commit saat
            // pointer dilepas (lasso/marquee). Pen & poly-lasso commit eksplisit via
            // Enter/double-click belakangan, jadi pakai modifier saat itu (jangan basi).
            _pendingSelMode = SelectionInteractionPolicy.CommitsOnPointerRelease(_activeTool)
                ? EffectiveMode(e.KeyModifiers)
                : null;
            if (_activeSelectionTool is PenTool penTool)
            {
                // Photoshop: double-click HANYA menaruh 2 anchor biasa, TIDAK menutup path.
                // Path ditutup dengan klik pada anchor pertama (lihat PenTool.AddAnchor).
                // Enter = akhiri path (tetap terbuka); Esc = batalkan path.
                // Drag knob handle Bézier diprioritaskan di atas tambah anchor —
                // ini cara utama mengedit kurva setelah anchor ditempatkan.
                var (handleHit, handleAnchor, handleIsIn) = penTool.HitTestHandle(imagePos, 10);
                if (handleHit)
                {
                    _penDragHandleIndex = handleAnchor;
                    _penDragHandleIsIn = handleIsIn;
                    e.Pointer.Capture(img);
                    RenderOverlay();
                    return true;
                }
                int anchorHit = penTool.HitTestAnchor(imagePos, 10);
                if (alt && anchorHit >= 0)
                {
                    // Photoshop: Alt+klik anchor = convert smooth→corner; Alt+drag
                    // langsung menarik handle-out baru dari anchor tersebut.
                    penTool.ConvertToCorner(anchorHit);
                    _penDragHandleIndex = anchorHit;
                    _penDragHandleIsIn = false;
                    e.Pointer.Capture(img);
                    RenderOverlay();
                    return true;
                }
                if (ctrl && !alt)
                {
                    if (anchorHit >= 0) { _penDragIndex = anchorHit; e.Pointer.Capture(img); return true; }
                }
                e.Pointer.Capture(img);
                penTool.PointerDown(imagePos, alt);
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

        // Brush HUD ala Photoshop (Alt + klik kanan + geser).
        if (_brushHudActive)
        {
            UpdateBrushHud(pos, centered);
            return true;
        }

        // Drag anchor pen (Ctrl+klik pada anchor, direct-selection).
        if (_penDragIndex >= 0 && _activeSelectionTool is PenTool penDrag)
        {
            penDrag.MoveAnchor(_penDragIndex, imagePos);
            RenderOverlay();
            return true;
        }

        // Drag knob handle Bézier pen; tahan Alt untuk mematahkan simetri handle.
        if (_penDragHandleIndex >= 0 && _activeSelectionTool is PenTool penHandle)
        {
            var ha = penHandle.Anchors[_penDragHandleIndex];
            var rel = new Vec2(imagePos.X - ha.Point.X, imagePos.Y - ha.Point.Y);
            penHandle.SetHandle(_penDragHandleIndex, _penDragHandleIsIn, rel,
                mirror: !e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            RenderOverlay();
            return true;
        }

        // Hover highlight anchor/handle pen + kursor kontekstual (tanpa drag).
        if (_activeSelectionTool is PenTool penHov && penHov.Anchors.Count > 0
            && _penDragIndex < 0 && _penDragHandleIndex < 0
            && !e.GetCurrentPoint(img).Properties.IsLeftButtonPressed)
        {
            if (UpdatePenHover(penHov, imagePos))
                RenderOverlay();
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

        // Akhiri brush HUD (tombol kanan) sebelum handler tombol kiri.
        if (_brushHudActive)
        {
            EndBrushHud();
            e.Pointer.Capture(null);
            return true;
        }

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

        if (_penDragIndex >= 0 || _penDragHandleIndex >= 0)
        {
            _penDragIndex = -1;
            _penDragHandleIndex = -1;
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
            Toast(T("Toast_OriginalNotReady"));
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

    // ========================
    // BRUSH HUD (Alt + klik kanan + geser, ala Photoshop)
    // ========================

    private void BeginBrushHud(Point screenPos)
    {
        _brushHudActive = true;
        _brushHudStart = screenPos;
        _brushHudSize0 = _settings.EditorBrushSize;
        _brushHudHardness0 = _settings.EditorBrushHardness;
    }

    private void UpdateBrushHud(Point screenPos, Vec2 centered)
    {
        var (size, hardness) = BrushHudMath.Compute(
            _brushHudSize0, _brushHudHardness0,
            screenPos.X - _brushHudStart.X, screenPos.Y - _brushHudStart.Y);
        _settings.EditorBrushSize = size;
        _settings.EditorBrushHardness = hardness;
        DrawBrushCursor(centered);
        DrawBrushHudLabel(centered);
    }

    private void EndBrushHud()
    {
        _brushHudActive = false;
        _settings.Save();
        SetSlider("SldBrushSize", _settings.EditorBrushSize);
        SyncHardnessSlider();
        UpdateOptionLabels();
        RenderOverlay();
    }

    /// <summary>Label live "120 px · 80%" di samping cursor selama HUD aktif.</summary>
    private void DrawBrushHudLabel(Vec2 img)
    {
        var overlay = this.FindControl<Canvas>("EditOverlay");
        if (overlay == null || _session == null) return;
        for (int i = overlay.Children.Count - 1; i >= 0; i--)
        {
            if (overlay.Children[i] is TextBlock tb && tb.Tag as string == "hud")
                overlay.Children.RemoveAt(i);
        }
        var s = ImageToOverlay(new Vec2(img.X - _session.Width / 2.0, img.Y - _session.Height / 2.0));
        double r = _settings.EditorBrushSize / 2.0 * _viewPort.Zoom;
        // Konvensi persen mengikuti label options bar (100% - hardness internal).
        var label = new TextBlock
        {
            Text = $"{_settings.EditorBrushSize} px · {(int)Math.Round((1.0 - _settings.EditorBrushHardness) * 100)}%",
            Foreground = Brushes.White,
            FontSize = 11,
            Background = new SolidColorBrush(Color.Parse("#CC1A1D21")),
            Padding = new Thickness(6, 3),
            Tag = "hud"
        };
        Canvas.SetLeft(label, s.X + r + 10);
        Canvas.SetTop(label, s.Y - r - 12);
        overlay.Children.Add(label);
    }

    /// <summary>Photoshop: Shift+[ / Shift+] = hardness brush.</summary>
    private void AdjustBrushHardness(double delta)
    {
        _settings.EditorBrushHardness = Math.Clamp(_settings.EditorBrushHardness + delta, 0.0, 1.0);
        _settings.Save();
        SyncHardnessSlider();
        UpdateOptionLabels();
    }

    /// <summary>Photoshop: tombol 1..9 = opacity 10%..90%, 0 = 100%.</summary>
    private static bool TryOpacityDigit(Key key, out double opacity)    {
        int d = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            Key.D0 or Key.NumPad0 => 10,
            _ => -1
        };
        opacity = d / 10.0;
        return d > 0;
    }

    /// <summary>Slider hardness memakai skala terbalik (lihat ReadOptionsFromUi).</summary>
    private void SyncHardnessSlider() =>
        SetSlider("SldBrushHardness", (1.0 - _settings.EditorBrushHardness) * 100.0);

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
    private void CommitSelectionToState(SelectionTool tool, KeyModifiers mods, SelectionCombineMode? forceMode = null)
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
        // forceMode (misal Ctrl+Shift+Enter = Add) mengalahkan pending & modifier,
        // ala Photoshop: Shift selalu berarti tambah.
        var mode = forceMode ?? _pendingSelMode ?? EffectiveMode(mods);
        _pendingSelMode = null;
        _session.PushSelectionUndo(T("Undo_Selection"));
        _session.Selection.Combine(region, mode, _settings.EditorAntiAlias, feather);

        tool.Cancel();
        AfterSelectionChanged("Selection");
    }

    // ========================
    // SELEKSI: AKSI & MARCHING ANTS
    // ========================

    /// <summary>
    /// Terapkan lapisan seleksi ke mask (0 = hapus/sembunyikan, 255 = restore/tampilkan).
    /// Ala Photoshop: bila tidak ada seleksi, fill diterapkan ke SELURUH mask
    /// (fill hitam = sembunyikan semua, fill putih = kembalikan seluruh gambar).
    /// </summary>
    private void ApplySelectionToMask(byte value, string label)
    {
        if (_session == null) return;
        if (value > 0 && !_session.HasRestoreSource)
        {
            Toast(RestoreUnavailableMessage(), warning: true);
            return;
        }

        bool fillAll = !_session.Selection.HasSelection;
        _session.ApplySelectionToMask(value, fillAll ? label + " (seluruh gambar)" : label, fillAll: fillAll);
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
        if (sel == null || !sel.HasSelection) { Toast(T("Toast_NoSelection")); return; }
        int px = Math.Clamp(_settings.EditorSelGrowPx, 1, EditorSettings.MaxSelGrowPx);
        sel.Grow(px);
        AfterSelectionChanged("Grow");
    }

    private void ShrinkSelection()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast(T("Toast_NoSelection")); return; }
        int px = Math.Clamp(_settings.EditorSelGrowPx, 1, EditorSettings.MaxSelGrowPx);
        _session?.PushSelectionUndo(T("Undo_Shrink"));
        sel.Shrink(px);
        AfterSelectionChanged("Shrink");
    }

    private void FeatherSelection()
    {
        var sel = Selection;
        if (sel == null || !sel.HasSelection) { Toast(T("Toast_NoSelection")); return; }
        int px = Math.Max(1, (int)Math.Round(_settings.EditorSelectionFeather));
        _session?.PushSelectionUndo(T("Undo_Feather"));
        sel.Feather(px);
        AfterSelectionChanged("Feather");
    }

    private void InvertSelection()
    {
        var sel = Selection;
        if (sel == null) return;
        _session?.PushSelectionUndo(T("Undo_Invert"));
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
        _session?.PushSelectionUndo(T("Undo_SelectAll"));
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
    private static void ShiftBuffer(byte[] src, byte[] dst, int w, int h, int dx, int dy)
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

    /// <summary>Photoshop: arrow keys = geser selection 1px (Shift = 10px). Undoable.</summary>
    private bool NudgeSelection(int dx, int dy)
    {
        var sel = Selection;
        if (_session == null || sel == null || !sel.HasSelection) return false;
        if (dx == 0 && dy == 0) return false;
        _session.BeginEdit("Nudge Selection");
        // ShiftBuffer mengosongkan dst dulu, jadi sumber harus salinan.
        var src = (byte[])sel.Coverage.Clone();
        ShiftBuffer(src, sel.Coverage, sel.Width, sel.Height, dx, dy);
        sel.NotifyChanged();
        AfterSelectionChanged("Nudge Selection");
        return true;
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
        if (sel == null || !sel.HasSelection) { Toast(T("Toast_NoSelectionSave")); return; }

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
            Toast(T("Toast_SelSaved"));
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
            Toast(T("Toast_SelLoaded"));
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
        SetText("TxtZoom", T("Status_Zoom", $"{_viewPort.Zoom * 100:0}"));
    }

    // ========================
    // UNDO / REDO
    // ========================

    /// <summary>Salin daftar anchor Pen untuk snapshot undo/redo.</summary>
    private static List<PenTool.Anchor> CloneAnchors(PenTool pen)
    {
        var list = new List<PenTool.Anchor>(pen.Anchors.Count);
        foreach (var a in pen.Anchors)
        {
            var c = new PenTool.Anchor(new Vec2(a.Point.X, a.Point.Y))
            {
                HandleIn = new Vec2(a.HandleIn.X, a.HandleIn.Y),
                HandleOut = new Vec2(a.HandleOut.X, a.HandleOut.Y)
            };
            list.Add(c);
        }
        return list;
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        // Prioritaskan undo path Pen yang sedang digambar.
        if (_activeSelectionTool is PenTool pen && pen.Anchors.Count > 0)
        {
            _pathRedo.Push(CloneAnchors(pen));
            pen.Cancel();
            RenderOverlay();
            UpdateEditorStatus();
            return;
        }
        var label = _session.UndoAction();
        if (label != null)
        {
            // History can change pixels outside the most recent stroke's dirty bounds.
            MarkResultDirty(PixelBounds.Full(_session.Width, _session.Height));
            RefreshResultBitmap();
            RenderQuickMask();
            RenderAnts();
            RenderOverlay();
            UpdateEditorStatus();
            RefreshHistory();
        }
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        // Prioritaskan redo path Pen.
        if (_activeSelectionTool is PenTool penRedo && _pathRedo.Count > 0 && penRedo.Anchors.Count == 0)
        {
            var anchors = _pathRedo.Pop();
            _pathUndo.Push(new List<PenTool.Anchor>());
            penRedo.RestoreAnchors(anchors);
            RenderOverlay();
            UpdateEditorStatus();
            return;
        }
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

        if (_quickMask && _maskView)
        {
            // Quick Mask dan Mask view saling lepas.
            _maskView = false;
            UpdateMaskViewButton();
            _resultDirtyBounds = PixelBounds.Full(_session.Width, _session.Height);
            RefreshResultBitmap();
        }

        if (_quickMask)
        {
            // Masuk Quick Mask: ingat tool lama, pakai Brush agar bisa melukis seleksi.
            _toolBeforeQuickMask = _activeTool;
            if (_activeTool != EditToolKind.Brush)
                SetActiveTool(EditToolKind.Brush);
            Toast(T("Toast_QuickMask"));
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

    /// <summary>
    /// Toggle Mask view (\) ala Photoshop: tampilkan mask sebagai hitam-putih.
    /// Saling lepas dengan Quick Mask agar tidak membingungkan.
    /// </summary>
    private void OnMaskViewClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode || _session == null) return;
        _maskView = !_maskView;

        if (_maskView && _quickMask)
        {
            // Keluar dari Quick Mask dulu (tanpa toast ganda).
            _quickMask = false;
            if (_activeTool == EditToolKind.Brush | _activeTool == EditToolKind.Eraser)
                SetActiveTool(_toolBeforeQuickMask);
            UpdateQuickMaskButton();
            RenderQuickMask();
        }

        UpdateMaskViewButton();
        // Gambar ulang seluruh kanvas dalam mode yang baru.
        _resultDirtyBounds = PixelBounds.Full(_session.Width, _session.Height);
        RefreshResultBitmap();
        UpdateEditorStatus();
        if (_maskView)
            Toast(T("Toast_MaskView"));
    }

    private void UpdateMaskViewButton()
    {
        if (this.FindControl<Button>("BtnMaskView") is { } b)
        {
            if (_maskView) { b.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x3A, 0x52)); b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x31, 0xA8, 0xFF)); }
            else { b.ClearValue(Button.BackgroundProperty); b.ClearValue(Button.BorderBrushProperty); }
        }
        if (this.FindControl<MenuItem>("MiMaskView") is { } mi && mi.IsChecked != _maskView)
            mi.IsChecked = _maskView;
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
            SetText("TxtRefineBusy", T("Refine_Checking"));

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

                SetText("TxtRefineBusy", T("Refine_Downloading"));
                var progress = new Progress<InstallProgressInfo>(p =>
                {
                    SetText("TxtRefineBusy", T("Refine_DownloadingName", spec.DisplayName, p.Percentage));
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
                Toast(T("Toast_NoEdge"));
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
            Toast(T("Toast_RefineCancelled"));
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
            Background = new SolidColorBrush(Color.Parse("#2B2B2B"))
        };

        var yes = new Button
        {
            Content = "Unduh", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0xA8, 0xFF)),
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand)
        };
        var no = new Button
        {
            Content = "Batal", Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand)
        };
        yes.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        no.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"Model \"{spec.DisplayName}\" belum terpasang.", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")) },
                new TextBlock { Text = $"Ukuran unduhan ≈ {spec.SizeBytes / 1_048_576.0:0.#} MB · Lisensi {spec.License}", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")) },
                new TextBlock { Text = $"Unduh sekarang? Model disimpan di cache pengguna: {OnnxModelManager.ModelsDirectory}", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")), TextWrapping = TextWrapping.Wrap },
                // Tombol vertikal full-width seragam, ala referensi Photoshop.
                new StackPanel { Spacing = 6, Children = { yes, no } }
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
        SetText("TxtEditorTool", ToolDisplayName(_activeTool));
        SetText("TxtEditorUndo", T("Status_UndoRedo", _session.Undo.UndoCount, _session.Undo.RedoCount));
        SetText("TxtEditorProvider", $"ONNX: {_refineHair.DescribeProvider()}");

        var sel = _session.Selection;
        string selInfo = sel.HasSelection
            ? T("Status_SelSome", $"{sel.CountSelected() / 1_000_000.0:0.##}", _selMode)
            : T("Status_SelNone");
        SetText("TxtSelInfo", selInfo);
        SetText("TxtSelStatus", sel.HasSelection
            ? T("Status_SelStatusSome", $"{sel.CountSelected() / 1_000_000.0:0.##}")
            : T("SelStatus_None"));
        UpdateZoomText();
        UpdateEditMenuState();

        UpdateQuickMaskButton();
    }

    private void UpdateQuickMaskButton()
    {
        var b = this.FindControl<Button>("BtnQuickMask");
        if (b == null) return;
        // QuickMask pakai warna kemerahan sesuai desain awal.
        if (_quickMask) { b.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x25, 0x2B)); b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0x79, 0x80)); }
        else { b.ClearValue(Button.BackgroundProperty); b.ClearValue(Button.BorderBrushProperty); }
        if (this.FindControl<MenuItem>("MiQuickMask") is { } mi && mi.IsChecked != _quickMask)
            mi.IsChecked = _quickMask;
    }

    /// <summary>
    /// Highlight tombol tool via properti langsung (bukan class), karena
    /// selector multi-class tidak bekerja di Avalonia 11.3.
    /// </summary>


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
        if (ctrlOnly && e.Key == Key.S) { SaveInPlace(); return true; }

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
        Bind("BtnExitEditHeader", OnExitEditClick);

        // Toggle "Bandingkan" (Original transparan di atas hasil): floating (preview)
        // dan options bar (mode edit) terhubung ke state yang sama.
        if (this.FindControl<ToggleButton>("BtnCompare") is { } cmp)
        {
            cmp.IsCheckedChanged -= OnCompareToggled;
            cmp.IsCheckedChanged += OnCompareToggled;
        }
        if (this.FindControl<ToggleButton>("BtnCompareBottom") is { } cmpBottom)
        {
            cmpBottom.IsCheckedChanged -= OnCompareToggled;
            cmpBottom.IsCheckedChanged += OnCompareToggled;
        }
        if (this.FindControl<ToggleButton>("BtnCompareOptions") is { } cmpOpt)
        {
            cmpOpt.IsCheckedChanged -= OnCompareToggled;
            cmpOpt.IsCheckedChanged += OnCompareToggled;
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

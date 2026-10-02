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

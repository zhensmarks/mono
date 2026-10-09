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

/// <summary>Manajemen sesi editor (dipisah agar file < batas push).</summary>
public partial class PreviewWindow
{
    // Catatan: utilitas rotasi/orientasi restore kini di RestoreOrientation
    // (Services/Editing) agar bisa diuji unit via logiccheck.

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
                        // WS1 Putaran 3: selaraskan original ke ruang display via EXIF
                        // (konsisten dengan LoadBitmapWithOrientation di jalur preview),
                        // lalu deteksi otomatis arah rotasi bila dimensi tertukar.
                        // Tidak ada lagi asumsi buta "selalu CW".
                        int exifOrientation = 1;
                        try { exifOrientation = PixelcutCompact.Helpers.ExifHelper.GetOrientation(originalPath); }
                        catch { }
                        var (norm, normIssue) = PixelcutCompact.Services.Editing.RestoreOrientation
                            .NormalizeOriginalForSession(obuf, exifOrientation, r);
                        o = norm;
                        issue = normIssue;
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
            // Mode editor bawaan OFF: alihkan ke editor eksternal (Photoshop /
            // PhotoCraft / custom) sebagai jembatan agar user tetap bisa mengedit
            // hasil di editor profesional. Bila belum ada path, buka Preferensi.
            LaunchExternalEditor();
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

        // Ikuti pilihan menu Window; jangan memaksa panel tampil.
        ApplyEditorDockVisibility();
        SetVisible("PanelMenuBar", true);
        SetVisible("PanelEditorStatus", true);
        SetVisible("BtnEnterEdit", false);
        SetVisible("BtnEnterEditHeader", false);
        SetVisible("BtnExitEditHeader", true);
        SetVisible("ChipEditMode", true);
        // Tombol Bandingkan melayang di tengah-bawah kanvas (hold-to-preview).
        SetVisible("BtnCompareFloat", true);
        // Footer navigasi (prev/next + tombol mode) tidak relevan di dalam editor:
        // sembunyikan agar tampilan bersih ala Photoshop. Toast tetap muncul (overlay root).
        SetVisible("PanelFooter", false);

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

        // Panel Layers (DocSession V2): isi sesi dokumen dari gambar hasil supaya
        // daftar layer tampil. Kanvas tetap dipegang sesi mask (tidak diubah).
        try { EnsureLayerDocSession(_session.Result); } catch { }

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
        StopSpinner();

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
        SetVisible("BtnCompareFloat", false);
        SetVisible("ChipEditMode", false);
        SetVisible("BtnExitEditHeader", false);
        SetVisible("BtnEnterEditHeader", _session != null);

        // Kembalikan panel floating + label Result untuk preview mode.
        SetVisible("PanelViewControls", true);
        SetVisible("TxtResultLabel", true);
        SetVisible("PanelFooter", true);   // footer navigasi kembali di mode preview

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

    // Tombol Bandingkan melayang (mode edit): KLIK = toggle tampilkan gambar asli.
    private void OnCompareFloatClick(object? sender, RoutedEventArgs e)
    {
        if (!_editMode) return;
        _compareOriginal = !_compareOriginal;
        ApplyCompareOverlay();
    }

    private void ApplyCompareOverlay()
    {
        var img = this.FindControl<Image>("ImgOriginalCompare");
        if (img == null) return;

        // Selalu pastikan sumber = gambar original (bukan hasil), agar klik pertama
        // langsung menampilkan perbandingan (dulu source hanya di-set bila null).
        var orig = this.FindControl<Image>("ImgOriginal")?.Source;
        if (orig != null && !ReferenceEquals(img.Source, orig)) img.Source = orig;

        img.IsVisible = _editMode && _compareOriginal;
        // Tombol: sorot saat aktif (via class agar tidak bertabrakan dengan style hover).
        if (this.FindControl<Button>("BtnCompareFloat") is { } b)
        {
            if (_compareOriginal)
            {
                if (!b.Classes.Contains("active")) b.Classes.Add("active");
            }
            else b.Classes.Remove("active");
        }
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
        if (this.FindControl<MenuItem>("MiCompare") is { } mi && mi.IsChecked != _compareOriginal)
            mi.IsChecked = _compareOriginal;
    }
    /// <summary>
    /// Buka editor eksternal (Photoshop / PhotoCraft / custom) dengan file hasil
    /// + file asli. Dipanggil saat tombol Mode Edit ditekan padahal editor
    /// bawaan (EditorBetaMode) sedang OFF — sebagai jembatan ke editor profesional.
    /// </summary>
    private void LaunchExternalEditor()
    {
        if (string.IsNullOrEmpty(_resultPath) || string.IsNullOrEmpty(_originalPath))
        {
            Toast(T("Toast_NoImageForExternalEditor"), warning: true);
            return;
        }

        var kind = ExternalEditorService.ParseKind(_settings.ExternalEditorKind);
        var path = _settings.ExternalEditorPath;

        // Semua editor (Photoshop, PhotoCraft, Custom) memakai file executable.
        // PhotoCraft boleh juga diarahkan ke folder repo (kompatibilitas lama).
        var exe = kind == ExternalEditorKind.Photocraft
            ? ExternalEditorService.ResolvePhotocraftPath(path)
            : path;

        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            Toast(T("Toast_ExternalEditorNotConfigured"), warning: true);
            OpenPreferences();
            return;
        }

        if (ExternalEditorService.Launch(kind, exe, _resultPath, _originalPath, out var err))
            Toast(T("Toast_ExternalEditorLaunched", kind.ToString()));
        else
            Toast(T("Toast_ExternalEditorFailed", err), warning: true);
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
}

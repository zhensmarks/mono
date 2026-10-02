using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

public partial class PreviewWindow
{
    private void UpdateOptionsBarVisibility(EditToolKind kind)
    {
        SetVisible("OptBrushGroup", kind is EditToolKind.Brush or EditToolKind.Eraser);
        SetVisible("OptWandGroup", kind == EditToolKind.MagicWand);
        SetVisible("OptPenGroup", kind == EditToolKind.Pen);
        SetVisible("OptSelectionGroup", kind is EditToolKind.Lasso or EditToolKind.PolyLasso or EditToolKind.Pen
            or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee);
        SetVisible("OptRefineEdgeGroup", kind == EditToolKind.RefineEdge);
        // Mode seleksi selalu tampil saat mode edit (tool seleksi & wand).
        SetVisible("OptSelectionModeGroup", kind is EditToolKind.Lasso or EditToolKind.PolyLasso 
            or EditToolKind.Pen or EditToolKind.MagicWand or EditToolKind.Move or EditToolKind.RefineEdge 
            or EditToolKind.RectMarquee or EditToolKind.EllipseMarquee); 
    }

    private void UpdateToolHint(EditToolKind kind)
    {
        UpdateEditorShortcutToolTips();
        string Shortcut(EditorShortcutAction action) => EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, action);
        string delMask = Shortcut(EditorShortcutAction.MaskDelete);
        string restoreMask = Shortcut(EditorShortcutAction.MaskRestore);
        string maskHint = T("Hint_MaskPart", delMask, restoreMask);
        string hint = kind switch
        {
            EditToolKind.Pan => T("Hint_Pan", Shortcut(EditorShortcutAction.Pan)),
            EditToolKind.Lasso => T("Hint_Lasso", Shortcut(EditorShortcutAction.Lasso), maskHint),
            EditToolKind.PolyLasso => T("Hint_PolyLasso", Shortcut(EditorShortcutAction.PolygonLasso)),
            EditToolKind.MagicWand => T("Hint_MagicWand", Shortcut(EditorShortcutAction.MagicWand), maskHint),
            EditToolKind.Pen => T("Hint_Pen", Shortcut(EditorShortcutAction.Pen), Shortcut(EditorShortcutAction.MakeSelection)),
            EditToolKind.Brush => T("Hint_Brush", Shortcut(EditorShortcutAction.Brush), Shortcut(EditorShortcutAction.BrushSizeDown), Shortcut(EditorShortcutAction.BrushSizeUp), Shortcut(EditorShortcutAction.ToggleBrushMode)),
            EditToolKind.Eraser => T("Hint_Eraser", Shortcut(EditorShortcutAction.Eraser), Shortcut(EditorShortcutAction.BrushSizeDown), Shortcut(EditorShortcutAction.BrushSizeUp)),
            EditToolKind.Move => T("Hint_Move", Shortcut(EditorShortcutAction.Move)),
            EditToolKind.RefineEdge => T("Hint_RefineEdge", Shortcut(EditorShortcutAction.RefineEdge)),
            EditToolKind.RectMarquee => T("Hint_RectMarquee", Shortcut(EditorShortcutAction.RectMarquee), maskHint),
            EditToolKind.EllipseMarquee => T("Hint_EllipseMarquee", Shortcut(EditorShortcutAction.EllipseMarquee), maskHint),
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

        SetTip("BtnToolPan", T("Tip_ToolPan", Shortcut(EditorShortcutAction.Pan)));
        SetTip("BtnToolMove", T("Tip_ToolMove", Shortcut(EditorShortcutAction.Move)));
        SetTip("BtnToolLasso", T("Tip_ToolLasso", Shortcut(EditorShortcutAction.Lasso)));
        SetTip("BtnToolPolyLasso", T("Tip_ToolPolyLasso", Shortcut(EditorShortcutAction.PolygonLasso)));
        SetTip("BtnToolWand", T("Tip_ToolWand", Shortcut(EditorShortcutAction.MagicWand)));
        SetTip("BtnToolPen", T("Tip_ToolPen", Shortcut(EditorShortcutAction.Pen), Shortcut(EditorShortcutAction.MakeSelection)));
        SetTip("BtnToolBrush", T("Tip_ToolBrush", Shortcut(EditorShortcutAction.Brush), Shortcut(EditorShortcutAction.BrushSizeDown), Shortcut(EditorShortcutAction.BrushSizeUp), Shortcut(EditorShortcutAction.ToggleBrushMode)));
        SetTip("BtnToolEraser", T("Tip_ToolEraser", Shortcut(EditorShortcutAction.Eraser), Shortcut(EditorShortcutAction.BrushSizeDown), Shortcut(EditorShortcutAction.BrushSizeUp)));
        SetTip("BtnToolRefineEdge", T("Tip_ToolRefineEdge", Shortcut(EditorShortcutAction.RefineEdge)));
        SetTip("BtnToolRectMarquee", T("Tip_ToolRectMarquee", Shortcut(EditorShortcutAction.RectMarquee)));
        SetTip("BtnToolEllipseMarquee", T("Tip_ToolEllipseMarquee", Shortcut(EditorShortcutAction.EllipseMarquee)));
        SetTip("BtnQuickMask", T("Tip_QuickMask", Shortcut(EditorShortcutAction.QuickMask)));
        SetTip("BtnMaskView", T("Tip_MaskView", Shortcut(EditorShortcutAction.MaskView)));
        SetTip("BtnBrushRestore", T("Tip_BrushRestore", Shortcut(EditorShortcutAction.ToggleBrushMode)));
        SetTip("BtnApplyErase", T("Tip_FillBlack", Shortcut(EditorShortcutAction.MaskDelete)));
        SetTip("BtnApplyRestore", RestoreTip(Shortcut(EditorShortcutAction.MaskRestore)));
        SetTip("BtnApplyErasePanel", T("Tip_FillBlack", Shortcut(EditorShortcutAction.MaskDelete)));
        SetTip("BtnApplyRestorePanel", RestoreTip(Shortcut(EditorShortcutAction.MaskRestore)));
        UpdateEditMenuShortcuts();
    }

    /// <summary>Tooltip tombol Restore: eksplisit bila restore tidak tersedia.</summary>
    private string RestoreTip(string shortcut)
    {
        var baseTip = T("Tip_FillWhite", shortcut);
        return _restoreUnavailableReason == null
            ? baseTip
            : T("Tip_FillWhiteNA", shortcut, _restoreUnavailableReason);
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
            if (this.FindControl<Slider>("SldPenThickness") is { } s10) s10.PropertyChanged += OnOptionChanged;

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

            Bind("BtnBrushAdvanced", (_, _) => SetVisible("OptBrushAdvancedGroup",
                !(this.FindControl<Control>("OptBrushAdvancedGroup")?.IsVisible ?? false)));
            Bind("BtnQuickMask", OnQuickMaskClick);
            Bind("BtnMaskView", OnMaskViewClick);
            Bind("BtnRefineHair", OnRefineHairClick);
            Bind("BtnRefineCancel", OnRefineCancelClick);
            WireEditMenu();
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
        SetSlider("SldPenThickness", PenPathStyle.ClampThickness(_settings.EditorPenPathThickness));
        _suppressOptionEvents = false;
        UpdateOptionLabels();
    }

    /// <summary>Toggle mode Erase/Restore brush (dipakai juga saat startup & shortcut X).</summary>
    private void UpdateBrushModeButton()
    {
        if (this.FindControl<Button>("BtnBrushRestore") is { } tb)
        {
            bool isEraser = _activeTool == EditToolKind.Eraser;
            tb.IsVisible = !isEraser;
            tb.Content = _settings.EditorBrushRestore ? T("BrushMode_Restore") : T("BrushMode_Erase");
            tb.Background = _settings.EditorBrushRestore
                ? new SolidColorBrush(Color.Parse("#3348D17A"))
                : new SolidColorBrush(Color.Parse("#15FFFFFF"));
            // WS2 Putaran 3: bila Hapus disembunyikan (eraser), Lanjutan bentang
            // penuh grid agar tidak ada kolom kosong.
            if (this.FindControl<Button>("BtnBrushAdvanced") is { } adv)
                Grid.SetColumnSpan(adv, isEraser ? 2 : 1);
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
        // Segarkan tooltip agar alasan nonaktifnya eksplisit (tidak diam-diam).
        UpdateEditorShortcutToolTips();
    }

    /// <summary>Pesan restore yang konsisten: pakai alasan eksplisit bila ada.</summary>
    private string RestoreUnavailableMessage() =>
        _restoreUnavailableReason != null
            ? $"Restore tidak tersedia: {_restoreUnavailableReason}."
            : "Piksel gambar asli belum siap; Pulihkan belum tersedia.";

    private void OnBrushModeClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTool == EditToolKind.Eraser) return;
        if (!_settings.EditorBrushRestore && _session?.HasRestoreSource != true)
        {
            Toast(RestoreUnavailableMessage(), warning: true);
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

        // Path pen yang sudah digambar ikut berubah live saat tebalnya digeser.
        if (sender is Slider sl && sl.Name == "SldPenThickness")
            RenderOverlay();
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
        if (this.FindControl<Slider>("SldPenThickness") is { } s10)
            _settings.EditorPenPathThickness = PenPathStyle.ClampThickness(s10.Value);
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
        SetText("TxtPenThickness", $"{PenPathStyle.ClampThickness(_settings.EditorPenPathThickness):0} px");

        UpdateBrushModeButton();
    }

    // ========================
    // PEN PATH STYLE (tebal & warna garis)
    // ========================

    /// <summary>Ketebalan garis path pen (px), selalu dalam rentang 1..8.</summary>
    private double PenPathThickness() => PenPathStyle.ClampThickness(_settings.EditorPenPathThickness);

    /// <summary>Warna garis path pen (tidak pernah throw).</summary>
    private Color PenPathColor() => PenPathStyle.ParseColor(_settings.EditorPenPathColor);


}

// ============================================================
// PreferencesWindow — dialog Preferensi modal ala Photoshop
// (Ctrl+K / ikon gear). Kategori di kiri, konten di kanan,
// tombol OK / Batal di bawah. Semua string terlokalisasi
// lewat dictionary App-level (DynamicResource).
//
// Semantik: OK = validasi + simpan semua + tutup.
// Batal/Esc = tutup TANPA menyimpan; perubahan yang sudah
// diterapkan live (bahasa, warna pen, beta, background)
// dikembalikan ke snapshot saat dialog dibuka.
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PixelcutCompact.Services;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Views;

public partial class PreferencesWindow : Window
{
    private readonly PreviewWindow _owner;
    private readonly PreviewWindowSettings _settings;
    private string Tr(string key, params object[] args) => _owner.Tr(key, args);

    // Snapshot untuk Batal.
    private readonly string _snapLang;
    private readonly string _snapPenColor;
    private readonly double _snapPenThickness;
    private readonly bool _snapBeta;
    private readonly int _snapBgType;
    private readonly string _snapSolid;
    private readonly string _snapChecker1;
    private readonly string _snapChecker2;

    private bool _syncingLanguage;
    private readonly List<Button> _penSwatches = new();
    private bool _uiReady;

    /// <summary>Hanya untuk XAML loader; selalu pakai <see cref="PreferencesWindow(PreviewWindow)"/>.</summary>
    public PreferencesWindow() : this(null!) { }

    public PreferencesWindow(PreviewWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _settings = owner.Settings;

        _snapLang = _settings.EditorLanguage;
        _snapPenColor = _settings.EditorPenPathColor;
        _snapPenThickness = _settings.EditorPenPathThickness;
        _snapBeta = _settings.EditorBetaMode;
        _snapBgType = _settings.BackgroundType;
        _snapSolid = _settings.SolidColorHex;
        _snapChecker1 = _settings.CheckerColor1;
        _snapChecker2 = _settings.CheckerColor2;

        InitializeComponent();
        _uiReady = true;
        // Tangkap Esc bahkan bila kontrol fokus (mis. ComboBox) sudah menandainya handled.
        this.AddHandler(InputElement.KeyDownEvent, OnDialogKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        InitControls();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnCancelClick(sender, e);
            e.Handled = true;
        }
    }

    // ========================
    // INISIALISASI
    // ========================

    private void InitControls()
    {
        var txtNext = this.FindControl<TextBox>("TxtNextShortcut");
        var txtPrev = this.FindControl<TextBox>("TxtPrevShortcut");
        var txtPhotoshop = this.FindControl<TextBox>("TxtPhotoshopShortcut");
        var txtRotate = this.FindControl<TextBox>("TxtRotateShortcut");
        var txtFitScreen = this.FindControl<TextBox>("TxtFitScreenShortcut");

        if (txtNext != null) txtNext.Text = _settings.ShortcutNext;
        if (txtPrev != null) txtPrev.Text = _settings.ShortcutPrevious;
        if (txtPhotoshop != null) txtPhotoshop.Text = _settings.ShortcutPhotoshop;
        if (txtRotate != null) txtRotate.Text = _settings.ShortcutRotate;
        if (txtFitScreen != null) txtFitScreen.Text = _settings.ShortcutFitScreen;
        foreach (var definition in EditorShortcutMap.Definitions)
        {
            var field = this.FindControl<TextBox>(definition.ControlName);
            if (field != null)
                field.Text = EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, definition.Action);
        }

        var betaToggle = this.FindControl<CheckBox>("ChkEditorBetaMode");
        if (betaToggle != null) betaToggle.IsChecked = _settings.EditorBetaMode;

        _syncingLanguage = true;
        try
        {
            var cboLang = this.FindControl<ComboBox>("CboEditorLanguage");
            if (cboLang != null) cboLang.SelectedIndex = _settings.EditorLanguage == "en" ? 1 : 0;
        }
        finally { _syncingLanguage = false; }

        WirePenColorUi();

        var cboBgType = this.FindControl<ComboBox>("CboBgType");
        if (cboBgType != null) cboBgType.SelectedIndex = _settings.BackgroundType;

        UpdateColorPreviewIndicators();
        UpdateBgVisibility();
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Abaikan event yang fire saat XAML populate (namescope belum siap).
        if (!_uiReady) return;
        var pages = new[]
        {
            this.FindControl<StackPanel>("PageGeneral"),
            this.FindControl<StackPanel>("PageLanguage"),
            this.FindControl<StackPanel>("PagePen"),
            this.FindControl<StackPanel>("PageShortcuts"),
        };
        var list = this.FindControl<ListBox>("LstCategories");
        int sel = list?.SelectedIndex ?? 0;
        for (int i = 0; i < pages.Length; i++)
            if (pages[i] != null) pages[i]!.IsVisible = sel == i;
    }

    // ========================
    // OK / BATAL
    // ========================

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var txtNext = this.FindControl<TextBox>("TxtNextShortcut");
        var txtPrev = this.FindControl<TextBox>("TxtPrevShortcut");
        var txtPhotoshop = this.FindControl<TextBox>("TxtPhotoshopShortcut");
        var txtRotate = this.FindControl<TextBox>("TxtRotateShortcut");
        var txtFitScreen = this.FindControl<TextBox>("TxtFitScreenShortcut");

        string ReadShortcut(TextBox? box, string current)
            => string.IsNullOrWhiteSpace(box?.Text) ? current : box.Text.Trim();
        var shortcuts = new[]
        {
            ReadShortcut(txtNext, _settings.ShortcutNext),
            ReadShortcut(txtPrev, _settings.ShortcutPrevious),
            ReadShortcut(txtPhotoshop, _settings.ShortcutPhotoshop),
            ReadShortcut(txtRotate, _settings.ShortcutRotate),
            ReadShortcut(txtFitScreen, _settings.ShortcutFitScreen)
        };
        if (shortcuts.Any(key => key.Equals("E", StringComparison.OrdinalIgnoreCase))
            || shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length)
        {
            _owner.ShowToast(Tr("Toast_ShortcutsUnique"), warning: true);
            return;
        }

        var editorShortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in EditorShortcutMap.Definitions)
        {
            var field = this.FindControl<TextBox>(definition.ControlName);
            editorShortcuts[definition.Action.ToString()] =
                ReadShortcut(field, EditorShortcutMap.GetShortcut(_settings.EditorShortcuts, definition.Action));
        }
        if (!EditorShortcutMap.TryValidate(editorShortcuts, out var editorShortcutError, k => Tr(k)))
        {
            _owner.ShowToast(editorShortcutError, warning: true);
            return;
        }

        _settings.ShortcutNext = shortcuts[0];
        _settings.ShortcutPrevious = shortcuts[1];
        _settings.ShortcutPhotoshop = shortcuts[2];
        _settings.ShortcutRotate = shortcuts[3];
        _settings.ShortcutFitScreen = shortcuts[4];
        _settings.EditorShortcuts = EditorShortcutMap.MergeWithDefaults(editorShortcuts);
        var betaToggle = this.FindControl<CheckBox>("ChkEditorBetaMode");
        if (betaToggle != null) _settings.EditorBetaMode = betaToggle.IsChecked == true;

        _settings.Save();
        _owner.RefreshEditorHints();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        // Kembalikan perubahan live ke snapshot (bahasa, warna pen, beta, background).
        _settings.EditorBetaMode = _snapBeta;
        _settings.BackgroundType = _snapBgType;
        _settings.SolidColorHex = _snapSolid;
        _settings.CheckerColor1 = _snapChecker1;
        _settings.CheckerColor2 = _snapChecker2;
        _settings.Save();
        _owner.ApplyBackground();
        _owner.SetPenPathColorPref(_snapPenColor);
        _owner.SetPenPathThicknessPref(_snapPenThickness);
        _owner.ApplyPreferencesLanguage(_snapLang);
        Close(false);
    }

    private void OnRestoreShortcutDefaultsClick(object? sender, RoutedEventArgs e)
    {
        _settings.RestoreKeyboardShortcutDefaults();
        _settings.Save();
        InitControls();
        _owner.RefreshEditorHints();
        _owner.ShowToast(Tr("Toast_ShortcutsRestored"));
    }

    // ========================
    // CAPTURE SHORTCUT
    // ========================

    private void OnShortcutTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        var editorDefinition = EditorShortcutMap.Definitions.FirstOrDefault(definition =>
            definition.ControlName.Equals(textBox.Name, StringComparison.Ordinal));

        // Keep capture local to this field; edit-mode bindings also support Shift+key.
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        if (editorDefinition != null)
        {
            // Delete/Backspace boleh di-bind ke aksi masking; Enter hanya ke Make selection.
            // Space + modifier (mis. Shift+Space) boleh; Space polos reserved untuk Pan.
            if (EditorShortcutMap.IsReservedKeyForAction(editorDefinition.Action, e.Key, e.KeyModifiers))
            {
                _owner.ShowToast(e.Key == Key.Space
                    ? Tr("Toast_SpaceReserved")
                    : Tr("Toast_KeyReserved", EditorShortcutMap.FormatKey(e.Key)), warning: true);
                return;
            }
            if (!EditorShortcutMap.IsSupportedModifiers(e.KeyModifiers))
            {
                _owner.ShowToast(Tr("Toast_EditShortcutsNote"), warning: true);
                return;
            }

            textBox.Text = EditorShortcutMap.FormatShortcut(e.Key, e.KeyModifiers);
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None) return;
        textBox.Text = e.Key.ToString();
    }

    // ========================
    // UMUM: BETA + BAHASA
    // ========================

    private void OnEditorBetaModeToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk)
        {
            _settings.EditorBetaMode = chk.IsChecked == true;
            _settings.Save();
        }
    }

    private void OnEditorLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguage) return;
        if (sender is ComboBox cbo && cbo.SelectedIndex >= 0)
            _owner.ApplyPreferencesLanguage(cbo.SelectedIndex == 1 ? "en" : "id");
    }

    // ========================
    // UMUM: BACKGROUND
    // ========================

    private void UpdateBgVisibility()
    {
        var type = _settings.BackgroundType;
        var pnlSolid = this.FindControl<StackPanel>("PanelSolidColor");
        var pnlChecker = this.FindControl<StackPanel>("PanelCheckerColor");

        if (pnlSolid != null) pnlSolid.IsVisible = type == 2;
        if (pnlChecker != null) pnlChecker.IsVisible = type == 1;
    }

    private void OnBgTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cbo && cbo.SelectedIndex >= 0)
        {
            _settings.BackgroundType = cbo.SelectedIndex;
            UpdateBgVisibility();
            _owner.ApplyBackground();
            _settings.Save();
        }
    }

    private void UpdateColorPreviewIndicators()
    {
        // Solid color indicator
        var brdCurrent = this.FindControl<Border>("BrdCurrentColor");
        var txtHex = this.FindControl<TextBlock>("TxtCurrentColorHex");
        try
        {
            var col = Color.Parse(_settings.SolidColorHex);
            if (brdCurrent != null) brdCurrent.Background = new SolidColorBrush(col);
            if (txtHex != null) txtHex.Text = _settings.SolidColorHex.ToUpperInvariant();
        }
        catch { }

        // Checker indicators
        var brdC1 = this.FindControl<Border>("BrdChecker1Preview");
        var brdC2 = this.FindControl<Border>("BrdChecker2Preview");
        var txtCk = this.FindControl<TextBlock>("TxtCheckerHex");
        try
        {
            var c1 = Color.Parse(_settings.CheckerColor1);
            var c2 = Color.Parse(_settings.CheckerColor2);
            if (brdC1 != null) brdC1.Background = new SolidColorBrush(c1);
            if (brdC2 != null) brdC2.Background = new SolidColorBrush(c2);
            if (txtCk != null) txtCk.Text = $"{_settings.CheckerColor1.ToUpperInvariant()} / {_settings.CheckerColor2.ToUpperInvariant()}";
        }
        catch { }
    }

    private void OnColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Name != null && btn.Name.StartsWith("SwColor_"))
        {
            var hex = "#" + btn.Name.Substring(8); // e.g. SwColor_FFFFFF -> #FFFFFF
            _settings.SolidColorHex = hex;
            _owner.ApplyBackground();
            _settings.Save();
            UpdateColorPreviewIndicators();
        }
    }

    private void OnChecker1SwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Name != null && btn.Name.StartsWith("CkColor1_"))
        {
            var hex = "#" + btn.Name.Substring(9); // e.g. CkColor1_FFFFFF -> #FFFFFF
            _settings.CheckerColor1 = hex;
            _owner.ApplyBackground();
            _settings.Save();
            UpdateColorPreviewIndicators();
        }
    }

    private void OnChecker2SwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Name != null && btn.Name.StartsWith("CkColor2_"))
        {
            var hex = "#" + btn.Name.Substring(9);
            _settings.CheckerColor2 = hex;
            _owner.ApplyBackground();
            _settings.Save();
            UpdateColorPreviewIndicators();
        }
    }

    // ========================
    // PEN: WARNA GARIS PATH
    // ========================

    /// <summary>
    /// Pasang UI warna garis Pen. Idempoten: aman dipanggil ulang.
    /// </summary>
    private void WirePenColorUi()
    {
        BuildPenSwatches();
        WirePenThicknessUi();
        var btn = this.FindControl<Button>("BtnPenCustomColorSettings");
        if (btn != null)
        {
            btn.Click -= OnPenCustomColorClick;
            btn.Click += OnPenCustomColorClick;
        }
    }

    private void WirePenThicknessUi()
    {
        var sld = this.FindControl<Slider>("SldPenThickness");
        var txt = this.FindControl<TextBlock>("TxtPenThickness");
        if (sld == null) return;
        sld.Value = _settings.EditorPenPathThickness;
        if (txt != null) txt.Text = $"{_settings.EditorPenPathThickness:0} px";
        sld.ValueChanged -= OnPenThicknessChanged;
        sld.ValueChanged += OnPenThicknessChanged;
    }

    private void OnPenThicknessChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_uiReady) return;
        var v = Math.Round(e.NewValue);
        _owner.SetPenPathThicknessPref(v);
        var txt = this.FindControl<TextBlock>("TxtPenThickness");
        if (txt != null) txt.Text = $"{v:0} px";
    }

    /// <summary>Bangun 8 swatch warna preset sekali saja (dipanggil dari WirePenColorUi).</summary>
    private void BuildPenSwatches()
    {
        var row = this.FindControl<StackPanel>("PenSwatchRowSettings");
        if (row == null || row.Children.Count > 0) return;
        foreach (var hex in PenPathStyle.PresetColors)
        {
            var b = new Button
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(PenPathStyle.ParseColor(hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#80FFFFFF")),
                BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(b, hex);
            string captured = hex;
            b.Click += (_, _) => SetPenPathColor(captured);
            row.Children.Add(b);
            _penSwatches.Add(b);
        }
        RefreshPenSwatchSelection();
    }

    /// <summary>Tandai swatch yang sedang aktif dengan ring biru aksen.</summary>
    private void RefreshPenSwatchSelection()
    {
        if (_penSwatches.Count == 0) return;
        string cur = PenPathStyle.NormalizeColor(_settings.EditorPenPathColor);
        foreach (var b in _penSwatches)
        {
            bool sel = string.Equals(ToolTip.GetTip(b) as string, cur, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = new SolidColorBrush(Color.Parse(sel ? "#31A8FF" : "#80FFFFFF"));
            b.BorderThickness = new Thickness(sel ? 2 : 1);
        }
    }

    private void SetPenPathColor(string hex)
    {
        _owner.SetPenPathColorPref(hex); // persist + render ulang path live
        RefreshPenSwatchSelection();
    }

    /// <summary>
    /// Dialog warna kustom: grid preset tambahan + input hex + preview.
    /// (ColorPicker bawaan tidak me-render di dialog ini, jadi dibuat manual —
    ///  deterministik dan selalu tampil.)
    /// </summary>
    private async void OnPenCustomColorClick(object? sender, RoutedEventArgs e)
    {
        string[] extra =
        {
            "#FF9F0A", "#FF6482", "#BF5AF2", "#7D7AFF",
            "#64D2FF", "#30B0C7", "#00C7BE", "#66BB6A",
            "#FFA726", "#AC8E68", "#8D6E63", "#B0BEC5",
            "#37474F", "#F48FB1", "#CE93D8", "#5E5CE6",
        };
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(12, 8, 12, 0) };
        var preview = new Border
        {
            Width = 40, Height = 26, CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.Parse("#80FFFFFF")), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(PenPathStyle.ParseColor(_settings.EditorPenPathColor)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hexBox = new TextBox
        {
            Watermark = "#RRGGBB", Text = PenPathStyle.NormalizeColor(_settings.EditorPenPathColor),
            Width = 110, VerticalAlignment = VerticalAlignment.Center,
        };
        string chosen = PenPathStyle.NormalizeColor(_settings.EditorPenPathColor);
        void Pick(string hex)
        {
            chosen = PenPathStyle.NormalizeColor(hex);
            preview.Background = new SolidColorBrush(PenPathStyle.ParseColor(chosen));
            if (hexBox.Text != chosen) hexBox.Text = chosen;
            foreach (var child in grid.Children)
                if (child is Button b)
                {
                    bool sel = string.Equals(ToolTip.GetTip(b) as string, chosen, StringComparison.OrdinalIgnoreCase);
                    b.BorderBrush = new SolidColorBrush(Color.Parse(sel ? "#31A8FF" : "#80FFFFFF"));
                    b.BorderThickness = new Thickness(sel ? 2 : 1);
                }
        }
        foreach (var hex in extra)
        {
            var b = new Button
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Padding = new Thickness(0),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(PenPathStyle.ParseColor(hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#80FFFFFF")), BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(b, hex);
            string captured = hex;
            b.Click += (_, _) => Pick(captured);
            grid.Children.Add(b);
        }
        hexBox.TextChanged += (_, _) =>
        {
            // Terapkan hanya bila input valid; abaikan saat user sedang mengetik.
            string t = (hexBox.Text ?? "").Trim();
            if (t.Length is 7 or 4 && t.StartsWith("#"))
            {
                try { Color.Parse(t); Pick(t); }
                catch { /* bukan warna valid */ }
            }
        };
        var hexRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 8, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        hexRow.Children.Add(new TextBlock { Text = "Hex:", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(preview);
        var btnOk = new Button
        {
            Content = Tr("Dlg_Choose"), MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#31A8FF")),
            Foreground = Brushes.White, CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4),
        };
        var btnCancel = new Button
        {
            Content = Tr("Btn_Cancel"), MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            Foreground = Brushes.White, CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4),
        };
        var dlg = new Window
        {
            Title = Tr("Dlg_PenColorTitle"),
            Width = 340, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
        };
        var btnGrid = new Grid
        {
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        btnCancel.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(btnCancel, 1);
        Grid.SetColumn(btnOk, 2);
        btnGrid.Children.Add(btnCancel);
        btnGrid.Children.Add(btnOk);
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = Tr("Dlg_ExtraColors"), Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")), FontSize = 11, Margin = new Thickness(12, 12, 12, 0) });
        panel.Children.Add(grid);
        panel.Children.Add(hexRow);
        panel.Children.Add(btnGrid);
        dlg.Content = panel;
        Pick(chosen);
        btnOk.Click += (_, _) => { SetPenPathColor(chosen); dlg.Close(); };
        btnCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }
}

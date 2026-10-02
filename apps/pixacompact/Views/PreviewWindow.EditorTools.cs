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

/// <summary>Pemilihan tool editor (dipisah agar file < batas push).</summary>
public partial class PreviewWindow
{
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
}

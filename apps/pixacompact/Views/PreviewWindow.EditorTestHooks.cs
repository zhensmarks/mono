using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace PixelcutCompact.Views;

/// <summary>
/// Hook TANPA UI untuk menguji workspace editor secara otomatis (harness headless).
/// Hanya memanggil method internal yang sudah ada; tidak mengubah perilaku produksi.
/// </summary>
public partial class PreviewWindow
{
    /// <summary>Masuk mode edit (tanpa butuh klik). True kalau berhasil.</summary>
    public bool EditorTestEnterEdit()
    {
        EnterEditMode();
        return _editMode;
    }

    /// <summary>Set visibilitas panel seperti lewat menu Window, lalu terapkan.</summary>
    public void EditorTestSetPanelVisible(string key, bool visible)
    {
        _settings.EditorPanelVisibility[key] = visible;
        SyncPanelMenuChecks();
        ApplyEditorDockVisibility();
    }

    /// <summary>Pindahkan panel ke sisi dock tertentu, lalu terapkan.</summary>
    public void EditorTestSetDock(string setting, string position)
    {
        switch (setting)
        {
            case "Tools": _settings.EditorToolsDock = NormalizeToolDock(position); break;
            case "Properties": _settings.EditorPropertiesDock = NormalizeDock(position); break;
            case "History": _settings.EditorHistoryDock = NormalizeDock(position); break;
            case "Layers": _settings.EditorLayersDock = NormalizeDock(position); break;
            default: return;
        }
        ArrangeDockedPanels();
        SyncDockCombos();
        ApplyEditorDockVisibility();
    }

    /// <summary>Laporan visibilitas tiap panel/host (untuk assertion teks).</summary>
    public string EditorTestVisibilityReport()
    {
        string V(string n) => (this.FindControl<Control>(n)?.IsVisible ?? false) ? "VISIBLE" : "hidden";
        string Host(string n) => (this.FindControl<StackPanel>(n)?.Children.Count ?? 0).ToString();
        return string.Join(" | ",
            $"ToolRail={V("PanelToolRail")}",
            $"OptionsBar={V("PanelOptionsBar")}",
            $"RightDock={V("PanelRightEditor")}",
            $"Props={V("PanelProperties")}",
            $"History={V("PanelHistory")}",
            $"Layers={V("PanelDocLayers")}",
            $"TopHost={V("DockTopHost")}",
            $"LeftHost={V("DockLeftHost")}",
            $"BottomHost={V("DockBottomHost")}",
            $"LayersInTop={Host("DockTopPanel")}",
            $"LayersInRight={Host("DockRightPanel")}",
            $"LayersInLeft={Host("DockLeftPanel")}",
            $"LayersInBottom={Host("DockBottomPanel")}");
    }

    /// <summary>True bila semua panel utama terlihat (dipakai untuk gate langkah tes).</summary>
    public bool EditorTestIsEditMode => _editMode;

    /// <summary>Set preferensi 2 kolom rail Tools lalu terapkan.</summary>
    public void EditorTestSetToolsTwoColumns(bool two)
    {
        _settings.EditorToolsPreferTwoColumns = two;
        if (this.FindControl<ToggleButton>("BtnToolsTwoColumns") is { } t) t.IsChecked = two;
        ConfigureToolOrientation(DockPositionOf("Tools"));
    }

    /// <summary>Uji docking: cabut panel jadi jendela melayang.</summary>
    public void EditorTestFloatPanel(string key) => FloatPanel(key);

    /// <summary>Uji docking: pasang panel ke sisi tertentu.</summary>
    public void EditorTestDockPanel(string key, string position) => DockPanelTo(key, position);

    /// <summary>Laporan status docking (melayang / posisi).</summary>
    public string EditorTestDockReport()
    {
        string Pos(string k) => SettingOf(k) switch
        {
            "Tools" => _settings.EditorToolsDock,
            "Properties" => _settings.EditorPropertiesDock,
            "History" => _settings.EditorHistoryDock,
            _ => _settings.EditorLayersDock
        };
        return $"floating=[{string.Join(",", _floatingPanels.Keys)}] "
             + $"Tools={Pos("toolrail")} Props={Pos("properties")} History={Pos("history")} Layers={Pos("layers")}";
    }

    /// <summary>Laporan tata letak rail Tools (lebar, jumlah kolom, isi tiap kolom).</summary>
    public string EditorTestToolRailReport()
    {
        double w = this.FindControl<Border>("PanelToolRail")?.Width ?? -1;
        var left = this.FindControl<StackPanel>("ToolColumnLeft");
        var right = this.FindControl<StackPanel>("ToolColumnRight");
        string pos(StackPanel? p)
        {
            if (p == null) return "n/a";
            var pt = p.TranslatePoint(new Avalonia.Point(0, 0), this);
            return pt == null ? "?" : $"X={pt.Value.X:0} Y={pt.Value.Y:0} W={p.Bounds.Width:0} H={p.Bounds.Height:0}";
        }
        return $"RailWidth={w:0} LeftGroups={left?.Children.Count ?? -1} RightGroups={right?.Children.Count ?? -1} RightVisible={(right?.IsVisible ?? false)} | Left[{pos(left)}] Right[{pos(right)}]";
    }

    /// <summary>Laporan isi panel Layers (jumlah item + nama) untuk assertion.</summary>
    public string EditorTestLayerReport()
    {
        var list = this.FindControl<ItemsControl>("LayerList");
        int n = 0;
        var names = new System.Collections.Generic.List<string>();
        if (list?.ItemsSource is System.Collections.IEnumerable en)
        {
            foreach (var it in en)
            {
                n++;
                var nm = it?.GetType().GetProperty("Name")?.GetValue(it)?.ToString();
                if (!string.IsNullOrEmpty(nm)) names.Add(nm!);
            }
        }
        return $"LayerCount={n} Names=[{string.Join(",", names)}]";
    }

    /// <summary>Isi mask jadi setengah (kiri opaque, kanan transparan) untuk menguji Refine Hair.</summary>
    public void EditorTestSeedHalfMask()
    {
        if (_session == null) return;
        int w = _session.Width, h = _session.Height;
        var m = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m[y * w + x] = x < w / 2 ? (byte)255 : (byte)0;
        _session.ReplaceMask(m, "TestSeed");
    }

    /// <summary>Zoom kanvas ke area gambar tertentu (untuk memotret detail tepi rambut).</summary>
    public void EditorTestZoomToImageRect(double x, double y, double w, double h)
    {
        if (_session == null) return;
        var canvas = this.FindControl<Canvas>("EditOverlay");
        double vw = canvas?.Bounds.Width ?? 0, vh = canvas?.Bounds.Height ?? 0;
        if (vw <= 10 || vh <= 10) { vw = 1000; vh = 820; }
        _viewPort.ViewWidth = vw; _viewPort.ViewHeight = vh;
        _viewPort.ImageWidth = _session.Width; _viewPort.ImageHeight = _session.Height;
        double z = Math.Min(vw / w, vh / h) * 0.92;
        if (z < 0.2) z = 0.2; if (z > 20) z = 20;
        _viewPort.Zoom = z;
        double cx = x + w / 2.0, cy = y + h / 2.0;
        _viewPort.PanX = -cx * z;
        _viewPort.PanY = -cy * z;
        ApplyEditorViewTransform();
        OnViewTransformChanged();
    }

    /// <summary>Simpan komposit sesi sekarang ke PNG (untuk bukti visual).</summary>
    public void EditorTestSaveComposite(string path)
    {
        if (_session == null) return;
        try
        {
            var buf = _session.Composite();
            var wb = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(buf.Width, buf.Height),
                new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = wb.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(buf.Bgra, 0, fb.Address, buf.Bgra.Length);
            }
            wb.Save(path);
        }
        catch (Exception ex) { Console.WriteLine("SAVE-COMPOSITE-ERR " + ex.Message); }
    }

    /// <summary>Jalankan Refine Hair (AI) dan laporkan hasil nyata untuk verifikasi.</summary>
    public async Task<string> EditorTestRunRefineHair()
    {
        if (_session == null) return "NO-SESSION";
        var spec = PixelcutCompact.Services.Ai.OnnxModelManager.Find(_settings.EditorRefineHairModel)
                   ?? PixelcutCompact.Services.Ai.OnnxModelManager.ModNet;
        var svc = new PixelcutCompact.Services.Ai.RefineHairService();
        try
        {
            var before = (byte[])_session.Mask.Clone();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await svc.RefineAsync(_session, spec, _settings.EditorRefineHairBandRadius,
                _settings.EditorRefineHairFeather, null, System.Threading.CancellationToken.None);
            sw.Stop();
            int changed = 0;
            for (int i = 0; i < before.Length && i < r.Mask.Length; i++) if (before[i] != r.Mask[i]) changed++;
            if (!r.NoChange) _session.ReplaceMask(r.Mask, "Refine Hair");
            return $"provider={r.ExecutionProvider} noChange={r.NoChange} changedPx={changed} elapsedMs={sw.ElapsedMilliseconds} bbox=({r.MinX},{r.MinY},{r.MaxX},{r.MaxY})";
        }
        catch (Exception ex) { return "REFINE-ERROR: " + ex.GetType().Name + ": " + ex.Message; }
        finally { svc.Dispose(); }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using PixelcutCompact.Services.Editing;

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

    /// <summary>Uji docking: pasang panel menumpuk sebelum/sesudah panel lain.</summary>
    public void EditorTestDockPanelStacked(string key, string anchor, bool after)
        => DockPanelTo(key, DockPositionOf(SettingOf(anchor)), anchor, after);

    /// <summary>Uji drop-zone: hitung target pada koordinat workspace, lalu pasang panel di sana.</summary>
    public string EditorTestDropAt(string key, double x, double y)
    {
        if (this.FindControl<Grid>("EditorWorkspaceGrid") is not { } ws) return "no-workspace";
        var (position, anchor, after) = ComputeDropTarget(ws, new Point(x, y));
        DockPanelTo(key, position, anchor, after);
        return $"pos={position} anchor={anchor ?? "-"} after={after}";
    }

    /// <summary>Uji drop-zone: laporan target yang dihitung pada koordinat tertentu (tanpa memasang).</summary>
    public string EditorTestDropTargetAt(double x, double y)
    {
        if (this.FindControl<Grid>("EditorWorkspaceGrid") is not { } ws) return "no-workspace";
        var (position, anchor, after) = ComputeDropTarget(ws, new Point(x, y));
        return $"pos={position} anchor={anchor ?? "-"} after={after} ws={ws.Bounds.Width:0}x{ws.Bounds.Height:0}";
    }

    /// <summary>Laporan urutan panel di tiap host (nama kontrol, dipisah koma).</summary>
    public string EditorTestHostOrderReport()
    {
        string Names(string host)
        {
            var sp = this.FindControl<StackPanel>(host);
            if (sp == null) return "-";
            var names = sp.Children.OfType<Control>().Select(c => c.Name ?? "?").ToList();
            return names.Count == 0 ? "-" : string.Join(",", names);
        }
        return $"Top[{Names("DockTopPanel")}] Left[{Names("DockLeftPanel")}] "
             + $"Right[{Names("DockRightPanel")}] Bottom[{Names("DockBottomPanel")}] "
             + $"order=[{string.Join(",", EffectiveDockOrder())}]";
    }

    /// <summary>Laporan isi jendela melayang (judul + ada/tidaknya konten panel).</summary>
    public string EditorTestFloatingReport()
    {
        if (_floatingPanels.Count == 0) return "floating=none";
        var parts = new List<string>();
        foreach (var (key, win) in _floatingPanels)
        {
            var host = win.Content as DockPanel;
            var body = host?.Children.OfType<Border>()
                .Select(b => b.Child as Control)
                .FirstOrDefault(c => c != null && DockedPanels.Any(d => d.Name == c.Name));
            parts.Add($"{key}:title={win.Title}:body={(body?.Name ?? "NULL")}");
        }
        return "floating=" + string.Join(" ", parts);
    }

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

    /// <summary>Uji background: laporkan jenis brush BrdOriginal vs BrdResult (harus sama).</summary>
    public string EditorTestBackgroundReport()
    {
        string Desc(Border? b)
        {
            if (b?.Background is Avalonia.Media.IImageBrush img)
                return $"ImageBrush({img.Source?.GetType().Name ?? "?"})";
            if (b?.Background is Avalonia.Media.ISolidColorBrush sc)
                return $"Solid({sc.Color})";
            return b?.Background?.GetType().Name ?? "null";
        }
        var o = this.FindControl<Border>("BrdOriginal");
        var r = this.FindControl<Border>("BrdResult");
        bool same = ReferenceEquals(o?.Background, r?.Background);
        return $"type={_settings.BackgroundType} same={same} orig={Desc(o)} result={Desc(r)} "
             + $"solid={_settings.SolidColorHex} c1={_settings.CheckerColor1} c2={_settings.CheckerColor2}";
    }

    /// <summary>Uji background: ubah setting lalu terapkan (meniru alur Preferences).</summary>
    public void EditorTestSetBackground(int type, string solid = "#00FF00", string c1 = "#333333", string c2 = "#4D4D4D")
    {
        _settings.BackgroundType = type;
        _settings.SolidColorHex = solid;
        _settings.CheckerColor1 = c1;
        _settings.CheckerColor2 = c2;
        ApplyBackground();
    }

    /// <summary>Uji background: panggil jalur background mode edit (harus sama dgn preview).</summary>
    public void EditorTestReapplyEditBackground() => ApplyCheckerboardBackground();

    /// <summary>Uji waktu save: ukur composite + encode PNG + tulis disk untuk gambar sesi.</summary>
    public string EditorTestTimeSave(int runs = 3)
    {
        if (_session == null) return "no-session";
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var buf = _session.Composite();
            long tComposite = sw.ElapsedMilliseconds;
            var png = buf.ToPngBytes();
            long tPng = sw.ElapsedMilliseconds;
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pxasave_bench_{i}.png");
            System.IO.File.WriteAllBytes(tmp, png);
            long tWrite = sw.ElapsedMilliseconds;
            try { System.IO.File.Delete(tmp); } catch { }
            parts.Add($"[run{i} composite={tComposite} pngEncode={tPng - tComposite} write={tWrite - tPng} total={tWrite} bytes={png.Length}]");
        }
        return $"px={_session.Width}x{_session.Height} " + string.Join(" ", parts);
    }

    /// <summary>Uji kecepatan baca: decode + FromBitmap untuk hasil & asli (ms).</summary>
    public string EditorTestBenchLoad(int runs = 3)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long tResultDecode = 0, tResultBuffer = 0, tOrigDecode = 0, tOrigBuffer = 0;
            try
            {
                using (var b = new Avalonia.Media.Imaging.Bitmap(_resultPath))
                {
                    tResultDecode = sw.ElapsedMilliseconds;
                    _ = PixelBuffer.FromBitmap(b);
                    tResultBuffer = sw.ElapsedMilliseconds;
                }
            }
            catch { }
            try
            {
                using (var b = new Avalonia.Media.Imaging.Bitmap(_originalPath))
                {
                    tOrigDecode = sw.ElapsedMilliseconds;
                    _ = PixelBuffer.FromBitmap(b);
                    tOrigBuffer = sw.ElapsedMilliseconds;
                }
            }
            catch { }
            parts.Add($"[run{i} resDecode={tResultDecode} resBuf={tResultBuffer - tResultDecode} " +
                      $"origDecode={tOrigDecode - tResultBuffer} origBuf={tOrigBuffer - tOrigDecode} total={tOrigBuffer}]");
        }
        return string.Join(" ", parts);
    }

    /// <summary>Uji round-trip PNG Skia: encode buffer, decode ulang, cek warna piksel.</summary>
    public string EditorTestPngRoundTrip()
    {
        if (_session == null) return "no-session";
        var buf = _session.Composite();
        var png = buf.ToPngBytes();
        using var ms = new System.IO.MemoryStream(png);
        using var decoded = new Avalonia.Media.Imaging.Bitmap(ms);
        var back = PixelBuffer.FromBitmap(decoded);
        // Ambil sampel piksel tengah.
        int cx = buf.Width / 2, cy = buf.Height / 2;
        int i = (cy * buf.Width + cx) * 4;
        string S(byte[] a) => $"B{a[i]}G{a[i + 1]}R{a[i + 2]}A{a[i + 3]}";
        bool same = buf.Bgra[i] == back.Bgra[i] && buf.Bgra[i + 1] == back.Bgra[i + 1]
                 && buf.Bgra[i + 2] == back.Bgra[i + 2] && buf.Bgra[i + 3] == back.Bgra[i + 3];
        return $"bytes={png.Length} orig[{S(buf.Bgra)}] back[{S(back.Bgra)}] centerSame={same} dims={decoded.PixelSize.Width}x{decoded.PixelSize.Height}";
    }

    /// <summary>Uji chrome: laporan visibilitas footer navigasi & toast.</summary>
    public string EditorTestChromeReport()
    {
        bool F(string n) => this.FindControl<Control>(n)?.IsVisible ?? false;
        return $"footer={F("PanelFooter")} toast={F("ToastNotification")} editMode={_editMode}";
    }

    /// <summary>Uji warna/tebal path: set preferensi + gambar path 3 titik, lalu lapor.</summary>
    public string EditorTestDrawPenPath(string color, double thickness)
    {
        SetPenPathColorPref(color);
        SetPenPathThicknessPref(thickness);
        SetActiveTool(EditToolKind.Pen);
        if (_activeSelectionTool is PenTool pen)
        {
            pen.Reset();
            pen.PointerDown(new Vec2(-300, -150)); pen.PointerUp(new Vec2(-300, -150));
            pen.PointerDown(new Vec2(0, 150));      pen.PointerUp(new Vec2(0, 150));
            pen.PointerDown(new Vec2(300, -150));   pen.PointerUp(new Vec2(300, -150));
            RenderOverlay();
        }
        var overlay = this.FindControl<Canvas>("EditOverlay");
        var polys = overlay?.Children.OfType<Avalonia.Controls.Shapes.Polyline>().ToList() ?? new();
        var info = polys.Select(p => $"stroke={(p.Stroke as Avalonia.Media.ISolidColorBrush)?.Color} th={p.StrokeThickness} dash={p.StrokeDashArray?.Count ?? 0}");
        return $"color={PenPathStyle.NormalizeColor(color)} th={PenPathStyle.ClampThickness(thickness)} polys=[{string.Join("; ", info)}]";
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

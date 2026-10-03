using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using PixelcutCompact;
using PixelcutCompact.Services;
using PixelcutCompact.Views;

// Harness headless: jalankan editor PixaCompact SUNGGUHAN, ubah dock panel,
// lalu tangkap screenshot render nyata (Skia). Bukti bahwa docking bekerja.

class Program
{
    static string _outDir = "shots";
    static string _origJpg = "";
    static string _resultPng = "";

    [STAThread]
    static int Main(string[] args)
    {
        _outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pixasnap", "editor");
        Directory.CreateDirectory(_outDir);

        var bahan = @"D:\#DATA ABENG\#PROJECT\#BAHAN_UJI_COBA\#bahan_coba_seleksi";
        // Pasangan asli berambut (JPG + PNG cutout dengan alpha) untuk uji Refine Hair.
        var damkar = Path.Combine(bahan, "1. FOTO 10RP OB PROFESI MINIMALIS", "KELAS A1", "DAMKAR", "45 (1)");
        _origJpg = File.Exists(damkar + ".JPG") ? damkar + ".JPG" : (FindFirst(bahan, "*.JPG") ?? "");
        _resultPng = File.Exists(damkar + ".png") ? damkar + ".png" : (FindFirst(bahan, "*.png") ?? "");
        if (string.IsNullOrEmpty(_origJpg) || string.IsNullOrEmpty(_resultPng))
        {
            Console.WriteLine("BAHAN-NOT-FOUND orig=" + _origJpg + " result=" + _resultPng);
            return 3;
        }
        Console.WriteLine("ORIG=" + _origJpg);
        Console.WriteLine("RESULT=" + _resultPng);

        // Pastikan Mode Editor (Beta) aktif untuk window baru (dibaca dari preview_settings.json).
        var s = new PreviewWindowSettings { EditorBetaMode = true };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview_settings.json"),
            JsonSerializer.Serialize(s));

        int exit = 0;
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    try { await RunAsync(); }
                    catch (Exception ex) { Console.WriteLine("TEST-ERROR: " + ex); exit = 1; }
                    finally
                    {
                        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                    }
                });
            })
            .StartWithClassicDesktopLifetime(Array.Empty<string>());

        Console.WriteLine("DONE exit=" + exit);
        return exit;
    }

    static async Task RunAsync()
    {
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        var win = new PreviewWindow { Width = 1500, Height = 1000 };
        win.Show();
        Dispatcher.UIThread.RunJobs();

        win.LoadImages(_origJpg, _resultPng, "Uji Docking");

        // Tunggu sesi editor siap (EnterEditMode hanya jalan kalau _session siap).
        bool ready = await WaitUntil(() => { var ok = win.EditorTestEnterEdit(); if (!ok) return false; return true; }, 20000);
        Console.WriteLine("EDIT-READY=" + ready);
        if (!ready) { Shoot(win, "00_not_ready"); return; }

        await Settle();
        Console.WriteLine("STEP1 " + win.EditorTestVisibilityReport());
        Console.WriteLine("LAYERS " + win.EditorTestLayerReport());
        Console.WriteLine("BG-DEFAULT " + win.EditorTestBackgroundReport());
        Console.WriteLine("CHROME-EDIT " + win.EditorTestChromeReport());
        Console.WriteLine("LOAD-BENCH " + win.EditorTestBenchLoad(3));
        Console.WriteLine("PENPATH-BLUE " + win.EditorTestDrawPenPath("#0A84FF", 4));
        Shoot(win, "01d_penpath_blue_th4");
        Console.WriteLine("PENPATH-RED " + win.EditorTestDrawPenPath("#FF3B30", 8));
        Shoot(win, "01e_penpath_red_th8");
        Console.WriteLine("SAVE-BENCH " + win.EditorTestTimeSave(3));
        Console.WriteLine("PNG-ROUNDTRIP " + win.EditorTestPngRoundTrip());
        Shoot(win, "01_default_all_visible");

        // Background: set solid hijau (seperti via Preferences) lalu cek mode edit memakai brush sama.
        win.EditorTestSetBackground(2, "#00FF00");
        await Settle();
        Console.WriteLine("BG-SOLID " + win.EditorTestBackgroundReport());
        Shoot(win, "01b_bg_solid_green");
        // Kembalikan ke checkerboard custom (meniru user set checkerboard di preview).
        win.EditorTestSetBackground(1, "#00FF00", "#102030", "#405060");
        await Settle();
        Console.WriteLine("BG-CHECKER " + win.EditorTestBackgroundReport());
        Shoot(win, "01c_bg_checker_custom");
        // Masuk-ulang edit mode: background HARUS tetap checkerboard custom yang sama.
        win.EditorTestReapplyEditBackground();
        await Settle();
        Console.WriteLine("BG-AFTER-EDIT " + win.EditorTestBackgroundReport());

        // Panel Layers disembunyikan lewat menu Window.
        win.EditorTestSetPanelVisible("layers", false);
        await Settle();
        Console.WriteLine("STEP2 " + win.EditorTestVisibilityReport());
        Shoot(win, "02_layers_hidden");

        // Tampilkan lagi.
        win.EditorTestSetPanelVisible("layers", true);
        await Settle();

        // Pindahkan panel Layers ke ATAS.
        win.EditorTestSetDock("Layers", "Top");
        await Settle();
        Console.WriteLine("STEP3 " + win.EditorTestVisibilityReport());
        Shoot(win, "03_layers_docked_top");

        // Pindahkan panel Layers ke KIRI.
        win.EditorTestSetDock("Layers", "Left");
        await Settle();
        Console.WriteLine("STEP4 " + win.EditorTestVisibilityReport());
        Shoot(win, "04_layers_docked_left");

        // Kembalikan ke kanan + pindahkan Properties ke BAWAH.
        win.EditorTestSetDock("Layers", "Right");
        win.EditorTestSetDock("Properties", "Bottom");
        await Settle();
        Console.WriteLine("STEP5 " + win.EditorTestVisibilityReport());
        Shoot(win, "05_props_docked_bottom");

        // Reset layout + sembunyikan Properties (uji menu Window).
        win.EditorTestSetDock("Properties", "Right");
        win.EditorTestSetPanelVisible("properties", false);
        await Settle();
        Console.WriteLine("STEP6 " + win.EditorTestVisibilityReport());
        Shoot(win, "06_properties_hidden");

        // Panel Layers + rail disembunyikan (uji "Dock Kanan" & menu Window).
        win.EditorTestSetPanelVisible("properties", true);
        win.EditorTestSetDock("Tools", "Right");
        await Settle();
        Console.WriteLine("STEP7 " + win.EditorTestVisibilityReport());
        Shoot(win, "07_tools_docked_right");

        // Rail Tools 2 kolom.
        win.EditorTestSetDock("Tools", "Left");
        await Settle();
        Console.WriteLine("RAIL-1COL " + win.EditorTestToolRailReport());
        win.EditorTestSetToolsTwoColumns(true);
        await Settle();
        Console.WriteLine("RAIL-2COL " + win.EditorTestToolRailReport());
        Shoot(win, "08_tools_two_columns");

        // Custom tool: sembunyikan beberapa tool → rail menyesuaikan.
        Console.WriteLine("TOOLS-ALL " + win.EditorTestToolVisibilityReport());
        win.EditorTestSetToolVisible("Pen", false);
        win.EditorTestSetToolVisible("Lasso", false);
        win.EditorTestSetToolVisible("QuickMask", false);
        await Settle();
        Console.WriteLine("TOOLS-CUSTOM " + win.EditorTestToolVisibilityReport());
        Shoot(win, "08b_tools_custom_hidden");
        // 1 kolom eksplisit.
        win.EditorTestSetToolsColumns(false);
        await Settle();
        Console.WriteLine("TOOLS-1COL " + win.EditorTestToolVisibilityReport());
        Shoot(win, "08c_tools_1col");
        // Pulihkan.
        win.EditorTestSetToolVisible("Pen", true);
        win.EditorTestSetToolVisible("Lasso", true);
        win.EditorTestSetToolVisible("QuickMask", true);
        await Settle();

        // Docking: cabut panel Layers jadi jendela melayang, lalu pasang ke KIRI.
        win.EditorTestFloatPanel("layers");
        await Settle();
        Console.WriteLine("DOCK-FLOAT " + win.EditorTestDockReport());
        Console.WriteLine("FLOAT-REPORT " + win.EditorTestFloatingReport());
        Shoot(win, "11_layers_floating");
        win.EditorTestDockPanel("layers", "Left");
        await Settle();
        Console.WriteLine("DOCK-LEFT " + win.EditorTestDockReport());
        Shoot(win, "12_layers_docked_left");
        win.EditorTestDockPanel("layers", "Right");
        await Settle();
        Console.WriteLine("DOCK-RIGHT " + win.EditorTestDockReport());
        Shoot(win, "13_layers_docked_right");

        // Penumpukan: taruh Layers SEBELUM Properties (Layers di atas Properties, sisi kanan).
        Console.WriteLine("ORDER-BEFORE " + win.EditorTestHostOrderReport());
        win.EditorTestDockPanelStacked("layers", "Properties", after: false);
        await Settle();
        Console.WriteLine("STACK-BEFORE " + win.EditorTestHostOrderReport());
        Shoot(win, "14_layers_stacked_before_props");

        // Taruh Layers SESUDAH Properties (kebalikannya).
        win.EditorTestDockPanelStacked("layers", "Properties", after: true);
        await Settle();
        Console.WriteLine("STACK-AFTER " + win.EditorTestHostOrderReport());
        Shoot(win, "15_layers_stacked_after_props");

        // Uji drop-zone: target berdasarkan koordinat workspace (tepi kiri/atas + tengah).
        Console.WriteLine("DROP-LEFT " + win.EditorTestDropTargetAt(8, 400));
        Console.WriteLine("DROP-TOP " + win.EditorTestDropTargetAt(700, 8));
        Console.WriteLine("DROP-BOTTOM " + win.EditorTestDropTargetAt(700, 1500));
        Console.WriteLine("DROP-RIGHT " + win.EditorTestDropTargetAt(1490, 400));

        // Refine Hair AI pada cutout asli berambut: zoom ke kepala, jalankan, bandingkan.
        win.EditorTestZoomToImageRect(1300, 300, 1400, 1500);
        await Settle();
        Shoot(win, "09_before_refine");
        win.EditorTestSaveComposite(Path.Combine(_outDir, "09_before_refine_comp.png"));
        Console.WriteLine("REFINE-START");
        var refineReport = await win.EditorTestRunRefineHair();
        Console.WriteLine("REFINE-RESULT " + refineReport);
        await Settle();
        Shoot(win, "10_after_refine");
        win.EditorTestSaveComposite(Path.Combine(_outDir, "10_after_refine_comp.png"));
    }

    static async Task Settle()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(60);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(80);
        }
        return cond();
    }

    static void Shoot(PreviewWindow win, string name)
    {
        Dispatcher.UIThread.RunJobs();
        try { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } catch { }
        var frame = win.CaptureRenderedFrame();
        var path = Path.Combine(_outDir, name + ".png");
        if (frame != null) { frame.Save(path); Console.WriteLine("SHOT " + path + " " + frame.PixelSize); }
        else Console.WriteLine("SHOT-NULL " + name);
    }

    static string? FindFirst(string dir, string pattern)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                return f;
        }
        catch { }
        return null;
    }
}

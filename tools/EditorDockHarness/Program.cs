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
        _origJpg = FindFirst(bahan, "*.JPG") ?? FindFirst(bahan, "*.jpg");
        _resultPng = FindFirst(bahan, "*.png");
        if (_origJpg == null || _resultPng == null)
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
        Shoot(win, "01_default_all_visible");

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

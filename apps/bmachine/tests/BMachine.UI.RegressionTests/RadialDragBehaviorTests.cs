using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Perilaku drag slot radial di roda pengaturan, dijalankan sungguhan lewat SettingsViewModel.
///
/// Keluhan user: "kalau item dari kiri ke kanan harusnya seperti switch item saja, jangan aneh
/// jadi kaya merubah posisi lain". Tes memastikan: (1) dua item BERTUKAR posisi, (2) item lain
/// tidak bergeser sama sekali, (3) batal drag mengembalikan urutan semula, (4) hasil tukar
/// tersimpan ke scripts.json di AppData (bukan folder instalasi).
///
/// AppData diarahkan ke folder sementara lewat BMACHINE_APPDATA_ROOT supaya data user tidak
/// tersentuh; skrip roda dibaca dari BaseDirectory\Scripts\Action sehingga tes menulis file uji
/// sementara di sana lalu menghapusnya. Semua langkah ada di SATU tes supaya tidak ada dua tes
/// yang saling menimpa folder/env (LoadAllScriptsAsync juga memakai dispatcher).
/// </summary>
public class RadialDragBehaviorTests
{
    private static readonly string[] SeedFiles =
        { "zz_radialtest_a.jsx", "zz_radialtest_b.jsx", "zz_radialtest_c.jsx", "zz_radialtest_d.jsx" };

    private static string ScriptsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Action");

    private static string Names(SettingsViewModel vm)
        => string.Join(",", vm.RadialSlots.Select(s => s.Script?.OriginalName ?? s.NavigationLabel ?? "-"));

    [AvaloniaFact]
    public async Task DragSwapsOnlyTheTwoItems()
    {
        string previous = Environment.GetEnvironmentVariable("BMACHINE_APPDATA_ROOT");
        string root = Path.Combine(Path.GetTempPath(), "bm-radial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "BMachine"));
        Directory.CreateDirectory(ScriptsDir);
        foreach (var f in SeedFiles)
            File.WriteAllText(Path.Combine(ScriptsDir, f), "// file uji regresi roda radial");
        Environment.SetEnvironmentVariable("BMACHINE_APPDATA_ROOT", root);

        try
        {
            var vm = new SettingsViewModel(new FakeDatabase(), () => { }, null, new FakeNotificationService());
            await vm.LoadAllScriptsAsync();
            Dispatcher.UIThread.RunJobs();
            vm.SwitchToScriptAction();

            var wheel = vm.RadialSlots.Where(s => !s.IsNavigationReserved && s.Script != null).ToList();
            Assert.True(wheel.Count >= 3, $"roda hanya punya {wheel.Count} item, tes butuh >= 3");

            var source = wheel[0];
            var target = wheel[1];
            int sourceIndex = vm.RadialSlots.IndexOf(source);
            int targetIndex = vm.RadialSlots.IndexOf(target);
            string sourceName = source.Script!.OriginalName;
            string targetName = target.Script!.OriginalName;
            var before = vm.RadialSlots.Select(s => s.Script?.OriginalName).ToList();

            // --- drop di slot yang sama = tidak berubah
            var unchanged = Names(vm);
            vm.BeginRadialPreview();
            vm.PreviewRadialSwap(source, source);
            vm.CancelRadialPreview();
            Assert.Equal(unchanged, Names(vm));

            // --- batal setelah menyentuh item lain = balik ke urutan semula
            vm.BeginRadialPreview();
            vm.PreviewRadialSwap(source, target);
            vm.CancelRadialPreview();
            Assert.Equal(unchanged, Names(vm));

            // --- drop sungguhan: TUKAR posisi
            vm.BeginRadialPreview();
            vm.PreviewRadialSwap(source, target);
            vm.CommitRadialPreview();

            var after = vm.RadialSlots.Select(s => s.Script?.OriginalName).ToList();
            Assert.Equal(targetName, after[sourceIndex]);
            Assert.Equal(sourceName, after[targetIndex]);

            for (int i = 0; i < before.Count; i++)
            {
                if (i == sourceIndex || i == targetIndex) continue;
                Assert.Equal(before[i], after[i]);
            }

            // --- tersimpan ke scripts.json di AppData
            var saved = Path.Combine(root, "BMachine", "scripts.json");
            Assert.True(File.Exists(saved), "scripts.json tidak ditulis ke AppData");
            var json = File.ReadAllText(saved);
            Assert.Contains(sourceName, json);
            Assert.Contains(targetName, json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BMACHINE_APPDATA_ROOT", previous);
            foreach (var f in SeedFiles)
            {
                try { File.Delete(Path.Combine(ScriptsDir, f)); } catch { }
            }
            try { Directory.Delete(root, true); } catch { }
        }
    }
}

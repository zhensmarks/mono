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
/// jadi kaya merubah posisi lain" dan "bukan jadi mengurut lagi aneh". Tes memastikan:
/// (1) memulai drag TIDAK mengubah susunan roda, (2) dua item bertukar posisi, (3) item lain tetap,
/// (4) item di luar roda (halaman lain) mempertahankan urutan relatifnya, (5) batal mengembalikan
/// urutan semula, (6) hasil tersimpan ke scripts.json di AppData.
///
/// AppData diisolasi lewat BMACHINE_APPDATA_ROOT; skrip roda dibaca dari BaseDirectory\Scripts\Action.
/// </summary>
public class RadialDragBehaviorTests
{
    private static readonly string[] SeedFiles =
        { "zz_radialtest_a.jsx", "zz_radialtest_b.jsx", "zz_radialtest_c.jsx", "zz_radialtest_d.jsx" };

    private static string ScriptsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Action");

    private static string Names(SettingsViewModel vm)
        => string.Join(",", vm.RadialSlots.Select(s => s.Script?.OriginalName ?? s.NavigationLabel ?? "-"));

    private static void WriteMetadata(string root)
    {
        // Urutan sengaja diberi celah; dua alias di luar roda (prefix "_" tidak masuk ActionScripts)
        // diberi Order TERBALIK dari urutan namanya, supaya tes bisa membuktikan urutan relatif
        // mereka dipertahankan saat diparkir.
        var json = @"{
  ""zz_radialtest_a.jsx"": { ""Name"": ""A"", ""Code"": ""A"", ""IconKey"": """", ""Order"": 0, ""Enabled"": true },
  ""zz_radialtest_b.jsx"": { ""Name"": ""B"", ""Code"": ""B"", ""IconKey"": """", ""Order"": 1, ""Enabled"": true },
  ""zz_radialtest_c.jsx"": { ""Name"": ""C"", ""Code"": ""C"", ""IconKey"": """", ""Order"": 2, ""Enabled"": true },
  ""zz_radialtest_d.jsx"": { ""Name"": ""D"", ""Code"": ""D"", ""IconKey"": """", ""Order"": 3, ""Enabled"": true },
  ""_zz_p2.py"": { ""Name"": ""P2"", ""Code"": ""P2"", ""IconKey"": """", ""Order"": 3, ""Enabled"": true },
  ""_zz_p3.py"": { ""Name"": ""P3"", ""Code"": ""P3"", ""IconKey"": """", ""Order"": 1, ""Enabled"": true }
}";
        File.WriteAllText(Path.Combine(root, "BMachine", "scripts.json"), json);
    }

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
        WriteMetadata(root);
        Environment.SetEnvironmentVariable("BMACHINE_APPDATA_ROOT", root);

        try
        {
            var vm = new SettingsViewModel(new FakeDatabase(), () => { }, null, new FakeNotificationService());
            await vm.LoadAllScriptsAsync();
            Dispatcher.UIThread.RunJobs();
            vm.SwitchToScriptAction();

            var wheel = vm.RadialSlots.Where(s => !s.IsNavigationReserved && s.Script != null).ToList();
            Assert.True(wheel.Count >= 3, $"roda hanya punya {wheel.Count} item, tes butuh >= 3");

            var before = Names(vm);
            var beforeOrders = vm.RadialSlots.Select(s => s.Script?.OriginalName).ToList();

            // (1) memulai drag tidak boleh mengubah susunan roda
            vm.BeginRadialPreview();
            Assert.Equal(before, Names(vm));

            var source = wheel[0];
            var target = wheel[1];
            int sourceIndex = vm.RadialSlots.IndexOf(source);
            int targetIndex = vm.RadialSlots.IndexOf(target);
            string sourceName = source.Script!.OriginalName;
            string targetName = target.Script!.OriginalName;

            // (5) batal mengembalikan susunan semula
            vm.PreviewRadialSwap(source, target);
            vm.CancelRadialPreview();
            Assert.Equal(before, Names(vm));

            // (2) drop sungguhan: TUKAR posisi
            vm.BeginRadialPreview();
            Assert.Equal(before, Names(vm));
            vm.PreviewRadialSwap(source, target);
            vm.CommitRadialPreview();

            var after = vm.RadialSlots.Select(s => s.Script?.OriginalName).ToList();
            Assert.Equal(targetName, after[sourceIndex]);
            Assert.Equal(sourceName, after[targetIndex]);

            // (3) item lain tetap di tempat
            for (int i = 0; i < beforeOrders.Count; i++)
            {
                if (i == sourceIndex || i == targetIndex) continue;
                Assert.Equal(beforeOrders[i], after[i]);
            }

            // (6) tersimpan ke scripts.json di AppData
            var saved = Path.Combine(root, "BMachine", "scripts.json");
            Assert.True(File.Exists(saved), "scripts.json tidak ditulis ke AppData");
            var json = File.ReadAllText(saved);
            Assert.Contains(sourceName, json);
            Assert.Contains(targetName, json);

            // (4) item di luar roda tetap berurutan: _zz_p3 (Order 1) harus tetap sebelum _zz_p2 (Order 3)
            int OrderOf(string key)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                Assert.True(doc.RootElement.TryGetProperty(key, out var entry), $"entri {key} tidak ada di scripts.json");
                return entry.GetProperty("Order").GetInt32();
            }

            Assert.True(OrderOf("_zz_p3.py") < OrderOf("_zz_p2.py"),
                "urutan relatif item di luar roda berubah (item halaman lain teracak)");
            Assert.True(OrderOf("_zz_p3.py") >= 10000, "item di luar roda seharusnya diparkir >= 10000");
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

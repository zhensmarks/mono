using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BMachine.UI.Models;
using BMachine.UI.Services;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Update yang hanya berubah ISI file skrip (mis. satu .jsx) tidak boleh menutup aplikasi.
/// Restart hanya kalau exe/Assets berubah, atau ada file skrip baru/hilang.
/// </summary>
public class HotUpdateTests
{
    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static ManifestComponentInfo Comp(string name, bool atomic, params (string Rel, string Content)[] files) =>
        new()
        {
            Name = name,
            Changed = true,
            Included = true,
            Atomic = atomic,
            Paths = new List<string>(),
            Files = files.ToDictionary(f => f.Rel, f => Sha(f.Content), StringComparer.OrdinalIgnoreCase)
        };

    private static string Write(string root, string rel, string content)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, new UTF8Encoding(false));
        return p;
    }

    private static UpdateInfo Info(params ManifestComponentInfo[] comps) =>
        new() { Components = comps.ToList() };

    [Fact]
    public void ContentOnlyScriptChangeDoesNotNeedRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Scripts/Action/save_master.jsx", "// lama");

            var action = Comp("Action", false, ("Scripts/Action/save_master.jsx", "// baru"));
            var (need, reason) = LocalUpdateService.EvaluateRestart(Info(action), null, root);

            Assert.False(need, reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void NewScriptFileNeedsRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Scripts/Action/lama.jsx", "// lama");

            var action = Comp("Action", false,
                ("Scripts/Action/lama.jsx", "// lama"),
                ("Scripts/Action/baru.jsx", "// baru"));   // file baru -> tombol baru butuh start ulang
            var (need, reason) = LocalUpdateService.EvaluateRestart(Info(action), null, root);

            Assert.True(need, reason);
            Assert.Contains("baru", reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ChangedExeNeedsRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "BMachine.App.exe", "exe lama");

            var app = Comp("Aplikasi", false, ("BMachine.App.exe", "exe baru"));
            var (need, reason) = LocalUpdateService.EvaluateRestart(Info(app), null, root);

            Assert.True(need, reason);
            Assert.Contains("Program", reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void RemovedScriptFileNeedsRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Scripts/Action/masih.jsx", "// ada");
            Write(root, "Scripts/Action/dihapus.jsx", "// akan dihapus");

            var action = Comp("Action", false, ("Scripts/Action/masih.jsx", "// ada"));
            action.Paths = new List<string> { "Scripts/Action" };
            var (need, reason) = LocalUpdateService.EvaluateRestart(Info(action), null, root);

            Assert.True(need, reason);
            Assert.Contains("dihapus", reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task AppliedHotVersionStopsBannerForSameVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_hot_" + Guid.NewGuid().ToString("N"));
        var updateFolder = Path.Combine(root, "update");
        Directory.CreateDirectory(updateFolder);
        var marker = Path.Combine(root, "applied_hot_version.txt");
        var original = LocalUpdateService.AppliedHotVersionFile;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(updateFolder, "manifest.json"),
                "{\"version\":\"99.9.9\",\"notes\":[\"x\"],\"payload\":\"payload\"}");

            LocalUpdateService.AppliedHotVersionFile = marker;
            LocalUpdateService.SaveAppliedHotVersion("v99.9.9");

            var (info, status) = await LocalUpdateService.CheckAsync(updateFolder, allowFallback: false);

            Assert.False(info.IsUpdateAvailable);
            Assert.True(info.AlreadyAppliedWithoutRestart);
            Assert.Contains("tanpa restart", status);
        }
        finally
        {
            LocalUpdateService.AppliedHotVersionFile = original;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NoRestartScriptSkipsProcessWaitAndRelaunch()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm_update_" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);

        try
        {
            Write(payload, "Scripts/Action/a.jsx", "// versi baru");
            Write(install, "Scripts/Action/a.jsx", "// versi lama");

            var action = Comp("Action", false, ("Scripts/Action/a.jsx", "// versi baru"));
            var script = LocalUpdateService.BuildApplyScript(
                payload, install, Environment.ProcessId, new[] { "Action" },
                new List<ManifestComponentInfo> { action }, restartApp: false);

            Assert.DoesNotContain("Wait-Process", script);
            Assert.DoesNotContain("Start-Process", script);

            // Jalankan applier selagi proses test ini HIDUP: harus langsung selesai (tanpa menunggu 180s).
            var scriptPath = Path.Combine(root, "apply.ps1");
            File.WriteAllText(scriptPath, string.Join("\n", script.Split('\n')
                .Where(l => !l.Contains("Remove-Item -LiteralPath $MyInvocation"))), new UTF8Encoding(false));

            var sw = Stopwatch.StartNew();
            using (var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })!)
            {
                Assert.True(p.WaitForExit(60000), "applier tidak selesai");
            }
            sw.Stop();

            Assert.Equal("// versi baru", File.ReadAllText(Path.Combine(install, "Scripts/Action/a.jsx")));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"applier menunggu proses ({sw.Elapsed})");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
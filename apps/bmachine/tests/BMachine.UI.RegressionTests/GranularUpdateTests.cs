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
/// Update harus menyalin HANYA file yang berubah (mis. satu file .jsx), dengan pengecualian
/// Scripts\Master yang wajib satu kesatuan. Test ini menjalankan skrip applier SUNGGUHAN
/// (hasil BuildApplyScript) di folder sandbox, jadi bukan cuma cek teks.
/// </summary>
public class GranularUpdateTests
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
            Files = files.ToDictionary(f => f.Rel, f => Sha(f.Content), StringComparer.OrdinalIgnoreCase)
        };

    private static string WritePayloadFile(string payload, string rel, string content)
    {
        var path = Path.Combine(payload, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void WithoutExplicitSelectionEveryComponentStillUsesPerFileCopy()
    {
        var script = LocalUpdateService.BuildApplyScript(
            @"C:\payload", @"C:\install", 12345, null,
            new List<ManifestComponentInfo>
            {
                Comp("Aplikasi", false, ("BMachine.App.exe", "exe")),
                Comp("Master", true, ("Scripts/Master/m1.py", "# m1")),
                Comp("Action", false, ("Scripts/Action/x.jsx", "A"))
            });

        // Tanpa pilihan user, jangan jatuh ke mode "salin seluruh payload".
        Assert.Contains("$hasFiles = $false", script);
        Assert.Contains("$selNames", script);
        Assert.Contains("Scripts/Action/x.jsx", script);
        Assert.Contains("komponen", script);
    }

    [Fact]
    public void ExplicitSelectionSkipsOtherComponents()
    {
        var root = Path.Combine(Path.GetTempPath(), "bmachine-upd_" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);

        try
        {
            WritePayloadFile(payload, "Scripts/Action/x.jsx", "baru");
            WritePayloadFile(payload, "Scripts/Master/m1.py", "# master baru");
            WritePayloadFile(install, "Scripts/Action/x.jsx", "lama");
            WritePayloadFile(install, "Scripts/Master/m1.py", "# master lama");

            var action = Comp("Action", false, ("Scripts/Action/x.jsx", "baru"));
            var master = Comp("Master", true, ("Scripts/Master/m1.py", "# master baru"));

            var script = LocalUpdateService.BuildApplyScript(
                payload, install, 999999, new[] { "Master" },
                new List<ManifestComponentInfo> { action, master });

            script = string.Join("\n", script.Split('\n')
                .Where(l => !l.Contains("Start-Process -FilePath $exe") && !l.Contains("Remove-Item -LiteralPath $MyInvocation")));
            var scriptPath = Path.Combine(root, "apply2.ps1");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            using (var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })!)
            {
                Assert.True(p.WaitForExit(120000), "applier tidak selesai");
            }

            Assert.Equal("# master baru", File.ReadAllText(Path.Combine(install, "Scripts/Master/m1.py")));
            Assert.Equal("lama", File.ReadAllText(Path.Combine(install, "Scripts/Action/x.jsx")));  // tidak dipilih
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ApplyScriptContainsPerFileMachinery()
    {
        var script = LocalUpdateService.BuildApplyScript(
            @"C:\payload", @"C:\install", 12345, new[] { "Action" },
            new List<ManifestComponentInfo> { Comp("Action", false, ("Scripts/Action/x.jsx", "A")) });

        Assert.Contains("$hasFiles", script);
        Assert.Contains("function Copy-One", script);
        Assert.Contains("$comp.atomic", script);
        Assert.Contains("Get-InstalledHash", script);
        Assert.Contains("Scripts/Action/x.jsx", script);   // peta file ikut terkirim
    }

    [Fact]
    public void OnlyChangedFilesAreCopied_AndAtomicComponentCopiesWholeFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "bmachine-upd_" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);

        try
        {
            // --- Payload ---
            // Action (per-file): a.jsx sama dengan install, b.jsx berubah, c.jsx baru.
            WritePayloadFile(payload, "Scripts/Action/a.jsx", "// sama");
            WritePayloadFile(payload, "Scripts/Action/b.jsx", "// versi baru");
            WritePayloadFile(payload, "Scripts/Action/c.jsx", "// file baru");
            // Master (atomic): satu file berubah -> seluruh isi folder disalin.
            WritePayloadFile(payload, "Scripts/Master/m1.py", "# master 1");
            WritePayloadFile(payload, "Scripts/Master/m2.py", "# master 2 versi baru");

            var action = Comp("Action", false,
                ("Scripts/Action/a.jsx", "// sama"),
                ("Scripts/Action/b.jsx", "// versi baru"),
                ("Scripts/Action/c.jsx", "// file baru"));
            var master = Comp("Master", true,
                ("Scripts/Master/m1.py", "# master 1"),
                ("Scripts/Master/m2.py", "# master 2 versi baru"));

            // --- Kondisi install sebelum update ---
            var aPath = WritePayloadFile(install, "Scripts/Action/a.jsx", "// sama");
            WritePayloadFile(install, "Scripts/Action/b.jsx", "// versi lama");
            WritePayloadFile(install, "Scripts/Master/m1.py", "# master 1");       // sudah sama
            WritePayloadFile(install, "Scripts/Master/m2.py", "# master 2 lama");  // beda

            var aTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var m1Time = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(aPath, aTime);
            File.SetLastWriteTimeUtc(Path.Combine(install, "Scripts/Master/m1.py"), m1Time);

            // --- Jalankan applier sunngguhan ---
            var script = LocalUpdateService.BuildApplyScript(
                payload, install, 999999, new[] { "Action", "Master" },
                new List<ManifestComponentInfo> { action, master });

            // Baris peluncuran exe + hapus-diri tidak relevan di sandbox.
            script = string.Join("\n", script.Split('\n')
                .Where(l => !l.Contains("Start-Process -FilePath $exe") && !l.Contains("Remove-Item -LiteralPath $MyInvocation")));

            var scriptPath = Path.Combine(root, "apply.ps1");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi)!)
            {
                Assert.True(p.WaitForExit(120000), "applier tidak selesai");
            }

            // --- Bukti ---
            var logPath = Path.Combine(Path.GetTempPath(), "bmachine-update.log");
            var log = File.Exists(logPath) ? File.ReadAllText(logPath) : "(log kosong)";
            var scriptDump = Path.Combine(Path.GetTempPath(), "bmachine-apply-test.ps1");
            File.WriteAllText(scriptDump, script, new UTF8Encoding(false));

            Assert.True(File.ReadAllText(Path.Combine(install, "Scripts/Action/b.jsx")) == "// versi baru",
                "b.jsx tidak ter-update.\n--- applier log ---\n" + log + "\n--- script: " + scriptDump);
            Assert.Equal("// file baru", File.ReadAllText(Path.Combine(install, "Scripts/Action/c.jsx")));

            // File yang isinya sama TIDAK boleh disentuh (bukti: timestamp tidak berubah).
            Assert.Equal(aTime, File.GetLastWriteTimeUtc(aPath));

            // Master atomic: m2 berubah -> m1 ikut disalin walau isinya sudah sama.
            Assert.Equal("# master 2 versi baru", File.ReadAllText(Path.Combine(install, "Scripts/Master/m2.py")));
            Assert.True(File.GetLastWriteTimeUtc(Path.Combine(install, "Scripts/Master/m1.py")) > m1Time,
                "komponen atomic harus menyalin seluruh isi folder");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
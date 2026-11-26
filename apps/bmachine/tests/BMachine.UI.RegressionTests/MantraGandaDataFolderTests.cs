using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

public class MantraGandaDataFolderTests
{
    private static (string root, string dataDir, string masterDir) MakeTemp(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), "ganda_test_" + prefix + "_" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "data");
        var masterDir = Path.Combine(root, "master");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(masterDir);
        return (root, dataDir, masterDir);
    }

    /// <summary>
    /// Jalankan ExecuteDuplicateAsync dari thread test + pump dispatcher headless
    /// hingga semua InvokeAsync dari thread pool selesai diproses.
    /// Sama polanya dengan ExplorerLogicTests.WaitFor.
    /// </summary>
    private static void RunGanda(MantraGandaViewModel vm, Func<bool>? done = null, int timeoutMs = 10_000)
    {
        var task = vm.ExecuteDuplicateAsync();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (done != null && done())
            {
                if (!task.IsCompleted)
                {
                    task.GetAwaiter().GetResult(); // wait for remaining UI callbacks to settle
                }
                else
                {
                    task.GetAwaiter().GetResult(); // re-throw exception if any
                }
                return;
            }
            if (task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                task.GetAwaiter().GetResult(); // re-throw exception if any
                return;
            }
            Thread.Sleep(25);
        }
        Dispatcher.UIThread.RunJobs();
        throw new TimeoutException($"ExecuteDuplicateAsync tidak selesai dalam {timeoutMs} ms. Status: {vm.StatusText}");
    }

    [AvaloniaFact]
    public void DataFolderModeRunsEvenWhenDuplicateCountIsZero()
    {
        var (root, dataDir, masterDir) = MakeTemp("count0");
        try
        {
            // Master folder with one PSD
            var masterPsd = Path.Combine(masterDir, "template.psd");
            File.WriteAllText(masterPsd, "master");

            // Data folder with two subfolders each containing a JPG
            Directory.CreateDirectory(Path.Combine(dataDir, "kelas_a"));
            Directory.CreateDirectory(Path.Combine(dataDir, "kelas_b"));
            File.WriteAllText(Path.Combine(dataDir, "kelas_a", "foto1.jpg"), "a");
            File.WriteAllText(Path.Combine(dataDir, "kelas_b", "foto2.jpg"), "b");

            var vm = new MantraGandaViewModel();
            vm.MasterPath = masterDir;
            vm.DataFolder = dataDir;
            vm.DuplicateCount = 0; // Would have blocked the old guard
            vm.DataFileFormat = 0;  // Semua (auto-detect: .jpg)

            var expectedA = Path.Combine(masterDir, "kelas_a", "foto1.psd");
            var expectedB = Path.Combine(masterDir, "kelas_b", "foto2.psd");

            RunGanda(vm, () => File.Exists(expectedA) && File.Exists(expectedB));

            var masterList = Directory.Exists(masterDir) ? Directory.GetFiles(masterDir, "*.*", SearchOption.AllDirectories) : Array.Empty<string>();
            var logDump = string.Join(" | ", vm.Logs);
            var masterDump = string.Join(", ", masterList);
            Assert.True(vm.IsProcessing == false, "IsProcessing should be false after completion");
            Assert.True(File.Exists(expectedA), $"subfolder A copy missing. Status: {vm.StatusText}. Logs: {logDump}. MasterDir files: {masterDump}");
            Assert.True(File.Exists(expectedB), $"subfolder B copy missing. Status: {vm.StatusText}. Logs: {logDump}");
            Assert.Contains("[OK]", string.Join("\n", vm.Logs));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void SingleMasterFileMirrorsDataSubfolderStructure()
    {
        var (root, dataDir, masterDir) = MakeTemp("single");
        try
        {
            // Single master file at root of masterDir
            var masterPsd = Path.Combine(masterDir, "master.psd");
            File.WriteAllText(masterPsd, "master");

            // Data folder: nested structure with a file in a subfolder
            Directory.CreateDirectory(Path.Combine(dataDir, "sub", "deep"));
            File.WriteAllText(Path.Combine(dataDir, "root.jpg"), "r");
            File.WriteAllText(Path.Combine(dataDir, "sub", "mid.png"), "m");
            File.WriteAllText(Path.Combine(dataDir, "sub", "deep", "low.jpg"), "l");

            var vm = new MantraGandaViewModel();
            vm.MasterPath = masterPsd;
            vm.DataFolder = dataDir;
            vm.DuplicateCount = 1;
            // Index 0 = Semua (auto-detect: .jpg, .png)
            vm.DataFileFormat = 0;

            var expectedRoot = Path.Combine(masterDir, "root.psd");
            var expectedMid = Path.Combine(masterDir, "sub", "mid.psd");
            var expectedDeep = Path.Combine(masterDir, "sub", "deep", "low.psd");

            RunGanda(vm, () => File.Exists(expectedRoot) && File.Exists(expectedMid) && File.Exists(expectedDeep));

            Assert.True(File.Exists(expectedRoot), $"root-level copy missing. Status: {vm.StatusText}");
            Assert.True(File.Exists(expectedMid), $"subfolder copy missing. Status: {vm.StatusText}");
            Assert.True(File.Exists(expectedDeep), $"deep subfolder copy missing. Status: {vm.StatusText}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void MasterFolderModeStillUsesParentFolderFallback()
    {
        var (root, dataDir, masterDir) = MakeTemp("fallback");
        try
        {
            // Master has PSD only in a parent folder
            Directory.CreateDirectory(Path.Combine(masterDir, "kelas"));
            File.WriteAllText(Path.Combine(masterDir, "kelas", "template.psd"), "m");

            // Data has a file deeper than master
            Directory.CreateDirectory(Path.Combine(dataDir, "kelas", "layer1"));
            File.WriteAllText(Path.Combine(dataDir, "kelas", "layer1", "img.jpg"), "x");

            var vm = new MantraGandaViewModel();
            vm.MasterPath = masterDir;
            vm.DataFolder = dataDir;
            vm.DuplicateCount = 0;
            vm.DataFileFormat = 0;

            var expected = Path.Combine(masterDir, "kelas", "layer1", "img.psd");
            RunGanda(vm, () => File.Exists(expected));

            // Should use parent folder PSD and create the copy
            Assert.True(File.Exists(expected), $"parent-fallback copy missing. Status: {vm.StatusText}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

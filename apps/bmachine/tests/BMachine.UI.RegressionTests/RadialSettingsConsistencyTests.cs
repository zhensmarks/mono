using System;
using System.IO;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Bug yang dilaporkan setelah 15.5.0:
/// 1. grup (TOOLBOX) tidak bisa dipindah posisinya di roda pengaturan;
/// 2. item yang dihapus masih tampil di roda pengaturan padahal roda live sudah bersih;
/// 3. tinggi kartu pengaturan beda dengan sidebar (seperti ada footer).
/// Ketiganya dikunci di sini supaya tidak kembali.
/// </summary>
public class RadialSettingsConsistencyTests
{
    [Fact]
    public void SettingsWheelUsesTheSameOrderSourceAsDrag()
    {
        var vm = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/SettingsViewModel.cs");

        // RebuildRadialSlots TIDAK boleh punya filter/urutan sendiri: harus memakai
        // BuildMergedRadialOrder() yang juga dipakai drag (Preview/ApplyRadialMove).
        Assert.Contains("var sortedScripts = BuildMergedRadialOrder();", vm);

        // Filter "Enabled" hanya boleh ada di SATU tempat (BuildMergedRadialOrder).
        var enabledFilters = CountOccurrences(vm, "|| c.Enabled)");
        Assert.Equal(1, enabledFilters);

        // Duplikat Ord() juga hanya satu.
        Assert.Equal(1, CountOccurrences(vm,
            "int Ord(ScriptItem s) => _scriptAliases.TryGetValue(s.OriginalName, out var c) ? c.Order : 9999;"));
    }

    [Fact]
    public void LiveMenuStillHidesDisabledItems()
    {
        var vm = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");

        // Roda live: item dengan alias Enabled=false tidak ikut tampil.
        Assert.Contains("!cfg.Enabled", vm);
    }

    [Fact]
    public void SettingsPanelsMatchSidebarHeight()
    {
        var xaml = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml");

        // Sidebar flush ke bawah grid; panel kolom kanan dulu punya margin bawah 20 sehingga
        // terlihat seperti ada footer dengan tinggi berbeda.
        Assert.Contains("x:Name=\"Part_Sidebar\"", xaml);
        Assert.Contains("Margin=\"0,0,18,0\"", xaml);
        Assert.DoesNotContain("Margin=\"0,0,14,20\"", xaml);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new FileNotFoundException($"{relativePath} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}
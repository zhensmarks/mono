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
    public void DropOnAnotherItemSwapsPositions()
    {
        var view = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml.cs");
        var vm = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/SettingsViewModel.cs");

        // Drag = TUKAR posisi dua item pada URUTAN TAMPIL (switch), bukan menggeser urutan.
        Assert.Contains("vm.PreviewRadialSwap(_radialDragSource, target);", view);
        Assert.DoesNotContain("PreviewRadialMove(_radialDragSource", view);
        Assert.Contains("(sequence[sourceAt], sequence[targetAt]) = (sequence[targetAt], sequence[sourceAt]);", vm);

        // Menukar angka Order tidak dipakai lagi: nilai Order user bisa kembar.
        Assert.DoesNotContain("SwapAliasOrder", vm);

        // Jangan menormalkan urutan saat drag mulai (membuat roda "mengurut" sendiri).
        var begin = vm.Substring(vm.IndexOf("public void BeginRadialPreview()"), 220);
        Assert.DoesNotContain("NormalizeAllOrders();", begin);

        // Item di luar roda diparkir >= 10000 DENGAN URUTAN RELATIF TETAP.
        Assert.Contains("int parked = 10000;", vm);
        Assert.Contains(".OrderBy(kv => kv.Value.Order)", vm);
    }

    [Fact]
    public void DropTargetUsesButtonDistanceNotCanvasCenter()
    {
        var view = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml.cs");

        Assert.Contains("RadialSlotPicker.FindNearestForSlots(", view);
        Assert.DoesNotContain("s.Angle).ToList(), pos.X, pos.Y);", view);
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
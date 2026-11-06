using System;
using System.Linq;
using Avalonia;
using BMachine.UI.Models;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Hover radial cuma boleh menyala SATU elemen: kalau pointer ada di cincin luar (submenu
/// grup / tab DOC), wedge cincin dalam milik tombol induk TIDAK ikut menyala.
/// Keluhan user: "2-2 nya nyala, harusnya yang di dalam tidak ikut menyala".
/// </summary>
public class RadialSingleHighlightTests
{
    private const double Window = 320.0;
    private const double Center = Window / 2.0;

    private static RadialMenuItem Item(string name, string shortName, string tabId = "")
        => new()
        {
            FullName = name,
            ShortName = shortName,
            TabId = tabId,
            Angle = 0,
            WedgeStartAngle = 337.5,
            WedgeSweepAngle = 45
        };

    [Fact]
    public void OuterRingHoverDoesNotLightInnerWedge()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        var parent = Item("TOOLBOX", "TB");
        var member = Item("FOOTNOTE", "TF", tabId: "");
        vm.VisibleItems.Add(parent);
        vm.SubmenuItems.Add(member);

        // Pointer tepat di cincin luar (jarak 128 px dari pusat, arah atas).
        vm.UpdateHighlight(new Point(Center, Center - 128), new Size(Window, Window));

        Assert.True(member.IsHighlighted, "item cincin luar yang di-hover harus menyala");
        Assert.False(parent.IsHighlighted, "tombol induk tidak boleh ikut menyala");
        Assert.False(vm.IsWedgeVisible, "wedge cincin dalam tidak boleh tampil saat fokus di submenu");

        // Pindah ke tombol induk (cincin dalam) -> wedge dalam yang menyala, anggota padam.
        vm.SubmenuItems.Clear();
        vm.UpdateHighlight(new Point(Center, Center - 70), new Size(Window, Window));

        Assert.True(parent.IsHighlighted);
        Assert.True(vm.IsWedgeVisible);
    }

    [Fact]
    public void InnerWedgeFollowsMainRingNotTabId()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        // Item roda utama yang punya TabId (dulu dipakai sebagai penanda "cincin luar")
        // tetap harus menampilkan wedge dalam saat di-hover.
        var mainItem = Item("SEND TEXT", "ST", tabId: "tab-1");
        vm.VisibleItems.Add(mainItem);

        vm.UpdateHighlight(new Point(Center, Center - 70), new Size(Window, Window));

        Assert.True(mainItem.IsHighlighted);
        Assert.True(vm.IsWedgeVisible);
    }

    [Fact]
    public void SourceKeepsWedgeBoundToMainRing()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");

        Assert.Contains("IsWedgeVisible = VisibleItems.Contains(_currentHighlightedItem);", src);
        Assert.DoesNotContain("IsWedgeVisible = string.IsNullOrEmpty(_currentHighlightedItem.TabId);", src);
    }

    [Fact]
    public void OuterRingItemCreationLeavesTabIdEmptyForGroups()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");
        var radial = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/RadialMenuWindow.axaml");

        // Anggota grup dibuat dengan TabId kosong (bukan tab DOC), jadi penanda cincin luar
        // TIDAK boleh memakai TabId.
        Assert.Contains("TabId = \"\",", src);
        // Cincin luar tetap punya highlight sendiri.
        Assert.Contains("InnerRadius=\"104\" OuterRadius=\"152\"", radial);
        Assert.Contains("IsVisible=\"{Binding IsHighlighted}\"", radial);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "WORKSPACES.md")))
                return System.IO.File.ReadAllText(System.IO.Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new System.IO.FileNotFoundException($"{relativePath} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}
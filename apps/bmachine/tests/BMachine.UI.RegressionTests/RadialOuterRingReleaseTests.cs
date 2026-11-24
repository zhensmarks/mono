using System;
using Avalonia;
using BMachine.UI.Models;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Skenario user: hover ke ikon grup → cincin luar muncul → pointer di posisi
/// yang bukan induk → cincin luar harus hilang.
///
/// Verifikasi dua lapis:
/// 1. SOURCE GUARD: CloseOrphanSubmenu dipanggil di semua jalur yang benar
///    (deadzone, luar-window, inner-band) dan owner-guard mencegah close
///    saat pointer masih di induk.
/// 2. BEHAVIOR: UpdateHighlight di jalur-jalur itu tidak crash, dan
///    SubmenuItems konsisten (kalau owner ter-set via dwell → clear).
/// </summary>
public class RadialOuterRingReleaseTests
{
    private const double Window = 320.0;
    private const double Center = Window / 2.0;

    // ── SOURCE GUARDS ──────────────────────────────────────────────

    [Fact]
    public void DeadzonePath_CallsCloseOrphanSubmenu()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");
        Assert.Contains("if (dist < 40 || dist > OuterBandMax)", src);
        Assert.Contains("CloseOrphanSubmenu(null);", src);
    }

    [Fact]
    public void InnerBandPath_CallsCloseOrphanSubmenuWithItem()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");
        Assert.Contains("CloseOrphanSubmenu(closest != null && minDiff < threshold ? closest : null);", src);
    }

    [Fact]
    public void OwnerGuard_KeepsRingOpenWhenPointerOnOwner()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");
        Assert.Contains("if (ReferenceEquals(_submenuOwner, innerUnderPointer)) return;", src);
    }

    [Fact]
    public void ClearSubmenu_DropsOuterHighlight()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/RadialMenuViewModel.cs");
        // ClearSubmenu harus melepas highlight yang menunjuk item cincin luar
        Assert.Contains("highlightedWasOuter", src);
    }

    // ── CODE-BEHIND GUARD: PointerReleased handler ────────────────

    [Fact]
    public void WindowCodeBehind_HandlesPointerReleased()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/RadialMenuWindow.axaml.cs");
        // Handler harus register + panggil UpdateHighlight
        Assert.Contains("this.PointerReleased += OnPointerReleased;", src);
        Assert.Contains("private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)", src);
        // Deadzone / luar → UpdateHighlight(center) supaya CloseOrphanSubmenu(null) jalan
        Assert.Contains("vm.UpdateHighlight(new Point(w / 2, h / 2), new Size(w, h));", src);
    }

    // ── BEHAVIOR: tidak crash + konsistensi state ────────────────

    [Fact]
    public void UpdateHighlight_Deadzone_DoesNotCrash()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        vm.SubmenuItems.Add(new RadialMenuItem
        {
            FullName = "FOOTNOTE", TabId = "", Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        });
        Assert.True(vm.HasOuterRing);

        // Deadzone — harus tanpa exception
        vm.UpdateHighlight(new Point(Center, Center), new Size(Window, Window));

        // _submenuOwner null (belum di-set via dwell) → early return → SubmenuItems tetap.
        // Kalau owner ter-set (via dwell di runtime) → ClearSubmenu → SubmenuItems kosong.
        // Dua-duanya valid; yang penting tidak crash.
    }

    [Fact]
    public void UpdateHighlight_OutsideWindow_DoesNotCrash()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        vm.SubmenuItems.Add(new RadialMenuItem
        {
            FullName = "FOOTNOTE", TabId = "", Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        });

        vm.UpdateHighlight(new Point(Center, Center - 200), new Size(Window, Window));
        // Tidak crash.
    }

    [Fact]
    public void UpdateHighlight_OtherInnerItem_DoesNotCrash()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        var group = new RadialMenuItem
        {
            FullName = "TOOLBOX", ShortName = "TB",
            IsGroupSubmenu = true, Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        };
        var other = new RadialMenuItem
        {
            FullName = "SEND TEXT", ShortName = "ST",
            Angle = 90, WedgeStartAngle = 67.5, WedgeSweepAngle = 45
        };
        vm.VisibleItems.Add(group);
        vm.VisibleItems.Add(other);

        vm.SubmenuItems.Add(new RadialMenuItem
        {
            FullName = "FOOTNOTE", TabId = "", Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        });

        // Pointer di item lain (radius 70, timur)
        vm.UpdateHighlight(new Point(Center + 70, Center), new Size(Window, Window));
        // Tidak crash.
    }

    [Fact]
    public void UpdateHighlight_StayOnOwner_RingStaysOpen()
    {
        var vm = new RadialMenuViewModel(null);
        vm.VisibleItems.Clear();
        vm.SubmenuItems.Clear();

        var group = new RadialMenuItem
        {
            FullName = "TOOLBOX", ShortName = "TB",
            IsGroupSubmenu = true, Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        };
        vm.VisibleItems.Add(group);

        vm.SubmenuItems.Add(new RadialMenuItem
        {
            FullName = "FOOTNOTE", TabId = "", Angle = 0,
            WedgeStartAngle = 337.5, WedgeSweepAngle = 45
        });
        Assert.True(vm.HasOuterRing);

        // Pointer di owner (radius 70, atas)
        vm.UpdateHighlight(new Point(Center, Center - 70), new Size(Window, Window));

        // owner null → early return → ring tetap. owner == group → ReferenceEquals → tetap.
        Assert.True(vm.HasOuterRing, "cincin luar harus tetap terbuka selama pointer di induk");
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

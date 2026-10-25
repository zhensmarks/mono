using FrameStudio.App.Models;
using FrameStudio.Engine;
using Xunit;

namespace FrameStudio.Tests;

public sealed class FrameModeAssignmentTests
{
    [Fact]
    public void SingleUsesOnlyValidatedXlEvenWhenOtherTargetsExist()
    {
        var xl = PhotoLayer(FrameLayerRole.XLPhoto, ready: true);
        var small = PhotoLayer(FrameLayerRole.SPhoto, ready: true);
        var background = BackgroundGroup();
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Single,
            new[] { Slot(FrameLayerRole.XLPhoto), Slot(FrameLayerRole.SPhoto) },
            Tree(xl, small, background));

        Assert.True(plan.TargetsReady);
        Assert.True(plan.CanExportPng);
        Assert.Equal(new[] { FrameLayerRole.XLPhoto }, plan.PhotoRoles);
    }

    [Fact]
    public void SingleRejectsInvalidXlEvenIfSAndBackgroundAreValid()
    {
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Single,
            new[] { Slot(FrameLayerRole.SPhoto) },
            Tree(
                PhotoLayer(FrameLayerRole.XLPhoto, ready: false),
                PhotoLayer(FrameLayerRole.SPhoto, ready: true),
                BackgroundGroup()));

        Assert.False(plan.TargetsReady);
        Assert.False(plan.CanExportPng);
        Assert.Contains("XL", plan.StatusText, StringComparison.Ordinal);
        Assert.Contains("batas positif", plan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleRejectsMissingXlInsteadOfFallingBackToS()
    {
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Single,
            new[] { Slot(FrameLayerRole.SPhoto) },
            Tree(PhotoLayer(FrameLayerRole.SPhoto, ready: true), BackgroundGroup()));

        Assert.False(plan.TargetsReady);
        Assert.Contains("XL: target tidak ditemukan", plan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void KolaseRequiresXlSAndNonEmptyBgKecilGroupAndDoesNotClaimExport()
    {
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Kolase,
            new[] { Slot(FrameLayerRole.XLPhoto), Slot(FrameLayerRole.SPhoto) },
            Tree(
                PhotoLayer(FrameLayerRole.XLPhoto, ready: true),
                PhotoLayer(FrameLayerRole.SPhoto, ready: true),
                BackgroundGroup()));

        Assert.True(plan.TargetsReady);
        Assert.False(plan.CanExportPng);
        Assert.Equal(new[] { FrameLayerRole.XLPhoto, FrameLayerRole.SPhoto }, plan.PhotoRoles);
        Assert.Contains("belum dapat diaktifkan", plan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void KolaseRejectsMissingSOrBgKecilWithoutPartialPlan()
    {
        var missingS = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Kolase,
            new[] { Slot(FrameLayerRole.XLPhoto) },
            Tree(PhotoLayer(FrameLayerRole.XLPhoto, ready: true), BackgroundGroup()));
        var missingBackground = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Kolase,
            new[] { Slot(FrameLayerRole.XLPhoto), Slot(FrameLayerRole.SPhoto) },
            Tree(PhotoLayer(FrameLayerRole.XLPhoto, ready: true), PhotoLayer(FrameLayerRole.SPhoto, ready: true)));

        Assert.False(missingS.TargetsReady);
        Assert.Contains("S: target tidak ditemukan", missingS.StatusText, StringComparison.Ordinal);
        Assert.False(missingBackground.TargetsReady);
        Assert.Contains("BG kecil: grup tidak ditemukan", missingBackground.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateXlTargetsAreRejectedAsAmbiguous()
    {
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Single,
            new[] { Slot(FrameLayerRole.XLPhoto), Slot(FrameLayerRole.XLPhoto) },
            Tree(PhotoLayer(FrameLayerRole.XLPhoto, ready: true), PhotoLayer(FrameLayerRole.XLPhoto, ready: true)));

        Assert.False(plan.TargetsReady);
        Assert.True(plan.XlTarget.Ambiguous);
        Assert.Contains("ambigu", plan.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KolaseRejectsBgKecilRoleWhenNodeIsNotAGroup()
    {
        var nonGroup = PhotoLayer(FrameLayerRole.BGKecil, ready: true);
        var plan = FrameModeAssignmentPlan.Create(
            FrameAssignmentMode.Kolase,
            new[] { Slot(FrameLayerRole.XLPhoto), Slot(FrameLayerRole.SPhoto) },
            Tree(
                PhotoLayer(FrameLayerRole.XLPhoto, ready: true),
                PhotoLayer(FrameLayerRole.SPhoto, ready: true),
                nonGroup));

        Assert.False(plan.TargetsReady);
        Assert.Contains("BG kecil", plan.StatusText, StringComparison.Ordinal);
    }

    private static FrameSlot Slot(FrameLayerRole role) => new(
        role.ToString(),
        role.ToString(),
        SlotKind.Photo,
        "synthetic",
        bounds: new FrameSlotBounds(1, 1, 10, 10),
        isPlacementReady: true,
        assignmentRole: role);

    private static FramePsdLayerSummary PhotoLayer(FrameLayerRole role, bool ready) => new(
        $"node-{role}",
        role.ToString(),
        "Pixel layer",
        false,
        true,
        255,
        ready ? new FrameBounds(1, 1, 10, 10) : new FrameBounds(0, 0, 0, 0),
        true,
        ready,
        false,
        false,
        Array.Empty<FramePsdLayerSummary>())
    {
        AssignmentRole = role,
        IsPlacementReady = ready
    };

    private static FramePsdLayerSummary BackgroundGroup() => new(
        "node-bg",
        "Group 1",
        "Group",
        true,
        false,
        255,
        new FrameBounds(0, 0, 100, 100),
        false,
        false,
        false,
        false,
        new[]
        {
            new FramePsdLayerSummary("bg-child", "Pixel layer 1", "Pixel layer", false, true, 255,
                new FrameBounds(0, 0, 100, 100), true, false, false, false, Array.Empty<FramePsdLayerSummary>())
        })
    {
        AssignmentRole = FrameLayerRole.BGKecil
    };

    private static FramePsdLayerTreeSummary Tree(params FramePsdLayerSummary[] roots) => new(
        roots.Length,
        roots.Count(node => node.IsGroup),
        0,
        0,
        0,
        0,
        roots);
}

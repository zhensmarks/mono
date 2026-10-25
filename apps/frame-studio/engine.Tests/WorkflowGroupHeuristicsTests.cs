using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class WorkflowGroupHeuristicsTests
{
    [Fact]
    public void DetectsSingleAndCollageHintsInsideNestedFotoGroup()
    {
        var foto = Group("FOTO", new[]
        {
            Group("XL", new[] { SmartObject("PRIVATE_XL_ASSET_1") }),
            Group("S", new[] { SmartObject("PRIVATE_S_ASSET_1") })
        });
        var smallBackground = Group("BG kecil", Array.Empty<FramePsdLayer>());

        var hints = FramePsdWorkflowGroupHeuristics.Analyze(new[] { foto, smallBackground });

        Assert.Equal(1, hints.FotoGroupCount);
        Assert.Equal(1, hints.XlSubgroupCount);
        Assert.Equal(1, hints.SmallSubgroupCount);
        Assert.Equal(1, hints.XlSmartObjectCandidateCount);
        Assert.Equal(1, hints.SmallSmartObjectCandidateCount);
        Assert.Equal(1, hints.SmallBackgroundGroupCount);
        Assert.True(hints.HasSingleCandidate);
        Assert.True(hints.HasCollageCandidates);
        Assert.Contains("KOLASE", hints.ToSafeSummary(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", hints.ToSafeSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void SingleHintDoesNotRequireOrTouchSmallCandidate()
    {
        var foto = Group("FOTO", new[]
        {
            Group("XL", new[] { SmartObject("XL photo object") })
        });
        var unrelatedSmall = Group("S", new[] { SmartObject("S object outside FOTO") });

        var hints = FramePsdWorkflowGroupHeuristics.Analyze(new[] { foto, unrelatedSmall });

        Assert.True(hints.HasSingleCandidate);
        Assert.False(hints.HasCollageCandidates);
        Assert.Equal(0, hints.SmallSubgroupCount);
        Assert.Equal(0, hints.SmallBackgroundGroupCount);
        Assert.Contains("Slot S", hints.ToSafeSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void AmbiguousFotoGroupsAndMissingSmartObjectsRemainUnusableHints()
    {
        var emptyFoto = Group("FOTO", new[] { Group("XL", Array.Empty<FramePsdLayer>()) });
        var secondFoto = Group("FOTO 2", new[] { Group("XL", new[] { SmartObject("another XL") }) });
        var hints = FramePsdWorkflowGroupHeuristics.Analyze(new[] { emptyFoto, secondFoto });

        Assert.Equal(2, hints.FotoGroupCount);
        Assert.False(hints.HasSingleCandidate);
        Assert.False(hints.HasCollageCandidates);
        Assert.Contains("ambigu", hints.ToSafeSummary(), StringComparison.OrdinalIgnoreCase);
    }

    private static FramePsdLayer Group(string name, IReadOnlyList<FramePsdLayer> children) => new(
        $"synthetic/{name}",
        name,
        true,
        255,
        new FrameBounds(0, 0, 16, 12),
        false,
        null,
        children)
    {
        Kind = "group",
        IsGroup = true
    };

    private static FramePsdLayer SmartObject(string name) => new(
        $"synthetic/{name}",
        name,
        true,
        255,
        new FrameBounds(2, 2, 8, 6),
        true,
        null,
        Array.Empty<FramePsdLayer>())
    {
        Kind = "smartobject"
    };
}

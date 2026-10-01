using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class ToolDockColumnPolicyTests
{
    [Fact]
    public void AutoKeepsOneColumnWhenTheFullListFits()
    {
        Assert.False(ToolDockColumnPolicy.UseTwoColumns(1100, 940, preferTwoColumns: false));
    }

    [Fact]
    public void AutoSwitchesToTwoColumnsBeforeTheListWouldClip()
    {
        Assert.True(ToolDockColumnPolicy.UseTwoColumns(520, 940, preferTwoColumns: false));
    }

    [Fact]
    public void UserPreferenceCanSelectTwoColumnsWhenOneColumnWouldFit()
    {
        Assert.True(ToolDockColumnPolicy.UseTwoColumns(1100, 940, preferTwoColumns: true));
    }

    [Fact]
    public void UnknownAvailableHeightUsesTheSafeTwoColumnLayout()
    {
        Assert.True(ToolDockColumnPolicy.UseTwoColumns(0, 940, preferTwoColumns: false));
    }
}

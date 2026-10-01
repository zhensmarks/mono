using PixelcutCompact.Services.Editing;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class TemporaryPanToolStateTests
{
    [Theory]
    [InlineData(EditToolKind.Pan)]
    [InlineData(EditToolKind.Brush)]
    [InlineData(EditToolKind.Eraser)]
    [InlineData(EditToolKind.Lasso)]
    [InlineData(EditToolKind.MagicWand)]
    [InlineData(EditToolKind.Move)]
    [InlineData(EditToolKind.RefineEdge)]
    public void SpaceTemporarilyUsesPanAndRestoresTheExactPreviouslySelectedTool(EditToolKind previousTool)
    {
        var state = new TemporaryPanToolState(previousTool);

        Assert.True(state.HoldSpace());
        Assert.Equal(EditToolKind.Pan, state.EffectiveTool);
        Assert.Equal(previousTool, state.SelectedTool);
        Assert.False(state.HoldSpace());

        Assert.True(state.ReleaseSpace());
        Assert.Equal(previousTool, state.EffectiveTool);
        Assert.False(state.ReleaseSpace());
    }

    [Fact]
    public void ChangingTheSelectedToolWhileSpaceIsHeldRestoresTheNewSelectionOnRelease()
    {
        var state = new TemporaryPanToolState(EditToolKind.Brush);
        state.HoldSpace();
        state.SelectTool(EditToolKind.Lasso);

        Assert.Equal(EditToolKind.Pan, state.EffectiveTool);
        Assert.True(state.ReleaseSpace());
        Assert.Equal(EditToolKind.Lasso, state.EffectiveTool);
    }
}

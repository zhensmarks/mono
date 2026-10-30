using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Jangkar load harus mengikuti range yang benar-benar dikembalikan Google, supaya
/// penomoran tidak bergeser diam-diam bila respons mulai di baris/kolom lain.
/// </summary>
public sealed class SpreadsheetLoadAlignmentTests
{
    [Fact]
    public void RealResponseRangeKeepsConfiguredAnchor()
    {
        // Bentuk respons asli sheet produksi untuk config "A3:Z".
        var anchor = SpreadsheetLoadAlignment.Resolve(
            "'ALL DATA REGULER'!A3:Z13205", 3, 0);

        Assert.Equal(3, anchor.StartRow);
        Assert.Equal(0, anchor.StartColumn);
    }

    [Fact]
    public void ResponseStartingOneRowLowerMovesTheAnchor()
    {
        var anchor = SpreadsheetLoadAlignment.Resolve(
            "'ALL DATA REGULER'!A4:Z13205", 3, 0);

        Assert.Equal(4, anchor.StartRow);
        Assert.Equal(0, anchor.StartColumn);
    }

    [Fact]
    public void ResponseStartingAtAnotherColumnMovesTheAnchor()
    {
        var anchor = SpreadsheetLoadAlignment.Resolve("'Sheet 1'!B5:Z900", 3, 0);

        Assert.Equal(5, anchor.StartRow);
        Assert.Equal(1, anchor.StartColumn);
    }

    [Fact]
    public void AbsoluteReferencesAreUnderstood()
    {
        var anchor = SpreadsheetLoadAlignment.Resolve("'Sheet'!$A$4:$Z$900", 3, 0);

        Assert.Equal(4, anchor.StartRow);
        Assert.Equal(0, anchor.StartColumn);
    }

    [Fact]
    public void RangeWithoutSheetNameIsUnderstood()
    {
        var anchor = SpreadsheetLoadAlignment.Resolve("C7:Z900", 3, 0);

        Assert.Equal(7, anchor.StartRow);
        Assert.Equal(2, anchor.StartColumn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("3:Z")]
    public void UnusableResponseRangeFallsBackToConfiguredAnchor(string? responseRange)
    {
        var anchor = SpreadsheetLoadAlignment.Resolve(responseRange, 3, 0);

        Assert.Equal(3, anchor.StartRow);
        Assert.Equal(0, anchor.StartColumn);
    }

    [Fact]
    public void FallbackKeepsNonZeroConfiguredColumn()
    {
        var anchor = SpreadsheetLoadAlignment.Resolve(null, 3, 26);

        Assert.Equal(3, anchor.StartRow);
        Assert.Equal(26, anchor.StartColumn);
    }
}

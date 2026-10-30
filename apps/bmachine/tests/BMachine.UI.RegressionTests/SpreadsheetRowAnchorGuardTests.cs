using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Pengaman anti grid-basi: kalau sheet bergeser setelah data dimuat, simpan harus
/// dibatalkan supaya nilai tidak mendarat 1 baris di atas sel yang diedit.
/// </summary>
public sealed class SpreadsheetRowAnchorGuardTests
{
    private static IList<IList<object>> Sheet(params string[] keys)
        => keys.Select(key => (IList<object>)new List<object> { key }).ToList();

    private static List<SpreadsheetRowAnchor> Anchors(int firstRow, params string[] keys)
        => keys.Select((key, index) => new SpreadsheetRowAnchor(firstRow + index, key)).ToList();

    [Fact]
    public void AlignedSheetProducesNoMismatch()
    {
        var anchors = Anchors(540, "F-540", "F-541", "F-542");
        var sheet = Sheet("F-540", "F-541", "F-542");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void SheetShiftedUpByOneRowIsDetected()
    {
        // Skenario bug: sheet kehilangan 1 baris di atas, jadi baris 540 kini berisi F-539.
        var anchors = Anchors(540, "F-540", "F-541", "F-542");
        var shifted = Sheet("F-539", "F-540", "F-541");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540);

        Assert.Equal(3, mismatches.Count);
        Assert.Equal(540, mismatches[0].SheetRowNumber);
        Assert.Equal("F-540", mismatches[0].Expected);
        Assert.Equal("F-539", mismatches[0].Actual);
    }

    [Fact]
    public void SheetShiftedDownByOneRowIsDetected()
    {
        // Baris disisip di atas: baris 540 kini berisi F-541.
        var anchors = Anchors(540, "F-540", "F-541");
        var shifted = Sheet("F-541", "F-542");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540);

        Assert.Equal(2, mismatches.Count);
        Assert.Equal("F-541", mismatches[0].Actual);
    }

    [Fact]
    public void RowsWithoutKeyAreSkippedInsteadOfBlockingTheSave()
    {
        var anchors = Anchors(540, "", "F-541");
        var sheet = Sheet("", "F-541");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void MissingTrailingValuesAreTreatedAsMismatch()
    {
        // Respons API memangkas baris kosong di akhir: baris 542 tidak ikut terbaca.
        var anchors = Anchors(540, "F-540", "F-541", "F-542");
        var truncated = Sheet("F-540", "F-541");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, truncated, 540);

        Assert.Single(mismatches);
        Assert.Equal(542, mismatches[0].SheetRowNumber);
        Assert.Equal(string.Empty, mismatches[0].Actual);
    }

    [Fact]
    public void NullResponseIsTreatedAsMismatchForEveryAnchoredRow()
    {
        var anchors = Anchors(540, "F-540");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, null, 540);

        Assert.Single(mismatches);
        Assert.Equal(540, mismatches[0].SheetRowNumber);
    }

    [Fact]
    public void ValuesAreTrimmedBeforeComparison()
    {
        var anchors = Anchors(540, "  F-540  ");
        var sheet = Sheet("F-540");

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void DescribeNamesTheFirstShiftedRow()
    {
        var anchors = Anchors(540, "F-540", "F-541");
        var shifted = Sheet("F-539", "F-540");

        var message = SpreadsheetRowAnchorGuard.Describe(
            SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540));

        Assert.Contains("540", message);
        Assert.Contains("F-539", message);
        Assert.Contains("F-540", message);
        Assert.Contains("Refresh", message);
    }
}

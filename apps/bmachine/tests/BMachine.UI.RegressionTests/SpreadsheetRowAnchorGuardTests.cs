using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Pengaman anti grid-basi: kalau sheet berubah setelah data dimuat, simpan harus dibatalkan
/// supaya nilai tidak mendarat di baris yang salah.
///
/// Jangkar memakai sidik jari SELURUH baris, bukan kolom kunci saja, karena pada sheet
/// produksi 70% baris punya NO FAKTUR yang sama dengan baris di atasnya (satu invoice
/// beberapa item) sehingga pergeseran di dalam blok item tidak terlihat dari kolom A.
/// </summary>
public sealed class SpreadsheetRowAnchorGuardTests
{
    private const int Columns = 3;

    private static string Fingerprint(params string[] cells)
        => SpreadsheetRowFingerprint.FromValues(cells, Columns);

    private static SpreadsheetRowAnchor Anchor(int sheetRow, params string[] cells)
    {
        var fingerprint = Fingerprint(cells);
        return new SpreadsheetRowAnchor(
            sheetRow, fingerprint, SpreadsheetRowFingerprint.DisplayOf(fingerprint));
    }

    private static IList<IList<object>> Sheet(params string[][] rows)
        => rows.Select(cells => (IList<object>)cells.Cast<object>().ToList()).ToList();

    [Fact]
    public void AlignedSheetProducesNoMismatch()
    {
        var anchors = new[]
        {
            Anchor(540, "INV-1", "2026-08-05", "ITEM-A"),
            Anchor(541, "INV-1", "2026-08-05", "ITEM-B"),
        };
        var sheet = Sheet(
            new[] { "INV-1", "2026-08-05", "ITEM-A" },
            new[] { "INV-1", "2026-08-05", "ITEM-B" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540, Columns);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void ShiftInsideRunOfIdenticalKeysIsStillDetected()
    {
        // Kasus yang lolos kalau guard hanya membandingkan kolom A:
        // baris 539-541 semuanya "INV-1" (satu invoice, tiga item), sheet bergeser -1.
        var anchors = new[]
        {
            Anchor(539, "INV-1", "2026-08-05", "ITEM-A"),
            Anchor(540, "INV-1", "2026-08-05", "ITEM-B"),
            Anchor(541, "INV-1", "2026-08-05", "ITEM-C"),
        };
        var shifted = Sheet(
            new[] { "INV-1", "2026-08-05", "ITEM-A" },   // baris 539 sekarang berisi item A
            new[] { "INV-1", "2026-08-05", "ITEM-A" },
            new[] { "INV-1", "2026-08-05", "ITEM-B" });  // baris 540 sekarang berisi item B

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 539, Columns);

        Assert.Equal(2, mismatches.Count);
        Assert.Equal(540, mismatches[0].SheetRowNumber);
        Assert.Equal("INV-1", mismatches[0].ExpectedDisplay);
        Assert.Equal("INV-1", mismatches[0].ActualDisplay);
    }

    [Fact]
    public void SheetShiftedUpByOneRowIsDetected()
    {
        var anchors = new[]
        {
            Anchor(540, "INV-540", "2026-08-05", "ITEM-540"),
            Anchor(541, "INV-541", "2026-08-05", "ITEM-541"),
        };
        var shifted = Sheet(
            new[] { "INV-539", "2026-08-05", "ITEM-539" },
            new[] { "INV-540", "2026-08-05", "ITEM-540" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540, Columns);

        Assert.Equal(2, mismatches.Count);
        Assert.Equal(540, mismatches[0].SheetRowNumber);
        Assert.Equal("INV-540", mismatches[0].ExpectedDisplay);
        Assert.Equal("INV-539", mismatches[0].ActualDisplay);
    }

    [Fact]
    public void SheetShiftedDownByOneRowIsDetected()
    {
        var anchors = new[] { Anchor(540, "INV-540", "2026-08-05", "ITEM-540") };
        var shifted = Sheet(new[] { "INV-541", "2026-08-05", "ITEM-541" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540, Columns);

        Assert.Single(mismatches);
        Assert.Equal("INV-541", mismatches[0].ActualDisplay);
    }

    [Fact]
    public void ChangeInALaterColumnIsDetected()
    {
        var anchors = new[] { Anchor(540, "INV-1", "2026-08-05", "ITEM-A") };
        var changed = Sheet(new[] { "INV-1", "2026-08-05", "ITEM-B" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, changed, 540, Columns);

        Assert.Single(mismatches);
    }

    [Fact]
    public void FullyEmptyRowIsSkippedInsteadOfBlockingTheSave()
    {
        var anchors = new[] { Anchor(540, "", "", ""), Anchor(541, "INV-2", "", "") };
        var sheet = Sheet(new[] { "", "", "" }, new[] { "INV-2", "", "" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540, Columns);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void MissingTrailingValuesAreTreatedAsMismatch()
    {
        var anchors = new[]
        {
            Anchor(540, "INV-1", "", ""),
            Anchor(541, "INV-2", "", ""),
            Anchor(542, "INV-3", "", ""),
        };
        var truncated = Sheet(new[] { "INV-1", "", "" }, new[] { "INV-2", "", "" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, truncated, 540, Columns);

        Assert.Single(mismatches);
        Assert.Equal(542, mismatches[0].SheetRowNumber);
        Assert.Equal(string.Empty, mismatches[0].ActualDisplay);
    }

    [Fact]
    public void NullResponseIsTreatedAsMismatchForEveryAnchoredRow()
    {
        var anchors = new[] { Anchor(540, "INV-1", "", "") };

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, null, 540, Columns);

        Assert.Single(mismatches);
        Assert.Equal(540, mismatches[0].SheetRowNumber);
    }

    [Fact]
    public void ShorterSheetRowIsPaddedAndStillMatches()
    {
        // API memangkas sel kosong di kanan: baris hanya berisi 1 sel.
        var anchors = new[] { Anchor(540, "INV-1", "", "") };
        var sheet = Sheet(new[] { "INV-1" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540, Columns);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void TrailingSpaceDifferenceIsDetected()
    {
        var anchors = new[] { Anchor(540, "INV-1", "", "") };
        var sheet = Sheet(new[] { "INV-1 ", "", "" });

        var mismatches = SpreadsheetRowAnchorGuard.FindMismatches(anchors, sheet, 540, Columns);

        Assert.Single(mismatches);
    }

    [Fact]
    public void DescribeNamesTheFirstShiftedRow()
    {
        var anchors = new[] { Anchor(540, "INV-540", "", "") };
        var shifted = Sheet(new[] { "INV-539", "", "" });

        var message = SpreadsheetRowAnchorGuard.Describe(
            SpreadsheetRowAnchorGuard.FindMismatches(anchors, shifted, 540, Columns));

        Assert.Contains("540", message);
        Assert.Contains("INV-539", message);
        Assert.Contains("INV-540", message);
        Assert.Contains("Refresh", message);
    }
}

using System.Globalization;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class SpreadsheetCellWritePlannerTests
{
    [Fact]
    public void EditingRows540Through545WritesOnlyThoseAbsoluteSheetRows()
    {
        const int rangeStartRow = 539;
        const int columnIndex = 2;
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        var fakeSheet = new Dictionary<string, string>();

        for (var sheetRow = 539; sheetRow <= 546; sheetRow++)
        {
            var valuesArrayRowIndex = sheetRow - rangeStartRow;
            var absoluteRow = SpreadsheetValueMapper.GetSheetRowNumber(rangeStartRow, valuesArrayRowIndex);
            var row = new SpreadsheetRowViewModel(valuesArrayRowIndex, vm, absoluteRow);
            var oldValue = $"sentinel-{sheetRow}";
            var cell = new SpreadsheetCellViewModel
            {
                Value = sheetRow is >= 540 and <= 545 ? $"edited-{sheetRow}" : oldValue,
                OriginalValue = oldValue,
                OriginalRawValue = oldValue,
                InputKind = SpreadsheetCellInputKind.Text,
                ColumnIndex = 0
            };
            row.Cells.Add(cell);
            vm.Rows.Add(row);
            fakeSheet[SpreadsheetValueMapper.GetCellRange("Synthetic", sheetRow, columnIndex)] = oldValue;
        }

        var writes = SpreadsheetCellWritePlanner.Create(
            vm.Rows, "Synthetic", columnIndex, CultureInfo.InvariantCulture);

        Assert.Equal(6, writes.Count);
        Assert.Equal(
            Enumerable.Range(540, 6).Select(row => $"'Synthetic'!C{row}"),
            writes.Select(write => write.Range));

        foreach (var write in writes)
            fakeSheet[write.Range] = Assert.IsType<string>(write.MappedValue.Value);

        Assert.Equal("sentinel-539", fakeSheet["'Synthetic'!C539"]);
        Assert.Equal("sentinel-546", fakeSheet["'Synthetic'!C546"]);
        for (var row = 540; row <= 545; row++)
            Assert.Equal($"edited-{row}", fakeSheet[$"'Synthetic'!C{row}"]);
    }

    [Theory]
    [InlineData("A539:Z", 539, 0)]
    [InlineData("AA539:AC", 539, 26)]
    [InlineData("$AB$539:$AC", 539, 27)]
    [InlineData("AA:AC", 1, 26)]
    public void RangeStartAndColumnNamesUseOneBasedSheetCoordinates(
        string range, int expectedRow, int expectedColumn)
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(range);

        Assert.Equal(expectedRow, start.RowNumber);
        Assert.Equal(expectedColumn, start.ColumnIndex);
        Assert.Equal(expectedColumn, SpreadsheetValueMapper.GetColumnIndex(
            SpreadsheetValueMapper.GetColumnName(expectedColumn)));
    }

    [Fact]
    public void HeaderAtRow539AndValuesIndexesOneThroughSixMapToSheetRows540Through545()
    {
        const int headerRow = 539;
        Assert.Equal(headerRow, SpreadsheetValueMapper.GetSheetRowNumber(headerRow, 0));
        Assert.Equal(Enumerable.Range(540, 6), Enumerable.Range(1, 6)
            .Select(valuesArrayIndex => SpreadsheetValueMapper.GetSheetRowNumber(headerRow, valuesArrayIndex)));
    }
}

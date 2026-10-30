using System.Globalization;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Mengunci alignment baris untuk konfigurasi PRODUKSI yang sebenarnya:
/// Spreadsheet.Range = "A3:Z" pada sheet "ALL DATA REGULER".
///
/// Bentuk sheet: row 1 = judul, row 2 = kosong, row 3 = header kolom, row 4+ = data.
/// Karena range dimulai di A3, values[0] = header (row 3) dan values[i] = row 3 + i.
/// Kesalahan off-by-one di sini membuat penulisan mendarat 1 baris di atas sel yang diedit
/// (mis. 540-545 tertulis ke 539-544), jadi tes ini menjaga rumus tersebut.
/// </summary>
public sealed class SpreadsheetProductionRangeAlignmentTests
{
    private const string ProductionRange = "A3:Z";
    private const string ProductionSheet = "ALL DATA REGULER";
    private const int HeaderRow = 3;

    [Fact]
    public void ProductionRangeStartsAtRow3AndColumnA()
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(ProductionRange);

        Assert.Equal(HeaderRow, start.RowNumber);
        Assert.Equal(0, start.ColumnIndex);
        Assert.Equal("A", SpreadsheetValueMapper.GetColumnName(start.ColumnIndex));
    }

    [Fact]
    public void HeaderIsRow3AndFirstDataRowIsRow4()
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(ProductionRange);

        // values[0] = header
        Assert.Equal(HeaderRow, SpreadsheetValueMapper.GetSheetRowNumber(start.RowNumber, 0));
        // values[1] = baris data pertama
        Assert.Equal(HeaderRow + 1, SpreadsheetValueMapper.GetSheetRowNumber(start.RowNumber, 1));
    }

    [Fact]
    public void EditingSheetRows540To545WritesToThoseExactRows()
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(ProductionRange);
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        var sheet = new Dictionary<string, string>();

        // Bangun grid persis seperti LoadData: satu baris per values index,
        // ditambah satu baris tetangga di atas & bawah sebagai penjaga batas.
        for (var sheetRow = 539; sheetRow <= 546; sheetRow++)
        {
            var valuesIndex = sheetRow - start.RowNumber;
            var absoluteRow = SpreadsheetValueMapper.GetSheetRowNumber(start.RowNumber, valuesIndex);
            Assert.Equal(sheetRow, absoluteRow);

            var row = new SpreadsheetRowViewModel(valuesIndex, vm, absoluteRow);
            var original = $"sentinel-{sheetRow}";
            row.Cells.Add(new SpreadsheetCellViewModel
            {
                Value = sheetRow is >= 540 and <= 545 ? $"edited-{sheetRow}" : original,
                OriginalValue = original,
                OriginalRawValue = original,
                InputKind = SpreadsheetCellInputKind.Text,
                ColumnIndex = 0
            });
            vm.Rows.Add(row);
            sheet[SpreadsheetValueMapper.GetCellRange(ProductionSheet, sheetRow, start.ColumnIndex)] = original;
        }

        var writes = SpreadsheetCellWritePlanner.Create(
            vm.Rows, ProductionSheet, start.ColumnIndex, CultureInfo.InvariantCulture);

        Assert.Equal(
            Enumerable.Range(540, 6).Select(r => $"'ALL DATA REGULER'!A{r}"),
            writes.Select(w => w.Range));

        foreach (var write in writes)
            sheet[write.Range] = Assert.IsType<string>(write.MappedValue.Value);

        // Baris yang tidak diedit tidak boleh ikut berubah.
        Assert.Equal("sentinel-539", sheet["'ALL DATA REGULER'!A539"]);
        Assert.Equal("sentinel-546", sheet["'ALL DATA REGULER'!A546"]);
        for (var row = 540; row <= 545; row++)
            Assert.Equal($"edited-{row}", sheet[$"'ALL DATA REGULER'!A{row}"]);
    }

    [Fact]
    public void RowNumberingIsNotShiftedByOne()
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(ProductionRange);

        // Baris sheet 540 = values index 537 untuk range yang dimulai di row 3.
        var valuesIndex = 540 - start.RowNumber;
        Assert.Equal(537, valuesIndex);
        Assert.Equal(540, SpreadsheetValueMapper.GetSheetRowNumber(start.RowNumber, valuesIndex));

        // Kanari: kalau rumus bergeser -1, range yang dihasilkan jadi A539.
        var range = SpreadsheetValueMapper.GetCellRange(ProductionSheet, 540, 0);
        Assert.Equal("'ALL DATA REGULER'!A540", range);
        Assert.NotEqual("'ALL DATA REGULER'!A539", range);
    }

    [Theory]
    [InlineData(0, "A540")]
    [InlineData(7, "H540")]
    [InlineData(25, "Z540")]
    public void WriteRangeUsesConfiguredStartColumn(int cellColumnIndex, string expectedCell)
    {
        var start = SpreadsheetValueMapper.ParseRangeStart(ProductionRange);

        var range = SpreadsheetValueMapper.GetCellRange(
            ProductionSheet, 540, cellColumnIndex + start.ColumnIndex);

        Assert.Equal($"'ALL DATA REGULER'!{expectedCell}", range);
    }
}

using System.Globalization;

namespace BMachine.UI.ViewModels;

internal sealed record SpreadsheetCellWrite(
    SpreadsheetCellViewModel Cell,
    string EditedValue,
    string Range,
    SpreadsheetMappedValue MappedValue);

internal static class SpreadsheetCellWritePlanner
{
    public static List<SpreadsheetCellWrite> Create(
        IEnumerable<SpreadsheetRowViewModel> rows,
        string sheetName,
        int rangeStartColumn,
        CultureInfo culture)
    {
        var writes = new List<SpreadsheetCellWrite>();
        foreach (var row in rows)
        {
            foreach (var cell in row.Cells.Where(candidate => candidate.IsChanged))
            {
                var range = SpreadsheetValueMapper.GetCellRange(
                    sheetName,
                    row.SheetRowNumber,
                    checked(cell.ColumnIndex + rangeStartColumn));
                var mappedValue = SpreadsheetValueMapper.MapForWrite(
                    cell.Value,
                    cell.OriginalRawValue,
                    cell.InputKind,
                    culture);
                writes.Add(new SpreadsheetCellWrite(cell, cell.Value, range, mappedValue));
            }
        }
        return writes;
    }
}

using System.Globalization;

namespace BMachine.UI.ViewModels;

internal enum SpreadsheetCellInputKind
{
    Text,
    Number,
    Date,
    Boolean
}

internal enum SpreadsheetValueInputMode
{
    Raw,
    UserEntered
}

internal readonly record struct SpreadsheetMappedValue(object Value, SpreadsheetValueInputMode InputMode);
internal readonly record struct SpreadsheetRangeStart(int RowNumber, int ColumnIndex);

internal static class SpreadsheetValueMapper
{
    public static SpreadsheetCellInputKind InferKind(object? value, bool isDateColumn = false)
    {
        if (isDateColumn) return SpreadsheetCellInputKind.Date;
        return value switch
        {
            bool => SpreadsheetCellInputKind.Boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => SpreadsheetCellInputKind.Number,
            _ => SpreadsheetCellInputKind.Text
        };
    }

    public static int GetSheetRowNumber(int rangeStartRow, int valuesRowIndex)
    {
        if (rangeStartRow < 1) throw new ArgumentOutOfRangeException(nameof(rangeStartRow));
        if (valuesRowIndex < 0) throw new ArgumentOutOfRangeException(nameof(valuesRowIndex));
        return checked(rangeStartRow + valuesRowIndex);
    }

    public static SpreadsheetRangeStart ParseRangeStart(string range)
    {
        if (string.IsNullOrWhiteSpace(range)) throw new ArgumentException("A spreadsheet range is required.", nameof(range));

        var start = range.Split(':', 2)[0].Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        var separator = start.LastIndexOf('!');
        if (separator >= 0) start = start[(separator + 1)..];

        var letters = new string(start.TakeWhile(char.IsLetter).ToArray());
        var digits = new string(start.SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());
        if (letters.Length == 0)
            throw new FormatException($"Range start '{range}' does not contain a column reference.");

        var columnIndex = GetColumnIndex(letters);
        var rowNumber = digits.Length == 0 ? 1 : int.Parse(digits, CultureInfo.InvariantCulture);
        if (rowNumber < 1) throw new FormatException($"Range start '{range}' has an invalid row number.");
        return new SpreadsheetRangeStart(rowNumber, columnIndex);
    }

    public static int GetColumnIndex(string columnLetters)
    {
        if (string.IsNullOrWhiteSpace(columnLetters)) throw new ArgumentException("A column reference is required.", nameof(columnLetters));

        var oneBasedIndex = 0;
        foreach (var character in columnLetters)
        {
            if (!char.IsLetter(character)) throw new FormatException($"Invalid column reference '{columnLetters}'.");
            var letter = char.ToUpperInvariant(character);
            if (letter is < 'A' or > 'Z') throw new FormatException($"Invalid column reference '{columnLetters}'.");
            oneBasedIndex = checked(oneBasedIndex * 26 + letter - 'A' + 1);
        }
        return oneBasedIndex - 1;
    }

    public static string GetColumnName(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        var oneBasedIndex = zeroBasedIndex + 1;
        var columnName = string.Empty;
        while (oneBasedIndex > 0)
        {
            var remainder = (oneBasedIndex - 1) % 26;
            columnName = Convert.ToChar('A' + remainder) + columnName;
            oneBasedIndex = (oneBasedIndex - 1) / 26;
        }
        return columnName;
    }

    public static string GetCellRange(string sheetName, int sheetRowNumber, int zeroBasedColumnIndex)
    {
        if (sheetRowNumber < 1) throw new ArgumentOutOfRangeException(nameof(sheetRowNumber));
        return $"{QuoteSheetName(sheetName)}!{GetColumnName(zeroBasedColumnIndex)}{sheetRowNumber}";
    }

    public static SpreadsheetCellInputKind InferColumnKind(
        IList<IList<object>>? rows,
        int columnIndex,
        bool isDateColumn)
    {
        if (isDateColumn) return SpreadsheetCellInputKind.Date;
        if (rows == null) return SpreadsheetCellInputKind.Text;

        foreach (var row in rows)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Count) continue;
            var value = row[columnIndex];
            if (value == null || value is string text && string.IsNullOrWhiteSpace(text)) continue;
            return InferKind(value);
        }

        return SpreadsheetCellInputKind.Text;
    }

    public static SpreadsheetMappedValue MapForWrite(
        string? input,
        object? originalRawValue,
        SpreadsheetCellInputKind columnInputKind,
        CultureInfo culture)
    {
        var text = input ?? string.Empty;
        if (text.Length == 0)
            return new SpreadsheetMappedValue(string.Empty, SpreadsheetValueInputMode.Raw);

        // Formulas are the only values intentionally sent through Sheets' parser.
        if (text.StartsWith("=", StringComparison.Ordinal))
            return new SpreadsheetMappedValue(text, SpreadsheetValueInputMode.UserEntered);

        var kind = columnInputKind == SpreadsheetCellInputKind.Date
            ? SpreadsheetCellInputKind.Date
            : originalRawValue == null
                ? columnInputKind
                : InferKind(originalRawValue);

        switch (kind)
        {
            case SpreadsheetCellInputKind.Number:
                if (TryParseNumber(text, culture, out var number))
                    return new SpreadsheetMappedValue(number, SpreadsheetValueInputMode.Raw);
                break;

            case SpreadsheetCellInputKind.Date:
                if (TryParseDate(text, culture, out var date))
                    return new SpreadsheetMappedValue(date.ToOADate(), SpreadsheetValueInputMode.Raw);
                if (TryParseNumber(text, culture, out var serial))
                    return new SpreadsheetMappedValue(serial, SpreadsheetValueInputMode.Raw);
                break;

            case SpreadsheetCellInputKind.Boolean:
                if (bool.TryParse(text.Trim(), out var boolean))
                    return new SpreadsheetMappedValue(boolean, SpreadsheetValueInputMode.Raw);
                break;
        }

        // RAW keeps identifiers, leading zeros, and date-like text from being reinterpreted.
        return new SpreadsheetMappedValue(text, SpreadsheetValueInputMode.Raw);
    }

    public static string QuoteSheetName(string sheetName)
        => $"'{(sheetName ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'";

    private static bool TryParseNumber(string text, CultureInfo culture, out double number)
    {
        return double.TryParse(
            text.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            culture,
            out number);
    }

    private static bool TryParseDate(string text, CultureInfo culture, out DateTime date)
    {
        var trimmed = text.Trim();
        string[] invariantFormats =
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
        };

        if (DateTime.TryParseExact(
                trimmed,
                invariantFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out date))
            return true;

        if (DateTime.TryParse(trimmed, culture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        date = default;
        return false;
    }
}

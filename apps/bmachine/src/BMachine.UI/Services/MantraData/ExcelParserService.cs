using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;
using ClosedXML.Excel;

namespace BMachine.UI.Services.MantraData;

public record SheetResult(
    string Name,
    List<string> Columns,
    List<TableDataRow> Rows,
    bool NeedsHeaderChoice = false,
    int? HeaderRowIndex = null,
    List<List<object?>>? SourceMatrix = null);

public class ExcelParserService
{
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        {"no", "No"},
        {"nomor", "No"},
        {"nis", "NIS"},
        {"nisn nip", "NISN/NIP"},
        {"nis nip", "NIS/NIP"},
        {"nisn nis", "NISN/NIS"},
        {"nama", "Nama"},
        {"nama lengkap", "Nama"},
        {"nama siswa", "Nama"},
        {"nama peserta didik", "Nama"},
        {"nama guru tendik", "Nama"},
        {"nama guru/ tendik", "Nama"},
        {"jk", "JK"},
        {"jk gender", "JK/Gender"},
        {"jenis kelamin", "JK"},
        {"kelamin", "JK"},
        {"kelas", "Kelas"},
        {"kelas rombel", "Kelas/Rombel"},
        {"rombel", "Kelas"},
        {"rombel saat ini", "Kelas"},
        {"nisn", "NISN"},
        {"tempat", "Tempat Lahir"},
        {"tempat lahir", "Tempat Lahir"},
        {"tanggal lahir", "Tanggal Lahir"},
        {"tgl lahir", "Tanggal Lahir"},
        {"tempat tgl lahir", "Tempat, Tanggal Lahir"},
        {"tempat tanggal lahir", "Tempat, Tanggal Lahir"},
        {"alamat", "Alamat"},
        {"jalan", "Alamat"},
        {"rt", "RT"},
        {"rw", "RW"},
        {"dusun", "Dusun"},
        {"kelurahan", "Kelurahan"},
        {"kel desa", "Kelurahan"},
        {"kecamatan", "Kecamatan"},
        {"agama", "Agama"},
        {"jabatan", "Jabatan"}
    };

    private static readonly string[] StudentHeaders =
    {
        "No", "Nama", "Kelas", "JK", "NISN", "Tempat Lahir", "Tanggal Lahir",
        "Agama", "Alamat", "RT", "RW", "Dusun", "Kelurahan", "Kecamatan", "Kelas (2)"
    };

    private static string CleanHeader(object? value)
    {
        var raw = value?.ToString() ?? "";
        var text = Regex.Replace(raw.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        if (HeaderAliases.TryGetValue(text, out var alias)) return alias;
        return raw.Trim();
    }

    private static bool IsIdentifierHeader(string header)
    {
        var h = CleanHeader(header);
        return h.Equals("NISN", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("NIS", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("NIK", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("No", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Nomor", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Nomor Peserta", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("No Peserta", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Nomor Induk", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Kode", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Kode Siswa", StringComparison.OrdinalIgnoreCase) ||
               h.Equals("Kode Guru", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeGetCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        
        try
        {
            var cellValue = cell.Value;
            
            if (cellValue.IsBlank) return "";
            if (cellValue.IsBoolean) return cellValue.GetBoolean() ? "Ya" : "Tidak";
            if (cellValue.IsDateTime) return cellValue.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (cellValue.IsNumber) 
            {
                var num = cellValue.GetNumber();
                if (num == Math.Floor(num) && !double.IsInfinity(num))
                {
                    var fmt = cell.Style.NumberFormat.Format;
                    try
                    {
                        if (!string.IsNullOrEmpty(fmt) &&
                            !fmt.Equals("General", StringComparison.OrdinalIgnoreCase) &&
                            !fmt.Contains("#") && !fmt.Contains("%") && !fmt.Contains("."))
                        {
                            var formatted = cell.GetFormattedString();
                            var digits = Regex.Replace(formatted, @"\D", "");
                            var plain = ((long)num).ToString("0", CultureInfo.InvariantCulture);
                            if (!string.IsNullOrEmpty(formatted) &&
                                (digits.Length > plain.Length ||
                                 long.TryParse(formatted, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _)))
                                return formatted.Replace(",", "").Replace(" ", "");
                        }
                    }
                    catch
                    {
                    }
                    return ((long)num).ToString();
                }
                return num.ToString(CultureInfo.InvariantCulture);
            }
            if (cellValue.IsText) return cellValue.GetText().Trim();
            
            return cell.GetText().Trim();
        }
        catch
        {
            return cell.GetText().Trim();
        }
    }

    private static string DisplayValue(object? value)
    {
        if (value == null) return "";
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "Ya" : "Tidak";
        if (value is double d && d == Math.Floor(d) && !double.IsInfinity(d)) return ((long)d).ToString();
        if (value is float f && f == Math.Floor(f) && !float.IsInfinity(f)) return ((long)f).ToString();
        return value.ToString()?.Trim() ?? "";
    }

    private static string DisplayExcelCell(IXLCell cell, string header)
    {
        if (cell.IsEmpty()) return "";

        var textValue = cell.GetText().Trim();
        if (IsIdentifierHeader(header))
        {
            if (!string.IsNullOrEmpty(textValue))
                return textValue;
        }

        return cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => cell.GetDouble() % 1 == 0
                ? ((long)cell.GetDouble()).ToString()
                : cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "Ya" : "Tidak",
            _ => textValue
        };
    }

    private static List<string> UniqueHeaders(List<object?> values, int width)
    {
        var result = new List<string>();
        var counts = new Dictionary<string, int>();
        
        for (int i = 0; i < width; i++)
        {
            var raw = i < values.Count ? values[i] : "";
            var baseName = CleanHeader(raw);
            if (string.IsNullOrEmpty(baseName)) baseName = $"Kolom {i + 1}";

            counts.TryGetValue(baseName, out int cnt);
            counts[baseName] = cnt + 1;

            result.Add(cnt > 0 ? $"{baseName} ({cnt + 1})" : baseName);
        }

        return result;
    }

    private static (int? Index, List<string> Headers) DetectHeader(List<List<object?>> rows)
    {
        int width = rows.Count > 0 ? rows.Max(r => r.Count) : 0;
        if (width == 0) return (null, new List<string>());

        // A dense row is not necessarily a header: headerless student rosters are
        // often denser than their headings. Require at least two recognized labels.
        for (int i = 0; i < Math.Min(40, rows.Count); i++)
        {
            var row = rows[i];
            var values = row.Where(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())).ToList();
            if (values.Count < 2) continue;

            int recognized = values.Count(v =>
            {
                var key = Regex.Replace(v?.ToString()?.ToLowerInvariant() ?? "", @"[^a-z0-9]+", " ").Trim();
                return HeaderAliases.ContainsKey(key);
            });

            if (recognized >= 2)
                return (i, UniqueHeaders(row, width));
        }

        // No reliable heading was found. Caller will keep all non-empty rows as
        // data under generic column names and may explicitly opt into row 1 as header.
        return (null, new List<string>());
    }

    private static List<List<object?>> TrimMatrix(List<List<object?>> matrix)
    {
        while (matrix.Count > 0 && !matrix[^1].Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
        {
            matrix.RemoveAt(matrix.Count - 1);
        }

        if (matrix.Count == 0) return matrix;

        int lastCol = 0;
        foreach (var row in matrix)
        {
            for (int index = 0; index < row.Count; index++)
            {
                var v = row[index];
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString()))
                    lastCol = Math.Max(lastCol, index + 1);
            }
        }

        return matrix.Select(r => r.Take(lastCol).ToList()).ToList();
    }

    private static char DetectDelimiter(string sample)
    {
        char best = ',';
        long bestScore = -1;
        foreach (var delimiter in new[] { ',', ';', '\t', '|' })
        {
            var records = ParseDelimitedText(sample, delimiter);
            var shaped = records.Select(r => r.Count).Where(count => count > 1).ToList();
            if (shaped.Count == 0) continue;
            int mostCommonWidth = shaped.GroupBy(width => width).Max(g => g.Count());
            long score = shaped.Count * 10000L + mostCommonWidth * 100L + shaped.Max();
            if (score > bestScore)
            {
                bestScore = score;
                best = delimiter;
            }
        }
        return best;
    }

    private static List<List<string>> ParseDelimitedText(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool recordHasContent = false;

        void FinishRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (recordHasContent || fields.Any(value => value.Length > 0))
                records.Add(fields);
            fields = new List<string>();
            recordHasContent = false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    recordHasContent = true;
                }
                else if (inQuotes)
                {
                    inQuotes = false;
                }
                else if (field.Length == 0)
                {
                    inQuotes = true;
                    recordHasContent = true;
                }
                else
                {
                    // Keep malformed/unescaped quotes rather than silently dropping them.
                    field.Append(ch);
                    recordHasContent = true;
                }
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
                recordHasContent = true;
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                FinishRecord();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                continue;
            }

            field.Append(ch);
            recordHasContent = true;
        }

        if (field.Length > 0 || fields.Count > 0 || recordHasContent)
            FinishRecord();
        return records;
    }

    public (List<string> Columns, List<TableDataRow> Rows) LoadDelimited(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
        }
        catch
        {
            text = File.ReadAllText(path, System.Text.Encoding.Default);
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);

        var delimiter = DetectDelimiter(text);
        var rawMatrix = ParseDelimitedText(text, delimiter)
            .Select(row => row.Select(value => (object?)value).ToList())
            .ToList();

        _allSheets.Clear();
        var result = BuildTableFromMatrix(rawMatrix, "Data");
        if (result.Rows.Count > 0 || result.NeedsHeaderChoice)
            _allSheets.Add(result);

        if (_allSheets.Count == 0)
            _allSheets.Add(new SheetResult("Data", StudentHeaders.ToList(), new List<TableDataRow>()));

        var first = _allSheets[0];
        return (first.Columns, first.Rows);
    }

    private SheetResult LoadSheet(IXLWorksheet ws)
    {
        var rawMatrix = new List<List<object?>>();
        int maxRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int maxCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (int r = 1; r <= maxRow; r++)
        {
            var row = new List<object?>();
            for (int c = 1; c <= maxCol; c++)
            {
                var cell = ws.Cell(r, c);
                row.Add(cell.IsEmpty() ? null : SafeGetCellText(cell));
            }
            rawMatrix.Add(row);
        }

        return BuildTableFromMatrix(rawMatrix, ws.Name);
    }

    private SheetResult BuildTableFromMatrix(
        List<List<object?>> matrix,
        string sheetName,
        bool useFirstNonEmptyRowAsHeader = false)
    {
        var sourceMatrix = matrix.Select(row => row.ToList()).ToList();
        matrix = TrimMatrix(matrix);
        if (matrix.Count == 0)
            return new SheetResult(sheetName, new List<string>(), new List<TableDataRow>(), SourceMatrix: sourceMatrix);

        var (headerIndex, headers) = DetectHeader(matrix);
        bool needsHeaderChoice = !headerIndex.HasValue && matrix.Any(row => row.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())));
        if (!headerIndex.HasValue && useFirstNonEmptyRowAsHeader)
        {
            int firstNonEmpty = matrix.FindIndex(row => row.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())));
            if (firstNonEmpty >= 0)
            {
                headerIndex = firstNonEmpty;
                headers = UniqueHeaders(matrix[firstNonEmpty], matrix.Max(row => row.Count));
                needsHeaderChoice = false;
            }
        }
        if (headers.Count == 0)
        {
            int width = matrix.Max(row => row.Count);
            headers = Enumerable.Range(1, width).Select(i => $"Kolom {i}").ToList();
        }

        var preambleRows = headerIndex.HasValue
            ? matrix.Take(headerIndex.Value).Where(row => row.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()))).ToList()
            : new List<List<object?>>();
        string? preambleColumn = null;
        if (preambleRows.Count > 0)
        {
            preambleColumn = "Catatan Impor";
            int suffix = 2;
            while (headers.Contains(preambleColumn, StringComparer.OrdinalIgnoreCase))
                preambleColumn = $"Catatan Impor ({suffix++})";
            headers.Add(preambleColumn);
        }

        int dataStart = headerIndex.HasValue ? headerIndex.Value + 1 : 0;
        while (dataStart < matrix.Count && !matrix[dataStart].Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
        {
            dataStart++;
        }

        var rows = new List<TableDataRow>();
        int rowNumber = 1;

        foreach (var preamble in preambleRows)
        {
            var noteRow = new TableDataRow { RowNumber = rowNumber++ };
            foreach (var header in headers) noteRow[header] = string.Empty;
            noteRow[preambleColumn!] = string.Join(" | ", preamble.Select(v => v?.ToString() ?? "").Where(v => !string.IsNullOrWhiteSpace(v)));
            rows.Add(noteRow);
        }

        for (int mIdx = dataStart; mIdx < matrix.Count; mIdx++)
        {
            var matrixRow = matrix[mIdx];
            var dataRow = new TableDataRow { RowNumber = rowNumber++ };
            bool hasAnyData = false;

            for (int cIdx = 0; cIdx < headers.Count; cIdx++)
            {
                var headerName = headers[cIdx];
                string cellText = "";

                if (cIdx < matrixRow.Count)
                {
                    cellText = matrixRow[cIdx]?.ToString() ?? "";
                }

                if (!string.IsNullOrEmpty(cellText))
                    hasAnyData = true;

                dataRow[headerName] = cellText;
            }

            if (hasAnyData)
            {
                rows.Add(dataRow);
            }
            else
            {
                rowNumber--;
            }
        }

        return new SheetResult(sheetName, headers, rows, needsHeaderChoice, headerIndex, sourceMatrix);
    }

    private List<SheetResult> _allSheets = new();

    public (List<string> Columns, List<TableDataRow> Rows) LoadExcel(string path)
    {
        _allSheets.Clear();

        using var workbook = new XLWorkbook(path);
        foreach (var ws in workbook.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible))
        {
            var result = LoadSheet(ws);
            if (result.Rows.Count > 0 || result.NeedsHeaderChoice)
            {
                _allSheets.Add(result);
            }
        }

        if (_allSheets.Count == 0)
        {
            _allSheets.Add(new SheetResult("Sheet1", StudentHeaders.ToList(), new List<TableDataRow>()));
        }

        var first = _allSheets[0];
        return (first.Columns, first.Rows);
    }

    public List<SheetResult> GetAllSheets() => _allSheets;

    public void UseFirstNonEmptyRowAsHeaderForUnrecognizedSheets()
    {
        for (int i = 0; i < _allSheets.Count; i++)
        {
            var sheet = _allSheets[i];
            if (!sheet.NeedsHeaderChoice || sheet.SourceMatrix == null) continue;
            _allSheets[i] = BuildTableFromMatrix(
                sheet.SourceMatrix.Select(row => row.ToList()).ToList(),
                sheet.Name,
                useFirstNonEmptyRowAsHeader: true);
        }
    }

    public static void SaveExcel(string path, List<string> columns, List<TableDataRow> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Data");

        for (int c = 0; c < columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = columns[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var val = rows[r].Values.TryGetValue(columns[c], out var v) ? v : "";
                ws.Cell(r + 2, c + 1).Value = val;
            }
        }

        ws.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    public static void SaveTxt(string path, List<string> columns, List<TableDataRow> rows)
    {
        using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        w.WriteLine(string.Join("\t", columns));
        foreach (var row in rows)
            w.WriteLine(string.Join("\t", columns.Select(c => row.Values.TryGetValue(c, out var v) ? v : "")));
    }

    public static void SaveCsv(string path, List<string> columns, List<TableDataRow> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        static string Quote(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        w.WriteLine(string.Join(",", columns.Select(Quote)));
        foreach (var row in rows)
            w.WriteLine(string.Join(",", columns.Select(c => Quote(row.Values.TryGetValue(c, out var v) ? v : string.Empty))));
    }

    public static string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var words = input.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "")));
    }

    public static string FormatIndonesianDate(string input, string formatType)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var match = Regex.Match(input, @"(\d{1,2})[\/\-\s](\d{1,2})[\/\-\s](\d{4})");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int day) &&
            int.TryParse(match.Groups[2].Value, out int month) &&
            int.TryParse(match.Groups[3].Value, out int year))
        {
            if (month > 0 && month <= 12)
            {
                var monthNames = new[] { "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
                return formatType switch
                {
                    "dd MMMM yyyy" => $"{day} {monthNames[month - 1]} {year}",
                    "dd-MM-yyyy" => $"{day:D2}-{month:D2}-{year}",
                    _ => input
                };
            }
        }

            return input;
    }

    public string ExportToDater(string path, List<string> columns, List<TableDataRow> rows)
    {
        var daterDir = Path.Combine(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory, "DATER");
        if (!Directory.Exists(daterDir)) Directory.CreateDirectory(daterDir);
        var xlsxPath = Path.Combine(daterDir, Path.GetFileNameWithoutExtension(path) + ".xlsx");
        SaveExcel(xlsxPath, columns, rows);
        return xlsxPath;
    }
    public void ExportToTxt(string path, List<string> columns, List<TableDataRow> rows)
    {
        SaveTxt(path, columns, rows);
    }

    public void ExportToCsv(string path, List<string> columns, List<TableDataRow> rows)
    {
        SaveCsv(path, columns, rows);
    }

    public void ExportToXlsx(string path, List<string> columns, List<TableDataRow> rows)
    {
        SaveExcel(path, columns, rows);
    }
}

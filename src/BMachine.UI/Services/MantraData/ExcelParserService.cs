using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;
using ClosedXML.Excel;

namespace BMachine.UI.Services.MantraData;

public record SheetResult(string Name, List<string> Columns, List<TableDataRow> Rows);

public class ExcelParserService
{
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        {"no", "No"},
        {"nomor", "No"},
        {"nis", "NIS"},
        {"nama", "Nama"},
        {"nama lengkap", "Nama"},
        {"nama siswa", "Nama"},
        {"nama peserta didik", "Nama"},
        {"nama guru tendik", "Nama"},
        {"nama guru/ tendik", "Nama"},
        {"jk", "JK"},
        {"jenis kelamin", "JK"},
        {"kelamin", "JK"},
        {"kelas", "Kelas"},
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
               h.Equals("Nomor", StringComparison.OrdinalIgnoreCase);
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

    private static bool RowIsStudentData(List<object?> values)
    {
        if (values.Count < 7) return false;
        var number = values[0]?.ToString()?.Trim() ?? "";
        var gender = values[3]?.ToString()?.Trim().ToUpperInvariant() ?? "";
        var nisnDigits = Regex.Replace(values[4]?.ToString() ?? "", @"\D", "");

        return int.TryParse(number, out _) &&
               !string.IsNullOrWhiteSpace(values[1]?.ToString()) &&
               (gender == "L" || gender == "P") &&
               nisnDigits.Length >= 8;
    }

    private static (int? Index, List<string> Headers) DetectHeader(List<List<object?>> rows)
    {
        int width = rows.Count > 0 ? rows.Max(r => r.Count) : 0;

        for (int i = 0; i < Math.Min(5, rows.Count); i++)
        {
            var row = rows[i];
            int nonEmpty = row.Count(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()));
            if (nonEmpty >= width * 0.6)
            {
                return (i, UniqueHeaders(row, width));
            }
        }

        int? bestIndex = null;
        for (int i = 0; i < Math.Min(10, rows.Count); i++)
        {
            if (RowIsStudentData(rows[i]))
            {
                bestIndex = i > 0 ? i - 1 : null;
                break;
            }
        }

        if (!bestIndex.HasValue)
        {
            for (int i = 0; i < Math.Min(10, rows.Count); i++)
            {
                var nonEmptyCount = rows[i].Count(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()));
                if (nonEmptyCount > 0)
                {
                    bestIndex = i;
                    break;
                }
            }
        }

        int firstData = bestIndex.GetValueOrDefault(0);
        if (firstData > 0 && firstData < rows.Count)
        {
            return (firstData, UniqueHeaders(rows[firstData], width));
        }

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

        var matrix = TrimMatrix(rawMatrix);
        if (matrix.Count == 0)
            return new SheetResult(ws.Name, new List<string>(), new List<TableDataRow>());

        var (headerIndex, headers) = DetectHeader(matrix);
        if (headers.Count == 0)
            return new SheetResult(ws.Name, new List<string>(), new List<TableDataRow>());

        int dataStart = headerIndex.HasValue ? headerIndex.Value + 1 : 0;
        while (dataStart < matrix.Count && !matrix[dataStart].Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
        {
            dataStart++;
        }

        var rows = new List<TableDataRow>();
        int rowNumber = 1;

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
                    cellText = matrixRow[cIdx]?.ToString()?.Trim() ?? "";
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

        return new SheetResult(ws.Name, headers, rows);
    }

    private List<SheetResult> _allSheets = new();

    public (List<string> Columns, List<TableDataRow> Rows) LoadExcel(string path)
    {
        _allSheets.Clear();

        using var workbook = new XLWorkbook(path);
        foreach (var ws in workbook.Worksheets)
        {
            var result = LoadSheet(ws);
            if (result.Rows.Count > 0)
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
        using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        w.WriteLine(string.Join(",", columns.Select(c => $"\"{c}\"")));
        foreach (var row in rows)
            w.WriteLine(string.Join(",", columns.Select(c => $"\"{(row.Values.TryGetValue(c, out var v) ? v : "")}\"")));
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
        var txtPath = Path.Combine(daterDir, Path.GetFileNameWithoutExtension(path) + ".txt");
        SaveTxt(txtPath, columns, rows);
        return txtPath;
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



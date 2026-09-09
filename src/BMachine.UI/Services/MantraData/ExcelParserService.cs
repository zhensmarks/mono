using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;
using MiniExcelLibs;

namespace BMachine.UI.Services.MantraData;

public class ExcelParserService
{
    private static readonly string[] IndonesianMonthsFull = {
        "", "Januari", "Februari", "Maret", "April", "Mei", "Juni",
        "Juli", "Agustus", "September", "Oktober", "November", "Desember"
    };

    private static readonly string[] IndonesianMonthsShort = {
        "", "Jan", "Feb", "Mar", "Apr", "Mei", "Jun",
        "Jul", "Agu", "Sep", "Okt", "Nov", "Des"
    };

    public (List<string> Columns, List<TableDataRow> Rows) LoadExcel(string filePath, string? sheetName = null)
    {
        var columns = new List<string>();
        var rows = new List<TableDataRow>();

        var rawRows = MiniExcel.Query(filePath, useHeaderRow: true, sheetName: sheetName).ToList();
        if (rawRows.Count == 0) return (columns, rows);

        // Extract column headers from the first IDictionary row
        var firstRow = rawRows[0] as IDictionary<string, object>;
        if (firstRow != null)
        {
            columns = firstRow.Keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        }

        int rowNum = 1;
        foreach (var item in rawRows)
        {
            if (item is IDictionary<string, object> dict)
            {
                var dataRow = new TableDataRow { RowNumber = rowNum++ };
                foreach (var col in columns)
                {
                    var val = dict.TryGetValue(col, out var objVal) ? FormatValue(col, objVal) : string.Empty;
                    dataRow[col] = val;
                }
                rows.Add(dataRow);
            }
        }

        return (columns, rows);
    }

    public static string FormatValue(string columnName, object? rawValue)
    {
        if (rawValue == null) return string.Empty;

        // Check if value is a DateTime
        if (rawValue is DateTime dt)
        {
            return $"{dt.Day} {IndonesianMonthsFull[dt.Month]} {dt.Year}";
        }

        var s = rawValue.ToString()?.Trim() ?? string.Empty;

        // Check if column looks like phone and starts with 8
        if (columnName.Contains("HP", StringComparison.OrdinalIgnoreCase) || 
            columnName.Contains("TELP", StringComparison.OrdinalIgnoreCase) ||
            columnName.Contains("WA", StringComparison.OrdinalIgnoreCase))
        {
            if (s.StartsWith("8") && s.Length >= 9)
            {
                s = "0" + s;
            }
        }

        return s;
    }

    public static string FormatIndonesianDate(string input, string formatType = "Full")
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(input, new CultureInfo("id-ID"), DateTimeStyles.None, out dt))
        {
            return formatType switch
            {
                "Short" => $"{dt.Day} {IndonesianMonthsShort[dt.Month]} {dt.Year}",
                "Slash" => $"{dt.Day:D2}/{dt.Month:D2}/{dt.Year}",
                _ => $"{dt.Day} {IndonesianMonthsFull[dt.Month]} {dt.Year}"
            };
        }

        // Try regex match for e.g. "12/05/2007" or "2007-05-12"
        var match = Regex.Match(input, @"(\d{1,2})[\/\-\s](\d{1,2})[\/\-\s](\d{4})");
        if (match.Success)
        {
            int day = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);
            int year = int.Parse(match.Groups[3].Value);
            if (month >= 1 && month <= 12 && day >= 1 && day <= 31)
            {
                return formatType switch
                {
                    "Short" => $"{day} {IndonesianMonthsShort[month]} {year}",
                    "Slash" => $"{day:D2}/{month:D2}/{year}",
                    _ => $"{day} {IndonesianMonthsFull[month]} {year}"
                };
            }
        }

        return input;
    }

    public static string ToTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var ti = CultureInfo.CurrentCulture.TextInfo;
        return ti.ToTitleCase(text.ToLowerInvariant());
    }

    public static string FormatCellForExport(string? val, string delimiter = "\t")
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        var s = val;

        // data_yb parity: Bersihkan trailing 00:00:00 dari nilai tanggal/waktu
        if (s.Contains("00:00:00"))
        {
            s = s.Replace(" 00:00:00", "").Replace("T00:00:00", "");
        }

        // data_yb parity: Ganti baris baru (\r\n, \n, \r) dengan spasi tunggal agar tidak merusak baris tabel
        s = s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        // data_yb parity: Hapus/ganti kemunculan delimiter di dalam teks dengan spasi
        if (!string.IsNullOrEmpty(delimiter) && s.Contains(delimiter))
        {
            s = s.Replace(delimiter, " ");
        }

        return s.Trim();
    }

    public static string FormatCellForExcel(string? val)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        var s = val;

        // Bersihkan trailing 00:00:00 dari nilai tanggal/waktu
        if (s.Contains("00:00:00"))
        {
            s = s.Replace(" 00:00:00", "").Replace("T00:00:00", "");
        }

        // Standardisasi baris baru menjadi \r\n agar Excel membukanya dengan baris baru (multiline)
        s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        return s.Trim();
    }

    public string ExportToDater(string sourceFilePath, List<string> columns, List<TableDataRow> rows)
    {
        var sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(sourceDir)) sourceDir = Environment.CurrentDirectory;
        var daterDir = Path.Combine(sourceDir, "DATER");
        if (!Directory.Exists(daterDir)) Directory.CreateDirectory(daterDir);

        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        if (string.IsNullOrEmpty(baseName)) baseName = "export";

        // data_yb parity persis: <BaseName>-export.txt dan yb_process_data_table.json
        var txtPath = Path.Combine(daterDir, $"{baseName}-export.txt");
        var jsonPath = Path.Combine(daterDir, "yb_process_data_table.json");
        var xlsxPath = Path.Combine(daterDir, $"{baseName}-dater.xlsx");

        // 1. Export TAB Delimited TXT persis data_yb (UTF-8 tanpa BOM)
        using (var sw = new StreamWriter(txtPath, false, new System.Text.UTF8Encoding(false)))
        {
            sw.WriteLine(string.Join("\t", columns.Select(c => c.Trim())));
            foreach (var row in rows)
            {
                var values = columns.Select(c => FormatCellForExport(row[c], "\t"));
                sw.WriteLine(string.Join("\t", values));
            }
        }

        // 2. Export JSON Table untuk runner Photoshop (yb_process_data.jsx)
        var jsonPayload = new
        {
            header = columns.Select(c => c.Trim()).ToList(),
            rows = rows.Select(r => columns.ToDictionary(c => c.Trim(), c => FormatCellForExport(r[c], "\t"))).ToList()
        };
        var jsonStr = JsonSerializer.Serialize(jsonPayload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, jsonStr, new System.Text.UTF8Encoding(false));

        // 3. Export XLSX Salinan Data Bersih
        ExportToXlsx(xlsxPath, columns, rows);

        return txtPath;
    }

    public string ExportToTxt(string savePath, List<string> columns, List<TableDataRow> rows, string delimiter = "\t")
    {
        using var sw = new StreamWriter(savePath, false, new System.Text.UTF8Encoding(false));
        sw.WriteLine(string.Join(delimiter, columns.Select(c => c.Trim())));
        foreach (var row in rows)
        {
            var values = columns.Select(c => FormatCellForExport(row[c], delimiter));
            sw.WriteLine(string.Join(delimiter, values));
        }
        return savePath;
    }

    public string ExportToCsv(string savePath, List<string> columns, List<TableDataRow> rows)
    {
        using var sw = new StreamWriter(savePath, false, new System.Text.UTF8Encoding(true));
        sw.WriteLine(string.Join(",", columns.Select(CsvQuote)));
        foreach (var row in rows)
        {
            var values = columns.Select(c => CsvQuote(FormatCellForExport(row[c], ",")));
            sw.WriteLine(string.Join(",", values));
        }
        return savePath;
    }

    private static string CsvQuote(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public string ExportToXlsx(string savePath, List<string> columns, List<TableDataRow> rows)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        // Tulis Headers
        for (int c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F3F4F6");
        }

        // Tulis Rows
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < columns.Count; c++)
            {
                var val = FormatCellForExcel(row[columns[c]]);
                var cell = ws.Cell(r + 2, c + 1);
                cell.Value = val;

                // Jika sel mengandung baris baru, aktifkan WrapText = true agar Excel menampilkannya bertingkat
                if (val.Contains('\n'))
                {
                    cell.Style.Alignment.WrapText = true;
                }
            }
        }

        // Otomatis atur lebar kolom
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 100));

        // Jika ada multiline, sesuaikan tinggi baris data
        for (int r = 0; r < rows.Count; r++)
        {
            ws.Row(r + 2).AdjustToContents();
        }

        workbook.SaveAs(savePath);
        return savePath;
    }
}

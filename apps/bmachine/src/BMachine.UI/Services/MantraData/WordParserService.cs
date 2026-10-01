using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BMachine.UI.Services.MantraData;

public class WordParserService
{
    private const string CategoryColumn = "Kategori";
    private const string UnclassifiedColumn = "Catatan/Tidak Terklasifikasi";

    private static readonly Regex NameLine = new(
        @"^\s*(?:nama(?:\s+(?:lengkap|siswa|murid|peserta\s+didik|guru(?:\s*/?\s*tendik)?))?|student\s+name|name)\s*(?::|[-–—])?\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KnownFieldWithoutColon = new(
        @"^\s*(?<key>nisn|nis|nip|nik|nuptk|jk|gender|jenis\s+kelamin|kelas(?:\s*/\s*rombel)?|rombel|ttl|tempat\s+lahir(?:\s*/\s*tanggal)?|tempat\s+tanggal\s+lahir|tanggal\s+lahir|tgl\s+lahir|alamat|jabatan|agama)\s+(?<value>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public (List<string> Columns, List<TableDataRow> Rows) LoadWordDocx(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File DOCX tidak ditemukan.", filePath);

        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) throw new InvalidDataException("Dokumen Word tidak memiliki isi yang dapat dibaca.");

        var columns = new List<string> { CategoryColumn, UnclassifiedColumn };
        var rows = new List<TableDataRow>();
        string currentCategory = "TIDAK DIKETAHUI";
        TableDataRow? currentRow = null;
        int rowNumber = 1;

        void FlushCurrent()
        {
            if (currentRow != null)
            {
                rows.Add(currentRow);
                currentRow = null;
            }
        }

        TableDataRow NewRow(string category)
        {
            var row = new TableDataRow { RowNumber = rowNumber++ };
            row[CategoryColumn] = category;
            return row;
        }

        void PreserveUnclassified(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var row = NewRow("TIDAK TERKLASIFIKASI");
            row[UnclassifiedColumn] = text.Trim();
            rows.Add(row);
        }

        foreach (var child in body.ChildElements)
        {
            if (child is Paragraph paragraph)
            {
                var text = paragraph.InnerText?.Trim() ?? string.Empty;
                if (text.Length == 0) continue;

                if (TryGetCategory(text, out var category))
                {
                    FlushCurrent();
                    currentCategory = category;
                    continue;
                }

                var nameMatch = NameLine.Match(text);
                if (nameMatch.Success)
                {
                    FlushCurrent();
                    var name = nameMatch.Groups["value"].Value.Trim();
                    if (name.Length == 0)
                    {
                        PreserveUnclassified($"Label nama kosong: {text}");
                        continue;
                    }

                    if (!columns.Contains("NAMA", StringComparer.OrdinalIgnoreCase)) columns.Add("NAMA");
                    currentRow = NewRow(currentCategory);
                    currentRow["NAMA"] = name;
                    continue;
                }

                if (TryParseKnownField(text, out var field, out var value))
                {
                    if (currentRow == null)
                    {
                        PreserveUnclassified(text);
                        continue;
                    }

                    if (!columns.Contains(field, StringComparer.OrdinalIgnoreCase)) columns.Add(field);
                    currentRow[field] = value;
                    continue;
                }

                // Free text, headings, and trailing notes are retained separately;
                // never attach them to whichever student happened to precede them.
                FlushCurrent();
                PreserveUnclassified(text);
                continue;
            }

            if (child is Table table)
            {
                FlushCurrent();
                ImportTable(table, columns, rows, ref rowNumber, PreserveUnclassified);
            }
        }

        FlushCurrent();
        if (rows.Count == 0)
            throw new InvalidDataException("Tidak ditemukan baris data atau tabel berisi catatan yang dapat diimpor. Pastikan dokumen memiliki tabel dengan baris data atau label Nama/field yang dapat dikenali.");

        return (columns, rows);
    }

    private static bool TryGetCategory(string text, out string category)
    {
        var normalized = Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        switch (normalized)
        {
            case "siswa":
            case "murid":
            case "peserta didik":
            case "student":
            case "students":
                category = "SISWA";
                return true;
            case "guru":
            case "staff":
            case "staf":
            case "teacher":
            case "tenaga pendidik":
            case "guru dan tenaga kependidikan":
                category = "GURU";
                return true;
            case "kepsek":
            case "kepala sekolah":
            case "principal":
                category = "KEPSEK";
                return true;
            default:
                category = string.Empty;
                return false;
        }
    }

    private static bool TryParseKnownField(string text, out string field, out string value)
    {
        field = string.Empty;
        value = string.Empty;
        string key;
        string rawValue;

        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            key = text[..colon].Trim();
            rawValue = text[(colon + 1)..].Trim();
        }
        else
        {
            var match = KnownFieldWithoutColon.Match(text);
            if (!match.Success) return false;
            key = match.Groups["key"].Value.Trim();
            rawValue = match.Groups["value"].Value.Trim();
        }

        var normalized = Regex.Replace(key.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        field = normalized switch
        {
            "nisn" or "nis" => "NISN",
            "nip" or "nuptk" => "NIP",
            "nik" => "NIK",
            "jk" or "gender" or "jenis kelamin" => "JK",
            "kelas" or "kelas rombel" or "rombel" => "KELAS/ROMBEL",
            "ttl" or "tempat tanggal lahir" or "tempat lahir tanggal" => "TTL",
            "tempat lahir" => "TEMPAT LAHIR",
            "tanggal lahir" or "tgl lahir" => "TGL LAHIR",
            "alamat" => "ALAMAT",
            "jabatan" => "JABATAN",
            "agama" => "AGAMA",
            _ => string.Empty
        };

        if (field.Length == 0) return false;
        value = rawValue;
        return true;
    }

    private static void ImportTable(
        Table table,
        List<string> columns,
        List<TableDataRow> rows,
        ref int rowNumber,
        Action<string> preserveUnclassified)
    {
        var matrix = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>()
                .Select(cell => cell.InnerText?.Trim() ?? string.Empty)
                .ToList())
            .ToList();
        while (matrix.Count > 0 && matrix[^1].All(string.IsNullOrWhiteSpace)) matrix.RemoveAt(matrix.Count - 1);
        if (matrix.Count == 0) return;

        int width = matrix.Max(row => row.Count);
        int headerIndex = -1;
        int bestRecognized = 0;
        for (int i = 0; i < matrix.Count; i++)
        {
            int recognized = matrix[i].Count(value => IsRecognizedHeader(value));
            if (recognized >= 2)
            {
                headerIndex = i;
                break;
            }
            bestRecognized = Math.Max(bestRecognized, recognized);
        }

        // Word tables are explicitly tabular. If no recognizable heading exists,
        // use the first row as headings and retain every following row.
        if (headerIndex < 0) headerIndex = 0;
        for (int i = 0; i < headerIndex; i++)
        {
            var preamble = string.Join(" | ", matrix[i].Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(preamble)) preserveUnclassified($"Teks sebelum header tabel: {preamble}");
        }

        var headers = UniqueHeaders(matrix[headerIndex], width);
        foreach (var header in headers)
            if (!columns.Contains(header, StringComparer.OrdinalIgnoreCase)) columns.Add(header);

        for (int i = headerIndex + 1; i < matrix.Count; i++)
        {
            if (matrix[i].All(string.IsNullOrWhiteSpace)) continue;
            var row = new TableDataRow { RowNumber = rowNumber++ };
            row[CategoryColumn] = "TIDAK DIKETAHUI";
            for (int col = 0; col < headers.Count; col++)
                row[headers[col]] = col < matrix[i].Count ? matrix[i][col] : string.Empty;
            rows.Add(row);
        }
    }

    private static bool IsRecognizedHeader(string value)
    {
        var key = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        return key is "nama" or "nama lengkap" or "nama siswa" or "nama peserta didik" or "name" or "student name" or
            "nomor" or "no" or "nis" or "nisn" or "nip" or "kelas" or "rombel" or "kelas rombel" or
            "jk" or "gender" or "jenis kelamin" or "alamat" or "ttl" or "tempat lahir" or "tanggal lahir" or "jabatan";
    }

    private static List<string> UniqueHeaders(IReadOnlyList<string> rawHeaders, int width)
    {
        var headers = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < width; i++)
        {
            var name = i < rawHeaders.Count ? rawHeaders[i].Trim() : string.Empty;
            if (name.Length == 0) name = $"Kolom {i + 1}";
            else name = NormalizeTableHeader(name);
            counts.TryGetValue(name, out int count);
            counts[name] = count + 1;
            headers.Add(count == 0 ? name : $"{name} ({count + 1})");
        }
        return headers;
    }

    private static string NormalizeTableHeader(string header)
    {
        var key = Regex.Replace(header.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        return key switch
        {
            "nama" or "nama lengkap" or "nama siswa" or "nama peserta didik" or "name" or "student name" => "NAMA",
            "nisn" => "NISN",
            "nis" => "NIS",
            "nip" => "NIP",
            "jk" or "gender" or "jenis kelamin" => "JK",
            "kelas" or "rombel" or "kelas rombel" => "KELAS/ROMBEL",
            "ttl" or "tempat tanggal lahir" => "TTL",
            "alamat" or "address" => "ALAMAT",
            "jabatan" => "JABATAN",
            _ => header.Trim()
        };
    }
}

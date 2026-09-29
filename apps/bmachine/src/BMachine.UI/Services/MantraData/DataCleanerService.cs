using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public class DataCleaningSuggestion
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool Applicable { get; set; } = true;
}

public record CleanerDialogResult(bool Applied, List<string> SelectedCodes);

public static class DataCleanerService
{
    private static readonly Regex WhitespaceRunRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex ClassMarkerRegex = new(@"^\s*KELAS\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CleanSpacesPreserveNewlines(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var lines = value.Replace("\r", "\n").Split('\n');
        var cleaned = lines.Select(l => WhitespaceRunRegex.Replace(l.Trim(), " ")).ToList();
        return string.Join("\n", cleaned).Trim();
    }

    public static bool IsRepeatedHeader(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        int matches = 0, nonempty = 0;
        int len = Math.Min(row.Count, headers.Count);
        for (int i = 0; i < len; i++)
        {
            if (string.IsNullOrWhiteSpace(row[i])) continue;
            nonempty++;
            if (YearbookLayoutService.CanonicalName(row[i]) == YearbookLayoutService.CanonicalName(headers[i]) ||
                string.Equals(row[i].Trim(), headers[i].Trim(), StringComparison.OrdinalIgnoreCase))
                matches++;
        }
        return nonempty >= 2 && (double)matches / nonempty >= 0.7;
    }

    public static List<DataCleaningSuggestion> AnalyzeSheet(
        IReadOnlyList<string> headers,
        IList<TableDataRow> rows)
    {
        var suggestions = new List<DataCleaningSuggestion>();

        int whitespace = 0;
        foreach (var row in rows)
        {
            foreach (var col in headers)
            {
                var val = row[col];
                if (!string.IsNullOrEmpty(val) && CleanSpacesPreserveNewlines(val) != val)
                    whitespace++;
            }
        }
        if (whitespace > 0)
        {
            suggestions.Add(new DataCleaningSuggestion
            {
                Code = "trim_spaces",
                Title = "Rapikan spasi",
                Detail = $"{whitespace} sel memiliki spasi berlebih. Baris baru tetap dipertahankan.",
                Count = whitespace
            });
        }

        var rowLists = rows.Select(r => headers.Select(h => r[h]).ToList()).ToList();
        int repeated = rowLists.Count(r => IsRepeatedHeader(r, headers));
        if (repeated > 0)
        {
            suggestions.Add(new DataCleaningSuggestion
            {
                Code = "remove_repeated_headers",
                Title = "Hapus header berulang",
                Detail = $"{repeated} baris di tengah data menyerupai header tabel.",
                Count = repeated
            });
        }

        var emptyColumns = new List<string>();
        foreach (var col in headers)
        {
            if (rows.All(r => string.IsNullOrWhiteSpace(r[col])))
                emptyColumns.Add(col);
        }
        if (emptyColumns.Count > 0)
        {
            suggestions.Add(new DataCleaningSuggestion
            {
                Code = "drop_empty_columns",
                Title = "Hapus kolom kosong",
                Detail = $"Kolom tanpa isi: {string.Join(", ", emptyColumns)}.",
                Count = emptyColumns.Count
            });
        }

        var genderCol = headers.FirstOrDefault(h =>
            string.Equals(YearbookLayoutService.CanonicalName(h), "JK", StringComparison.OrdinalIgnoreCase));
        if (genderCol != null)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "laki-laki", "laki laki", "pria", "male", "laki", "cowok",
                "perempuan", "wanita", "female", "cewek"
            };
            int genderCount = rows.Count(r =>
                aliases.Contains(r[genderCol]?.Trim() ?? ""));
            if (genderCount > 0)
            {
                suggestions.Add(new DataCleaningSuggestion
                {
                    Code = "normalize_gender",
                    Title = "Normalisasi jenis kelamin",
                    Detail = $"{genderCount} nilai dapat diubah aman menjadi L/P.",
                    Count = genderCount
                });
            }
        }

        var nisnCol = headers.FirstOrDefault(h =>
            string.Equals(YearbookLayoutService.CanonicalName(h), "NISN", StringComparison.OrdinalIgnoreCase));
        if (nisnCol != null)
        {
            var suspicious = new List<(int Row, string Value)>();
            for (int i = 0; i < rows.Count; i++)
            {
                var val = rows[i][nisnCol]?.Trim() ?? "";
                var digits = Regex.Replace(val, @"\D", "");
                if (!string.IsNullOrEmpty(val) && digits.Length != 10)
                    suspicious.Add((i + 2, val));
            }
            if (suspicious.Count > 0)
            {
                var examples = string.Join(", ", suspicious.Take(4).Select(s => $"baris {s.Row}: {s.Value}"));
                suggestions.Add(new DataCleaningSuggestion
                {
                    Code = "warn_nisn",
                    Title = "Periksa NISN",
                    Detail = $"{suspicious.Count} NISN tidak berjumlah 10 digit ({examples}). Tidak akan diubah otomatis.",
                    Count = suspicious.Count,
                    Applicable = false
                });
            }
        }

        int classMarkers = rows.Count(r =>
            r[headers.FirstOrDefault() ?? ""] is string f && ClassMarkerRegex.IsMatch(f));
        if (classMarkers > 0)
        {
            suggestions.Add(new DataCleaningSuggestion
            {
                Code = "warn_class_sections",
                Title = "Terdeteksi beberapa bagian kelas",
                Detail = $"Ada {classMarkers} penanda kelas. Gunakan sebagai petunjuk pemisahan sheet; periksa preview sebelum membagi data.",
                Count = classMarkers,
                Applicable = false
            });
        }

        return suggestions;
    }

    public static void ApplySuggestions(
        IList<string> headers,
        IList<TableDataRow> rows,
        IReadOnlyCollection<string> codes)
    {
        var selected = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

        if (selected.Contains("trim_spaces"))
        {
            foreach (var row in rows)
                foreach (var col in headers)
                    row[col] = CleanSpacesPreserveNewlines(row[col] ?? "");
        }

        if (selected.Contains("remove_repeated_headers"))
        {
            var toRemove = rows.Where(r =>
            {
                var vals = headers.Select(h => r[h]).ToList();
                return IsRepeatedHeader(vals, headers.ToList());
            }).ToList();
            foreach (var row in toRemove)
                rows.Remove(row);
        }

        if (selected.Contains("drop_empty_columns"))
        {
            var emptyCols = headers.Where(col => rows.All(r => string.IsNullOrWhiteSpace(r[col]))).ToList();
            foreach (var col in emptyCols)
            {
                headers.Remove(col);
                foreach (var row in rows)
                    row.Values.Remove(col);
            }
        }

        if (selected.Contains("normalize_gender"))
        {
            var genderCol = headers.FirstOrDefault(h =>
                string.Equals(YearbookLayoutService.CanonicalName(h), "JK", StringComparison.OrdinalIgnoreCase));
            if (genderCol != null)
            {
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["laki-laki"] = "L", ["laki laki"] = "L", ["pria"] = "L", ["male"] = "L", ["laki"] = "L", ["cowok"] = "L",
                    ["perempuan"] = "P", ["wanita"] = "P", ["female"] = "P", ["cewek"] = "P"
                };
                foreach (var row in rows)
                {
                    var key = (row[genderCol] ?? "").Trim();
                    if (aliases.TryGetValue(key, out var val))
                        row[genderCol] = val;
                }
            }
        }
    }
}

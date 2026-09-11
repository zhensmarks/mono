using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public class TransformService
{
    public List<TransformPreset> GetAvailablePresets(List<string> headers)
    {
        var presets = new List<TransformPreset>
        {
            new TransformPreset
            {
                Code = "custom",
                Title = "Atur sendiri",
                Description = "Anda menentukan kolom, urutan, prefix, dan pemisah. Asisten hanya menyusun dan memproses pilihan tersebut.",
                Recommended = true
            }
        };

        var headerMap = headers.Select((h, i) => (Header: h, Index: i))
            .ToDictionary(x => CleanHeader(x.Header), x => x.Index, StringComparer.OrdinalIgnoreCase);

        bool hasName = headerMap.ContainsKey("nama");
        bool hasNisn = headerMap.ContainsKey("nisn");
        bool hasAddress = headerMap.ContainsKey("alamat");
        bool hasPlace = headerMap.ContainsKey("tempat lahir");
        bool hasDate = headerMap.ContainsKey("tanggal lahir");
        bool hasCombinedTTL = headerMap.ContainsKey("tempat, tanggal lahir");
        bool hasTTL = hasCombinedTTL || (hasPlace && hasDate);

        if (hasName && hasNisn && hasAddress && hasTTL)
        {
            presets.Add(new TransformPreset
            {
                Code = "student_data",
                Title = "Nama + DATA siswa",
                Description = "Gabungkan NISN, TTL, alamat, RT/RW, kelurahan, kecamatan, dan kode pos menjadi DATA multiline.",
                Recommended = false
            });
        }

        if (hasPlace && hasDate)
        {
            presets.Add(new TransformPreset
            {
                Code = "merge_ttl",
                Title = "Gabungkan tempat dan tanggal lahir",
                Description = "Membuat kolom Tempat, Tanggal Lahir tanpa mengubah kedua kolom sumber.",
                Recommended = false
            });
        }

        if (hasName && headerMap.ContainsKey("jabatan") && hasTTL)
        {
            presets.Add(new TransformPreset
            {
                Code = "guru_oke2",
                Title = "Photoshop guru: NAMA + DATA",
                Description = "Membuat NAMA serta DATA berisi Jabatan dan TTL.",
                Recommended = false
            });
        }

        if (hasName && hasNisn)
        {
            presets.Add(new TransformPreset
            {
                Code = "nisn_name",
                Title = "Ambil NISN + NAMA",
                Description = "Menyisakan dua kolom identitas tanpa mengubah NISN atau menghapus nol awal.",
                Recommended = false
            });
        }

        return presets;
    }

    public (List<string> Headers, List<TableDataRow> Rows) ApplyPreset(
        string presetCode,
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows)
    {
        var headerMap = sourceHeaders.Select((h, i) => (Header: h, Index: i))
            .ToDictionary(x => CleanHeader(x.Header), x => (x.Index, x.Header), StringComparer.OrdinalIgnoreCase);

        switch (presetCode)
        {
            case "student_data":
                return ApplyStudentDataPreset(sourceHeaders, sourceRows, headerMap);

            case "merge_ttl":
                return ApplyMergeTTLPreset(sourceHeaders, sourceRows, headerMap);

            case "guru_oke2":
                return ApplyGuruOke2Preset(sourceHeaders, sourceRows, headerMap);

            case "nisn_name":
                return ApplyNisnNamePreset(sourceHeaders, sourceRows, headerMap);

            default:
                throw new ArgumentException($"Preset tidak dikenal: {presetCode}");
        }
    }

    private (List<string>, List<TableDataRow>) ApplyStudentDataPreset(
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows,
        Dictionary<string, (int Index, string Header)> headerMap)
    {
        var newHeaders = new List<string> { "Nama", "DATA" };
        var newRows = new List<TableDataRow>();
        int rowNum = 1;

        foreach (var sourceRow in sourceRows)
        {
            var name = GetValue(sourceRow, headerMap, "nama");
            var nisn = GetValue(sourceRow, headerMap, "nisn");
            var ttl = GetTTL(sourceRow, headerMap);

            var addressParts = new List<string>();
            foreach (var (key, prefix) in new[]
            {
                ("alamat", ""), ("rt", "RT "), ("rw", "RW "),
                ("kelurahan", ""), ("kecamatan", ""), ("kode pos", "")
            })
            {
                var value = GetValue(sourceRow, headerMap, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (key == "rt" || key == "rw")
                        value = TwoDigits(value);
                    addressParts.Add(prefix + value);
                }
            }

            var dataParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(nisn)) dataParts.Add(nisn);
            if (!string.IsNullOrWhiteSpace(ttl)) dataParts.Add(ttl);
            if (addressParts.Count > 0) dataParts.Add(string.Join(", ", addressParts));

            var data = string.Join("\n", dataParts);

            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(data))
            {
                var row = new TableDataRow { RowNumber = rowNum++ };
                row["Nama"] = name;
                row["DATA"] = data;
                newRows.Add(row);
            }
        }

        return (newHeaders, newRows);
    }

    private (List<string>, List<TableDataRow>) ApplyMergeTTLPreset(
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows,
        Dictionary<string, (int Index, string Header)> headerMap)
    {
        var newHeaders = new List<string>(sourceHeaders) { "Tempat, Tanggal Lahir" };
        var newRows = new List<TableDataRow>();
        int rowNum = 1;

        foreach (var sourceRow in sourceRows)
        {
            var row = new TableDataRow { RowNumber = rowNum++ };
            foreach (var header in sourceHeaders)
            {
                row[header] = sourceRow.Values.ContainsKey(header) ? sourceRow[header] : "";
            }
            row["Tempat, Tanggal Lahir"] = GetTTL(sourceRow, headerMap);
            newRows.Add(row);
        }

        return (newHeaders, newRows);
    }

    private (List<string>, List<TableDataRow>) ApplyGuruOke2Preset(
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows,
        Dictionary<string, (int Index, string Header)> headerMap)
    {
        var newHeaders = new List<string> { "NAMA", "DATA" };
        var newRows = new List<TableDataRow>();
        int rowNum = 1;

        foreach (var sourceRow in sourceRows)
        {
            var name = GetValue(sourceRow, headerMap, "nama");
            var role = GetValue(sourceRow, headerMap, "jabatan");
            var ttl = GetTTL(sourceRow, headerMap);

            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(ttl))
            {
                var row = new TableDataRow { RowNumber = rowNum++ };
                row["NAMA"] = name;
                row["DATA"] = $"Jabatan : {role}\nTTL : {ttl}";
                newRows.Add(row);
            }
        }

        return (newHeaders, newRows);
    }

    private (List<string>, List<TableDataRow>) ApplyNisnNamePreset(
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows,
        Dictionary<string, (int Index, string Header)> headerMap)
    {
        var newHeaders = new List<string> { "NISN", "NAMA" };
        var newRows = new List<TableDataRow>();
        int rowNum = 1;

        foreach (var sourceRow in sourceRows)
        {
            var nisn = GetValue(sourceRow, headerMap, "nisn");
            var name = GetValue(sourceRow, headerMap, "nama");

            // Skip header-like rows
            if (nisn.Equals("nisn", StringComparison.OrdinalIgnoreCase) &&
                (name.Equals("nama", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("nama lengkap", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!string.IsNullOrWhiteSpace(nisn) || !string.IsNullOrWhiteSpace(name))
            {
                var row = new TableDataRow { RowNumber = rowNum++ };
                row["NISN"] = nisn;
                row["NAMA"] = name;
                newRows.Add(row);
            }
        }

        return (newHeaders, newRows);
    }

    public (List<string> Headers, List<TableDataRow> Rows) BuildCustomMerge(
        List<string> sourceHeaders,
        List<TableDataRow> sourceRows,
        List<MergeColumn> columns,
        string targetHeader,
        string defaultSeparator,
        bool keepSources)
    {
        if (columns.Count == 0)
            throw new ArgumentException("Pilih minimal satu kolom sumber.");

        if (string.IsNullOrWhiteSpace(targetHeader))
            throw new ArgumentException("Nama header hasil belum diisi.");

        var newHeaders = keepSources
            ? new List<string>(sourceHeaders) { targetHeader.Trim() }
            : new List<string> { targetHeader.Trim() };

        var newRows = new List<TableDataRow>();
        int rowNum = 1;

        foreach (var sourceRow in sourceRows)
        {
            var parts = new List<(string Value, string Separator)>();

            foreach (var col in columns)
            {
                var value = sourceRow.Values.ContainsKey(sourceHeaders[col.SourceIndex])
                    ? sourceRow[sourceHeaders[col.SourceIndex]].Trim()
                    : "";

                if (!string.IsNullOrWhiteSpace(value))
                {
                    var separator = string.IsNullOrEmpty(col.Separator) ? defaultSeparator : col.Separator;
                    parts.Add((col.Prefix + value, separator));
                }
            }

            var merged = string.Join("", parts.Select((p, i) =>
                p.Value + (i < parts.Count - 1 ? p.Separator : "")));

            var row = new TableDataRow { RowNumber = rowNum++ };
            if (keepSources)
            {
                foreach (var header in sourceHeaders)
                {
                    row[header] = sourceRow.Values.ContainsKey(header) ? sourceRow[header] : "";
                }
            }
            row[targetHeader.Trim()] = merged;
            newRows.Add(row);
        }

        return (newHeaders, newRows);
    }

    private string GetValue(TableDataRow row, Dictionary<string, (int Index, string Header)> headerMap, string key)
    {
        if (headerMap.TryGetValue(key, out var mapping))
        {
            return row.Values.ContainsKey(mapping.Header) ? row[mapping.Header].Trim() : "";
        }
        return "";
    }

    private string GetTTL(TableDataRow row, Dictionary<string, (int Index, string Header)> headerMap)
    {
        var combined = GetValue(row, headerMap, "tempat, tanggal lahir");
        if (!string.IsNullOrWhiteSpace(combined))
            return combined;

        var place = GetValue(row, headerMap, "tempat lahir");
        var date = GetValue(row, headerMap, "tanggal lahir");

        if (!string.IsNullOrWhiteSpace(place) && string.IsNullOrWhiteSpace(date))
            return place;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(place)) parts.Add(place);
        if (!string.IsNullOrWhiteSpace(date)) parts.Add(date);

        return string.Join(", ", parts);
    }

    private string TwoDigits(string value)
    {
        if (int.TryParse(value.Trim(), out int num) && num >= 0 && num < 100)
            return num.ToString("D2");
        return value;
    }

    private string CleanHeader(string value)
    {
        return Regex.Replace(value?.ToLowerInvariant() ?? "", @"[^a-z0-9]+", " ").Trim();
    }
}

public class TransformPreset
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Recommended { get; set; }
}

public class MergeColumn
{
    public int SourceIndex { get; set; }
    public string Prefix { get; set; } = "";
    public string Separator { get; set; } = "";
}

public record TransformDialogResult(
    bool Confirmed,
    string PresetCode,
    string TargetHeader,
    string Separator,
    bool KeepSources,
    List<MergeColumn> MergeColumns);
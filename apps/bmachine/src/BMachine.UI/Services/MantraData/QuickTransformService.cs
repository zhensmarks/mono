using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public enum QuickTransformKind
{
    Gender,
    IndonesianDate
}

public readonly record struct QuickTransformResult(int ChangedCount, int SkippedCount);

public readonly record struct QuickTransformColumnResolution(string? Column, bool IsAmbiguous);

/// <summary>Strict, non-destructive value transforms for common MantraData columns.</summary>
public static class QuickTransformService
{
    private static readonly string[] IndonesianMonths =
    {
        "Januari", "Februari", "Maret", "April", "Mei", "Juni",
        "Juli", "Agustus", "September", "Oktober", "November", "Desember"
    };

    private static readonly Regex IndonesianDatePattern = new(
        @"^\d{1,2}\s+(Januari|Februari|Maret|April|Mei|Juni|Juli|Agustus|September|Oktober|November|Desember)\s+\d{4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Maps only exact L/P codes after trimming outer whitespace, case-insensitively.</summary>
    public static string? NormalizeGenderValue(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, "L", StringComparison.OrdinalIgnoreCase))
            return "Laki-Laki";
        if (string.Equals(normalized, "P", StringComparison.OrdinalIgnoreCase))
            return "Perempuan";
        return null;
    }

    /// <summary>Formats only valid dd-MM-yyyy dates using fixed Indonesian month names.</summary>
    public static string? FormatIndonesianDateValue(string? value)
    {
        var normalized = value?.Trim();
        if (!DateOnly.TryParseExact(
                normalized,
                "dd-MM-yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            return null;

        return $"{date.Day.ToString(CultureInfo.InvariantCulture)} {IndonesianMonths[date.Month - 1]} {date.Year.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Prefer the active column when it has the right kind of header or values. Otherwise use a
    /// unique semantic header, then a unique column with transformable (or already transformed)
    /// values. Return ambiguity rather than guessing between multiple candidates.
    /// </summary>
    public static QuickTransformColumnResolution ResolveTargetColumn(
        IEnumerable<string> headers,
        IEnumerable<TableDataRow> rows,
        string? selectedColumn,
        QuickTransformKind kind)
    {
        var columns = headers.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        var data = rows.ToList();

        if (!string.IsNullOrWhiteSpace(selectedColumn) && columns.Contains(selectedColumn))
        {
            var selectedValues = data.Select(row => row[selectedColumn]).ToList();
            if (HasSemanticHeader(selectedColumn, kind) || selectedValues.Any(value => IsCandidateValue(value, kind)))
                return new QuickTransformColumnResolution(selectedColumn, false);
        }

        var headerCandidates = columns.Where(header => HasSemanticHeader(header, kind)).ToList();
        if (headerCandidates.Count == 1)
            return new QuickTransformColumnResolution(headerCandidates[0], false);
        if (headerCandidates.Count > 1)
            return new QuickTransformColumnResolution(null, true);

        var valueCandidates = columns.Where(header =>
            data.Any(row => IsCandidateValue(row[header], kind))).ToList();
        if (valueCandidates.Count == 1)
            return new QuickTransformColumnResolution(valueCandidates[0], false);

        return new QuickTransformColumnResolution(null, valueCandidates.Count > 1);
    }

    private static bool HasSemanticHeader(string header, QuickTransformKind kind)
    {
        var normalized = Regex.Replace(header.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        return kind switch
        {
            QuickTransformKind.Gender => normalized is "jk" or "sex" or "gender" or "jk gender" or "jk sex" ||
                normalized.Contains("kelamin") || normalized.EndsWith(" gender", StringComparison.Ordinal),
            QuickTransformKind.IndonesianDate => normalized is "date" or "birth date" ||
                normalized.Contains("tanggal") || normalized.Contains("tgl") || normalized.Contains("birth"),
            _ => false
        };
    }

    private static bool IsCandidateValue(string? value, QuickTransformKind kind)
    {
        if (kind == QuickTransformKind.Gender)
        {
            var normalized = value?.Trim();
            return NormalizeGenderValue(value) != null ||
                   string.Equals(normalized, "Laki-Laki", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Perempuan", StringComparison.OrdinalIgnoreCase);
        }

        return FormatIndonesianDateValue(value) != null ||
               (!string.IsNullOrWhiteSpace(value) && IndonesianDatePattern.IsMatch(value.Trim()));
    }
}

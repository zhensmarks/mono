using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public static class YearbookLayoutService
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NO"] = new[] { "no", "nomor", "no urut", "no.", "nr" },
        ["NAMA"] = new[] { "nama", "nama siswa", "nama lengkap", "name", "nama murid" },
        ["NIS"] = new[] { "nis", "nisn", "no induk", "nomor induk", "no. induk" },
        ["NIP"] = new[] { "nip", "nuptk", "niy" },
        ["KELAS"] = new[] { "kelas", "rombel", "class", "ruang" },
        ["JK"] = new[] { "jk", "gender", "jenis kelamin", "l/p", "lp" },
        ["TEMPAT LAHIR"] = new[] { "tempat lahir", "tempat", "kota lahir", "pob" },
        ["TGL LAHIR"] = new[] { "tgl lahir", "tanggal lahir", "tgl", "dob", "tgl. lahir" },
        ["TTL"] = new[] { "ttl", "tempat tanggal lahir", "tempat, tanggal lahir" },
        ["ALAMAT"] = new[] { "alamat", "address", "alamat lengkap" },
        ["HP"] = new[] { "hp", "telp", "telepon", "wa", "no hp", "no. hp", "whatsapp", "no wa" },
        ["JABATAN"] = new[] { "jabatan", "posisi", "job", "tugas" },
        ["QUOTE"] = new[] { "quote", "quotes", "kata mutiara", "motto", "pesan" },
        ["GOL DARAH"] = new[] { "gol darah", "golongan darah", "blood" },
        ["AGAMA"] = new[] { "agama", "religion" }
    };

    public static IReadOnlyList<string> ColumnsFor(DataJobKind kind) => kind switch
    {
        DataJobKind.YearbookStudent => new[]
        {
            "NO", "NAMA", "NIS", "KELAS", "JK", "TEMPAT LAHIR", "TGL LAHIR", "TTL", "ALAMAT", "HP", "QUOTE"
        },
        DataJobKind.YearbookTeacher => new[]
        {
            "NO", "NAMA", "NIP", "JABATAN", "TEMPAT LAHIR", "TGL LAHIR", "TTL", "ALAMAT", "HP"
        },
        DataJobKind.IdCardStudent => new[]
        {
            "NAMA", "NIS", "KELAS", "TTL", "ALAMAT"
        },
        DataJobKind.IdCardStaff => new[]
        {
            "NAMA", "NIP", "JABATAN", "TTL", "ALAMAT"
        },
        _ => Array.Empty<string>()
    };

    public enum FieldRole
    {
        Unknown, Name, Serial, Class, Gender, BirthPlace, BirthDate, Ttl, Address, Phone, Job, Quote, Blood, Religion
    }

    public static FieldRole RoleOf(string header)
    {
        return CanonicalName(header) switch
        {
            "NAMA" => FieldRole.Name,
            "NO" or "NIS" or "NIP" => FieldRole.Serial,
            "KELAS" => FieldRole.Class,
            "JK" => FieldRole.Gender,
            "TEMPAT LAHIR" => FieldRole.BirthPlace,
            "TGL LAHIR" => FieldRole.BirthDate,
            "TTL" => FieldRole.Ttl,
            "ALAMAT" => FieldRole.Address,
            "HP" => FieldRole.Phone,
            "JABATAN" => FieldRole.Job,
            "QUOTE" => FieldRole.Quote,
            "GOL DARAH" => FieldRole.Blood,
            "AGAMA" => FieldRole.Religion,
            _ => GuessRole(header)
        };
    }

    public static string SmartClean(string column, string value)
    {
        var v = TrimCell(value);
        if (v.Length == 0) return string.Empty;

        return RoleOf(column) switch
        {
            FieldRole.Name or FieldRole.BirthPlace or FieldRole.Address or FieldRole.Job or FieldRole.Religion
                => ExcelParserService.ToTitleCase(v),
            FieldRole.Quote => TrimCell(value),
            FieldRole.Gender => NormalizeGender(v),
            FieldRole.Phone => CleanPhone(v),
            FieldRole.BirthDate => ExcelParserService.FormatIndonesianDate(v, "Full"),
            FieldRole.Ttl => FormatTtl(v),
            FieldRole.Class => NormalizeKelas(v),
            FieldRole.Serial => CleanSerial(v),
            FieldRole.Blood => v.Replace(" ", "").ToUpperInvariant(),
            _ => v
        };
    }

    public static string CleanPhone(string raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;

        s = Regex.Replace(s, @"[\s\-\(\)\.]", "");
        if (s.StartsWith("+62", StringComparison.Ordinal)) s = "0" + s[3..];
        else if (s.StartsWith("62", StringComparison.Ordinal) && s.Length >= 11) s = "0" + s[2..];
        if (s.StartsWith("8") && s.Length >= 9 && s.Length <= 15 && s.All(char.IsDigit))
            s = "0" + s;
        return s;
    }

    public static string FormatTtl(string ttl)
    {
        var (tempat, tgl) = SplitTtl(ttl);
        if (string.IsNullOrEmpty(tgl))
        {
            var formatted = ExcelParserService.FormatIndonesianDate(tempat, "Full");
            return formatted;
        }

        var place = ExcelParserService.ToTitleCase(tempat);
        var date = ExcelParserService.FormatIndonesianDate(tgl, "Full");
        if (string.IsNullOrEmpty(place)) return date;
        return $"{place}, {date}";
    }

    public static string NormalizeKelas(string raw)
    {
        var s = TrimCell(raw);
        if (s.Length == 0) return string.Empty;
        s = s.ToUpperInvariant();
        s = s.Replace("KELAS ", "").Replace("KELAS", "");
        return TrimCell(s);
    }

    public static string CleanSerial(string raw)
    {
        var s = TrimCell(raw);
        if (s.Length == 0) return string.Empty;
        if (Regex.IsMatch(s, @"^\d+\.0+$"))
            return s[..s.IndexOf('.')];
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            Math.Abs(n - Math.Truncate(n)) < 0.0000001 && n >= 0 && n < 1_000_000_000_000)
        {
            return Math.Truncate(n).ToString("0", CultureInfo.InvariantCulture);
        }
        return s;
    }

    public static string TrimCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string SuggestedCleanLabel(string? column)
    {
        if (string.IsNullOrEmpty(column)) return "Rapikan otomatis";
        return RoleOf(column) switch
        {
            FieldRole.Name => $"Rapikan nama di {column}",
            FieldRole.Phone => $"Rapikan nomor HP di {column}",
            FieldRole.Ttl => $"Rapikan TTL di {column}",
            FieldRole.BirthDate => $"Format tanggal di {column}",
            FieldRole.Gender => $"JK ke L / P di {column}",
            FieldRole.Class => $"Rapikan kelas di {column}",
            FieldRole.Serial => $"Rapikan nomor di {column}",
            _ => $"Rapikan {column}"
        };
    }

    private static FieldRole GuessRole(string header)
    {
        var k = Collapse(header);
        if (k.Contains("nama")) return FieldRole.Name;
        if (k.Contains("hp") || k.Contains("telp") || k.Contains("wa") || k.Contains("phone")) return FieldRole.Phone;
        if (k.Contains("ttl")) return FieldRole.Ttl;
        if (k.Contains("lahir") && k.Contains("tempat")) return FieldRole.BirthPlace;
        if (k.Contains("tgl") || k.Contains("tanggal")) return FieldRole.BirthDate;
        if (k.Contains("kelas") || k.Contains("rombel")) return FieldRole.Class;
        if (k.Contains("alamat")) return FieldRole.Address;
        if (k.Contains("jk") || k.Contains("kelamin") || k.Contains("gender")) return FieldRole.Gender;
        return FieldRole.Unknown;
    }

    public static string? CanonicalName(string header)
    {
        var key = Collapse(header);
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var kv in Aliases)
        {
            if (Collapse(kv.Key) == key) return kv.Key;
            foreach (var alias in kv.Value)
            {
                if (Collapse(alias) == key) return kv.Key;
            }
        }

        return null;
    }

    public static string NormalizeGender(string raw)
    {
        var s = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return string.Empty;

        if (s is "l" or "lk" or "laki" or "laki-laki" or "laki laki" or "pria" or "cowok" or "male" or "m")
            return "L";
        if (s is "p" or "pr" or "perempuan" or "wanita" or "cewek" or "female" or "f")
            return "P";

        if (s.StartsWith("laki")) return "L";
        if (s.StartsWith("perem") || s.StartsWith("wani")) return "P";
        return (raw ?? string.Empty).Trim();
    }

    public static (string Tempat, string Tanggal) SplitTtl(string ttl)
    {
        var s = (ttl ?? string.Empty).Trim();
        if (s.Length == 0) return (string.Empty, string.Empty);

        var comma = s.IndexOf(',');
        if (comma > 0)
        {
            return (s[..comma].Trim(), s[(comma + 1)..].Trim());
        }

        var date = Regex.Match(s,
            @"(\d{1,2}\s+\p{L}+\s+\d{4}|\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4})$",
            RegexOptions.IgnoreCase);
        if (date.Success)
        {
            var tgl = date.Value.Trim();
            var tempat = s[..date.Index].Trim();
            return (tempat, tgl);
        }

        return (s, string.Empty);
    }

    public static List<string> SplitByDelimiter(string value, string delimiter, int maxParts)
    {
        if (maxParts < 2) maxParts = 2;
        var s = value ?? string.Empty;
        if (string.IsNullOrEmpty(delimiter))
        {
            return Enumerable.Repeat(string.Empty, maxParts - 1).Prepend(s).ToList();
        }

        var parts = s.Split(new[] { delimiter }, maxParts, StringSplitOptions.None)
            .Select(p => p.Trim())
            .ToList();
        while (parts.Count < maxParts) parts.Add(string.Empty);
        return parts;
    }

    public static string Collapse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = Regex.Replace(text.Trim(), @"[\s._]+", " ");
        return cleaned.ToLowerInvariant();
    }
}

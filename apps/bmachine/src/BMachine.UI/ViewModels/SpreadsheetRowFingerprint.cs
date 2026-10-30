namespace BMachine.UI.ViewModels;

/// <summary>
/// Sidik jari satu baris: gabungan seluruh nilai kolom yang dimuat.
///
/// Kolom kunci tunggal (mis. NO FAKTUR) tidak cukup: pada sheet ini satu invoice bisa
/// menempati beberapa baris berturut-turut dengan NO FAKTUR identik (70% baris), sehingga
/// pergeseran 1 baris di dalam blok tersebut tidak akan terlihat. Sidik jari seluruh baris
/// menangkap pergeseran itu karena kolom lain (ORDER TIME, ITEM, dll) ikut berbeda.
/// Aman dari false-positive karena range data sheet ini tidak berformula (tidak ada
/// nilai volatil yang berubah sendiri antara load dan save).
/// </summary>
internal static class SpreadsheetRowFingerprint
{
    private const char Separator = '\u001f';

    public static string FromValues(IEnumerable<string?>? values, int columnCount)
    {
        if (columnCount <= 0) return string.Empty;

        var parts = new string[columnCount];
        var index = 0;
        if (values != null)
        {
            foreach (var value in values)
            {
                if (index >= columnCount) break;
                parts[index++] = value ?? string.Empty;
            }
        }

        // Baris dari API bisa lebih pendek (sel kosong di kanan dipangkas): samakan panjangnya.
        for (; index < columnCount; index++) parts[index] = string.Empty;

        return string.Join(Separator, parts);
    }

    public static string FromLoadedRow(SpreadsheetRowViewModel row, int columnCount)
        => FromValues(row.Cells.Select(cell => cell.OriginalValue), columnCount);

    public static string FromSheetRow(IList<object>? row, int columnCount)
        => FromValues(row?.Select(cell => cell?.ToString()), columnCount);

    /// <summary>True bila semua kolom kosong — baris seperti ini tidak bisa dipakai sebagai jangkar.</summary>
    public static bool IsEmpty(string? fingerprint)
        => string.IsNullOrEmpty(fingerprint) || fingerprint.Replace(Separator.ToString(), string.Empty).Length == 0;

    /// <summary>Nilai pertama yang tidak kosong, dipakai untuk pesan yang bisa dibaca manusia.</summary>
    public static string DisplayOf(string? fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint)) return string.Empty;
        foreach (var part in fingerprint.Split(Separator))
        {
            if (!string.IsNullOrWhiteSpace(part)) return part.Trim();
        }
        return string.Empty;
    }

    public static string DisplayOfSheetRow(IList<object>? row)
    {
        if (row == null) return string.Empty;
        foreach (var cell in row)
        {
            var text = cell?.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return string.Empty;
    }
}

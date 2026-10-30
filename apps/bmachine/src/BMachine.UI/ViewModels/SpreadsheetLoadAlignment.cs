namespace BMachine.UI.ViewModels;

/// <summary>
/// Titik jangkar hasil load: baris & kolom sheet tempat <c>values[0]</c> benar-benar berada.
/// </summary>
internal readonly record struct SpreadsheetLoadAnchor(int StartRow, int StartColumn);

/// <summary>
/// Menyelaraskan penomoran dengan range yang BENAR-BENAR dikembalikan Google.
///
/// VM selama ini memakai start row/col dari teks konfigurasi (mis. "A3:Z"). Selama Google
/// mengembalikan range yang sama, hasilnya identik. Tapi respons bisa mulai di baris/kolom
/// lain (mis. kolom pertama range kosong), dan saat itu penomoran akan bergeser tanpa suara —
/// nilai tersimpan ke baris/kolom yang salah. Helper ini memakai start dari respons bila
/// bisa diurai, dan jatuh kembali ke konfigurasi bila tidak.
/// </summary>
internal static class SpreadsheetLoadAlignment
{
    public static SpreadsheetLoadAnchor Resolve(
        string? responseRange,
        int configuredStartRow,
        int configuredStartColumn)
    {
        var fallback = new SpreadsheetLoadAnchor(configuredStartRow, configuredStartColumn);
        if (string.IsNullOrWhiteSpace(responseRange)) return fallback;

        try
        {
            var start = SpreadsheetValueMapper.ParseRangeStart(responseRange);
            return new SpreadsheetLoadAnchor(start.RowNumber, start.ColumnIndex);
        }
        catch (Exception)
        {
            // Range tak terduga: jangan pernah menggeser penomoran karena tebakan.
            return fallback;
        }
    }
}

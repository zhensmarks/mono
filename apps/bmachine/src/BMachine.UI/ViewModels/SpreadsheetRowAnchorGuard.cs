namespace BMachine.UI.ViewModels;

/// <summary>
/// Jangkar identitas satu baris: nomor baris sheet + nilai kolom kunci saat data dimuat.
/// </summary>
internal readonly record struct SpreadsheetRowAnchor(int SheetRowNumber, string ExpectedKey);

internal readonly record struct SpreadsheetAnchorMismatch(int SheetRowNumber, string Expected, string Actual);

/// <summary>
/// Pengaman anti "grid basi". Nomor baris dihitung sekali saat load; kalau sheet berubah
/// setelahnya (baris disisip/dihapus/di-cut), penulisan akan mendarat di baris yang salah
/// (gejala: isi 540-545 tapi masuk 539-544). Guard ini membandingkan kolom kunci di sheet
/// dengan nilai yang dimuat, sebelum ada satu sel pun ditulis.
/// </summary>
internal static class SpreadsheetRowAnchorGuard
{
    /// <summary>
    /// Membandingkan setiap jangkar dengan nilai sheet yang baru dibaca.
    /// <paramref name="sheetValues"/> adalah hasil bacaan kolom kunci mulai
    /// <paramref name="firstSheetRow"/>; indeks 0 = baris <paramref name="firstSheetRow"/>.
    /// Baris tanpa nilai kunci (kosong) dilewati karena tidak bisa dipakai sebagai jangkar.
    /// </summary>
    public static List<SpreadsheetAnchorMismatch> FindMismatches(
        IEnumerable<SpreadsheetRowAnchor> anchors,
        IList<IList<object>>? sheetValues,
        int firstSheetRow)
    {
        var mismatches = new List<SpreadsheetAnchorMismatch>();
        if (firstSheetRow < 1) return mismatches;

        foreach (var anchor in anchors)
        {
            var expected = anchor.ExpectedKey?.Trim() ?? string.Empty;
            if (expected.Length == 0) continue;

            var index = anchor.SheetRowNumber - firstSheetRow;
            string actual = string.Empty;
            if (sheetValues != null && index >= 0 && index < sheetValues.Count)
            {
                var row = sheetValues[index];
                if (row != null && row.Count > 0)
                    actual = row[0]?.ToString()?.Trim() ?? string.Empty;
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                mismatches.Add(new SpreadsheetAnchorMismatch(anchor.SheetRowNumber, expected, actual));
        }

        return mismatches;
    }

    /// <summary>Pesan siap tampil untuk pengguna, menyebut baris pertama yang bergeser.</summary>
    public static string Describe(IReadOnlyList<SpreadsheetAnchorMismatch> mismatches)
    {
        var first = mismatches[0];
        var more = mismatches.Count > 1 ? $" (dan {mismatches.Count - 1} baris lain)" : string.Empty;
        return $"Data di sheet sudah bergeser sejak terakhir dimuat{more}: " +
               $"baris {first.SheetRowNumber} di sheet berisi '{first.Actual}', " +
               $"sedangkan yang dimuat '{first.Expected}'. " +
               "Simpan dibatalkan agar data tidak salah baris — klik Refresh lalu ulangi edit.";
    }
}

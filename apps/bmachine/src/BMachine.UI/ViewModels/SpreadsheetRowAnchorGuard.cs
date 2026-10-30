namespace BMachine.UI.ViewModels;

/// <summary>
/// Jangkar identitas satu baris: nomor baris sheet, sidik jari seluruh baris saat dimuat,
/// dan nilai tampilan (untuk pesan ke pengguna).
/// </summary>
internal readonly record struct SpreadsheetRowAnchor(
    int SheetRowNumber,
    string Fingerprint,
    string Display);

internal readonly record struct SpreadsheetAnchorMismatch(
    int SheetRowNumber,
    string ExpectedDisplay,
    string ActualDisplay);

/// <summary>
/// Pengaman anti "grid basi". Nomor baris dihitung sekali saat load; kalau sheet berubah
/// setelahnya (baris disisip/dihapus/di-cut), penulisan akan mendarat di baris yang salah
/// (gejala: isi 540-545 tapi masuk 539-544). Guard ini membandingkan sidik jari baris di
/// sheet dengan sidik jari saat dimuat, sebelum ada satu sel pun ditulis.
/// </summary>
internal static class SpreadsheetRowAnchorGuard
{
    /// <summary>
    /// Membandingkan setiap jangkar dengan nilai sheet yang baru dibaca.
    /// <paramref name="sheetValues"/> adalah hasil bacaan mulai
    /// <paramref name="firstSheetRow"/>; indeks 0 = baris <paramref name="firstSheetRow"/>.
    /// Baris yang seluruhnya kosong dilewati karena tidak bisa dipakai sebagai jangkar.
    /// </summary>
    public static List<SpreadsheetAnchorMismatch> FindMismatches(
        IEnumerable<SpreadsheetRowAnchor> anchors,
        IList<IList<object>>? sheetValues,
        int firstSheetRow,
        int columnCount)
    {
        var mismatches = new List<SpreadsheetAnchorMismatch>();
        if (firstSheetRow < 1) return mismatches;

        foreach (var anchor in anchors)
        {
            if (SpreadsheetRowFingerprint.IsEmpty(anchor.Fingerprint)) continue;

            var index = anchor.SheetRowNumber - firstSheetRow;
            var row = sheetValues != null && index >= 0 && index < sheetValues.Count
                ? sheetValues[index]
                : null;

            var actualFingerprint = SpreadsheetRowFingerprint.FromSheetRow(row, columnCount);
            if (string.Equals(actualFingerprint, anchor.Fingerprint, StringComparison.Ordinal))
                continue;

            mismatches.Add(new SpreadsheetAnchorMismatch(
                anchor.SheetRowNumber,
                SpreadsheetRowFingerprint.DisplayOf(anchor.Fingerprint),
                SpreadsheetRowFingerprint.DisplayOfSheetRow(row)));
        }

        return mismatches;
    }

    /// <summary>Pesan siap tampil untuk pengguna, menyebut baris pertama yang bergeser.</summary>
    public static string Describe(IReadOnlyList<SpreadsheetAnchorMismatch> mismatches)
    {
        var first = mismatches[0];
        var more = mismatches.Count > 1 ? $" (dan {mismatches.Count - 1} baris lain)" : string.Empty;
        return $"Data di sheet sudah berubah sejak terakhir dimuat{more}: " +
               $"baris {first.SheetRowNumber} di sheet berisi '{first.ActualDisplay}', " +
               $"sedangkan yang dimuat '{first.ExpectedDisplay}'. " +
               "Simpan dibatalkan agar data tidak salah baris — klik Refresh lalu ulangi edit.";
    }
}

using System.Text.Json;

namespace BMachine.UI.ViewModels;

/// <summary>
/// Satu perubahan yang belum dikirim ke Google Sheets.
/// </summary>
internal sealed record SpreadsheetDraftEntry(
    string SheetId,
    string SheetName,
    int SheetRowNumber,
    int ColumnIndex,
    string OriginalValue,
    string Value);

internal sealed record SpreadsheetDraftRestoreResult(
    int Applied,
    List<SpreadsheetDraftEntry> Remaining);

/// <summary>
/// Menyimpan perubahan yang belum di-push ke spreadsheet, supaya isian tidak hilang saat
/// window ditutup / aplikasi ditutup / window dibuka lagi lewat pencarian Trello.
///
/// Draft diikat ke identitas baris (nomor baris + nilai asli sel). Saat dipulihkan, draft
/// hanya diterapkan kalau sel di sheet masih berisi nilai asli yang sama; kalau datanya
/// sudah berubah, draft dibiarkan sebagai sisa (tidak menimpa data orang lain).
/// </summary>
internal static class SpreadsheetDraftStore
{
    public const string StorageKey = "Spreadsheet.Draft";

    public static string Serialize(IEnumerable<SpreadsheetDraftEntry> entries)
    {
        var list = entries.ToList();
        return list.Count == 0 ? string.Empty : JsonSerializer.Serialize(list);
    }

    public static List<SpreadsheetDraftEntry> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<SpreadsheetDraftEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<SpreadsheetDraftEntry>>(json)
                   ?? new List<SpreadsheetDraftEntry>();
        }
        catch (JsonException)
        {
            // Data rusak / format lama: jangan sampai menghalangi load spreadsheet.
            return new List<SpreadsheetDraftEntry>();
        }
    }

    /// <summary>Ambil semua sel yang berubah menjadi daftar draft.</summary>
    public static List<SpreadsheetDraftEntry> Capture(
        string sheetId,
        string sheetName,
        IEnumerable<SpreadsheetRowViewModel> rows)
    {
        var entries = new List<SpreadsheetDraftEntry>();
        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
            {
                if (!cell.IsChanged) continue;
                entries.Add(new SpreadsheetDraftEntry(
                    sheetId,
                    sheetName,
                    row.SheetRowNumber,
                    cell.ColumnIndex,
                    cell.OriginalValue ?? string.Empty,
                    cell.Value ?? string.Empty));
            }
        }
        return entries;
    }

    /// <summary>
    /// Terapkan draft ke baris yang baru dimuat. Draft untuk sheet lain dibuang; draft yang
    /// barisnya tidak ada / nilainya sudah berubah dikembalikan sebagai sisa.
    /// </summary>
    public static SpreadsheetDraftRestoreResult Apply(
        IReadOnlyList<SpreadsheetDraftEntry> drafts,
        string sheetId,
        IReadOnlyList<SpreadsheetRowViewModel> rows,
        int columnCount)
    {
        var applied = 0;
        var remaining = new List<SpreadsheetDraftEntry>();
        var rowsByNumber = new Dictionary<int, SpreadsheetRowViewModel>();
        foreach (var row in rows)
            rowsByNumber[row.SheetRowNumber] = row;

        foreach (var draft in drafts)
        {
            if (!string.Equals(draft.SheetId, sheetId, StringComparison.Ordinal)) continue;

            if (!rowsByNumber.TryGetValue(draft.SheetRowNumber, out var row) ||
                draft.ColumnIndex < 0 ||
                draft.ColumnIndex >= columnCount ||
                draft.ColumnIndex >= row.Cells.Count)
            {
                remaining.Add(draft);
                continue;
            }

            var cell = row.Cells[draft.ColumnIndex];
            if (!string.Equals(cell.OriginalValue ?? string.Empty, draft.OriginalValue ?? string.Empty, StringComparison.Ordinal))
            {
                // Sheet sudah berubah di sel itu: jangan timpa, simpan sebagai sisa.
                remaining.Add(draft);
                continue;
            }

            cell.Value = draft.Value ?? string.Empty;
            applied++;
        }

        return new SpreadsheetDraftRestoreResult(applied, remaining);
    }
}

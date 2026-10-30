using System.Reflection;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Siklus penuh draft lewat database: edit → tutup (persist) → buka lagi (restore).
/// Ini yang membuat isian tidak hilang saat window dibuka ulang lewat pencarian Trello.
/// </summary>
public sealed class SpreadsheetDraftPersistenceTests
{
    private const string SheetId = "sheet-1";
    private const string SheetName = "ALL DATA REGULER";

    private static void SetPrivate(object target, string field, object? value)
        => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static async Task<object?> InvokeRestoreAsync(SpreadsheetViewModel vm)
    {
        var method = typeof(SpreadsheetViewModel)
            .GetMethod("RestoreDraftsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(vm, null)!;
        await task;
        return null;
    }

    private static SpreadsheetViewModel BuildViewModel(FakeDatabase db, string itemValue)
    {
        var vm = new SpreadsheetViewModel(db);
        SetPrivate(vm, "_sheetId", SheetId);
        SetPrivate(vm, "_sheetName", SheetName);

        for (var column = 0; column < 3; column++)
        {
            vm.Columns.Add(new SpreadsheetColumnViewModel
            {
                Header = $"Column {column + 1}",
                Index = column,
                ColumnLetter = ((char)('A' + column)).ToString()
            });
        }

        var row = new SpreadsheetRowViewModel(537, vm, 540);
        row.Cells.Add(new SpreadsheetCellViewModel { Value = "INV-1", OriginalValue = "INV-1", ColumnIndex = 0 });
        row.Cells.Add(new SpreadsheetCellViewModel { Value = "2026-08-05", OriginalValue = "2026-08-05", ColumnIndex = 1 });
        row.Cells.Add(new SpreadsheetCellViewModel { Value = itemValue, OriginalValue = itemValue, ColumnIndex = 2 });
        vm.Rows.Add(row);
        return vm;
    }

    [Fact]
    public async Task DraftSurvivesReopeningAndComesBackOnNextLoad()
    {
        var db = new FakeDatabase();

        // Sesi 1: user mengisi, lalu window ditutup tanpa push.
        var first = BuildViewModel(db, "ITEM-A");
        first.Rows[0].Cells[2].Value = "ITEM-B";
        // Catatan: HasChanges hanya di-update oleh handler milik LoadData, sedangkan baris
        // di tes ini dibuat manual — jadi yang diperiksa perubahan selnya langsung.
        Assert.True(first.Rows[0].Cells[2].IsChanged);
        await first.PersistDraftsAsync();

        // Sesi 2: window dibuka lagi (VM baru, data sheet masih ITEM-A).
        var second = BuildViewModel(db, "ITEM-A");
        Assert.False(second.HasChanges);

        await InvokeRestoreAsync(second);

        Assert.Equal("ITEM-B", second.Rows[0].Cells[2].Value);
        Assert.True(second.HasChanges);
        Assert.Contains("draft", second.StatusText, StringComparison.OrdinalIgnoreCase);

        // Draft yang sudah diterapkan tidak disimpan dua kali.
        var stored = await db.GetAsync<string>(SpreadsheetDraftStore.StorageKey);
        Assert.True(string.IsNullOrEmpty(stored));
    }

    [Fact]
    public async Task DraftIsNotAppliedWhenSheetValueChangedMeanwhile()
    {
        var db = new FakeDatabase();

        var first = BuildViewModel(db, "ITEM-A");
        first.Rows[0].Cells[2].Value = "ITEM-B";
        await first.PersistDraftsAsync();

        // Sesi 2: orang lain sudah mengubah sel itu di sheet.
        var second = BuildViewModel(db, "ITEM-CHANGED");
        await InvokeRestoreAsync(second);

        Assert.Equal("ITEM-CHANGED", second.Rows[0].Cells[2].Value);
        Assert.False(second.HasChanges);

        // Draft tetap disimpan sebagai sisa (tidak hilang diam-diam).
        var stored = await db.GetAsync<string>(SpreadsheetDraftStore.StorageKey);
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.Single(SpreadsheetDraftStore.Deserialize(stored));
    }

    [Fact]
    public async Task PersistingWithNoChangesClearsStoredDraft()
    {
        var db = new FakeDatabase();

        var first = BuildViewModel(db, "ITEM-A");
        first.Rows[0].Cells[2].Value = "ITEM-B";
        await first.PersistDraftsAsync();
        Assert.False(string.IsNullOrEmpty(await db.GetAsync<string>(SpreadsheetDraftStore.StorageKey)));

        // Setelah push (nilai asli ikut diperbarui), draft harus kosong.
        first.Rows[0].Cells[2].OriginalValue = "ITEM-B";
        await first.PersistDraftsAsync();

        Assert.True(string.IsNullOrEmpty(await db.GetAsync<string>(SpreadsheetDraftStore.StorageKey)));
    }
}

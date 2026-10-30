using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Draft = perubahan yang belum di-push ke Google Sheets. Harus bertahan saat window/aplikasi
/// ditutup, dan hanya diterapkan kembali kalau sel di sheet masih berisi nilai aslinya.
/// </summary>
public sealed class SpreadsheetDraftTests
{
    private const string SheetId = "sheet-1";
    private const string SheetName = "ALL DATA REGULER";

    private static SpreadsheetViewModel NewViewModel()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        for (var column = 0; column < 3; column++)
        {
            vm.Columns.Add(new SpreadsheetColumnViewModel
            {
                Header = $"Column {column + 1}",
                Index = column,
                ColumnLetter = ((char)('A' + column)).ToString()
            });
        }
        return vm;
    }

    private static SpreadsheetRowViewModel AddRow(SpreadsheetViewModel vm, int sheetRow, params string[] values)
    {
        var row = new SpreadsheetRowViewModel(sheetRow - 3, vm, sheetRow);
        for (var column = 0; column < values.Length; column++)
        {
            row.Cells.Add(new SpreadsheetCellViewModel
            {
                Value = values[column],
                OriginalValue = values[column],
                ColumnIndex = column
            });
        }
        vm.Rows.Add(row);
        return row;
    }

    [Fact]
    public void CaptureOnlyRecordsChangedCells()
    {
        var vm = NewViewModel();
        var row = AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        row.Cells[2].Value = "ITEM-B";

        var drafts = SpreadsheetDraftStore.Capture(SheetId, SheetName, vm.Rows);

        var draft = Assert.Single(drafts);
        Assert.Equal(SheetId, draft.SheetId);
        Assert.Equal(540, draft.SheetRowNumber);
        Assert.Equal(2, draft.ColumnIndex);
        Assert.Equal("ITEM-A", draft.OriginalValue);
        Assert.Equal("ITEM-B", draft.Value);
    }

    [Fact]
    public void CaptureIsEmptyWhenNothingChanged()
    {
        var vm = NewViewModel();
        AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");

        Assert.Empty(SpreadsheetDraftStore.Capture(SheetId, SheetName, vm.Rows));
    }

    [Fact]
    public void SerializeRoundTripsAndEmptyDraftStoresNothing()
    {
        var entry = new SpreadsheetDraftEntry(SheetId, SheetName, 540, 2, "old", "new");

        var restored = SpreadsheetDraftStore.Deserialize(SpreadsheetDraftStore.Serialize(new[] { entry }));

        Assert.Equal(entry, Assert.Single(restored));
        Assert.Equal(string.Empty, SpreadsheetDraftStore.Serialize(Array.Empty<SpreadsheetDraftEntry>()));
        Assert.Empty(SpreadsheetDraftStore.Deserialize(null));
        Assert.Empty(SpreadsheetDraftStore.Deserialize(""));
    }

    [Fact]
    public void CorruptDraftPayloadDoesNotThrow()
    {
        Assert.Empty(SpreadsheetDraftStore.Deserialize("{not json"));
        Assert.Empty(SpreadsheetDraftStore.Deserialize("[{\"SheetId\": 1}]"));
    }

    [Fact]
    public void DraftIsReappliedWhenSheetStillMatches()
    {
        var vm = NewViewModel();
        var row = AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry(SheetId, SheetName, 540, 2, "ITEM-A", "ITEM-B")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(1, result.Applied);
        Assert.Empty(result.Remaining);
        Assert.Equal("ITEM-B", row.Cells[2].Value);
        Assert.True(row.Cells[2].IsChanged);
    }

    [Fact]
    public void DraftIsKeptWhenSheetValueChangedMeanwhile()
    {
        var vm = NewViewModel();
        var row = AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-EDITED-BY-SOMEONE");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry(SheetId, SheetName, 540, 2, "ITEM-A", "ITEM-B")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(0, result.Applied);
        Assert.Single(result.Remaining);
        Assert.Equal("ITEM-EDITED-BY-SOMEONE", row.Cells[2].Value);
    }

    [Fact]
    public void DraftForAnotherSheetIsDiscarded()
    {
        var vm = NewViewModel();
        AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry("other-sheet", SheetName, 540, 2, "ITEM-A", "ITEM-B")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(0, result.Applied);
        Assert.Empty(result.Remaining);
    }

    [Fact]
    public void DraftForRowOutsideLoadedRangeIsKept()
    {
        var vm = NewViewModel();
        AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry(SheetId, SheetName, 9999, 2, "ITEM-A", "ITEM-B")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(0, result.Applied);
        Assert.Single(result.Remaining);
    }

    [Fact]
    public void DraftForColumnOutsideRangeIsKept()
    {
        var vm = NewViewModel();
        AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry(SheetId, SheetName, 540, 9, "ITEM-A", "ITEM-B")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(0, result.Applied);
        Assert.Single(result.Remaining);
    }

    [Fact]
    public void MultipleDraftsApplyToTheirOwnRows()
    {
        var vm = NewViewModel();
        var first = AddRow(vm, 540, "INV-1", "2026-08-05", "ITEM-A");
        var second = AddRow(vm, 541, "INV-1", "2026-08-05", "ITEM-B");
        var drafts = new[]
        {
            new SpreadsheetDraftEntry(SheetId, SheetName, 540, 2, "ITEM-A", "ITEM-A2"),
            new SpreadsheetDraftEntry(SheetId, SheetName, 541, 2, "ITEM-B", "ITEM-B2")
        };

        var result = SpreadsheetDraftStore.Apply(drafts, SheetId, vm.Rows, vm.Columns.Count);

        Assert.Equal(2, result.Applied);
        Assert.Equal("ITEM-A2", first.Cells[2].Value);
        Assert.Equal("ITEM-B2", second.Cells[2].Value);
    }
}

using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Seleksi multi-baris harus "diam": penanda hanya boleh ada di sel kolom fokus untuk baris
/// yang benar-benar terpilih, dan tidak boleh tertinggal di baris lain setelah container
/// DataGrid didaur-ulang karena scroll.
/// </summary>
public sealed class SpreadsheetSelectionVirtualizationTests
{
    private const int RowCount = 120;
    private const int ColumnCount = 3;

    private static (SpreadsheetViewModel Vm, List<SpreadsheetRowViewModel> Rows) BuildViewModel()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        for (var column = 0; column < ColumnCount; column++)
        {
            vm.Columns.Add(new SpreadsheetColumnViewModel
            {
                Header = $"Column {column + 1}",
                Index = column,
                ColumnLetter = ((char)('A' + column)).ToString()
            });
        }

        var rows = new List<SpreadsheetRowViewModel>();
        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            var row = new SpreadsheetRowViewModel(rowIndex, vm, rowIndex + 4);
            for (var column = 0; column < ColumnCount; column++)
            {
                var value = $"R{rowIndex + 1}C{column + 1}";
                row.Cells.Add(new SpreadsheetCellViewModel
                {
                    Value = value,
                    OriginalValue = value,
                    ColumnIndex = column
                });
            }
            rows.Add(row);
            vm.Rows.Add(row);
        }
        vm.FilteredRows = new ObservableCollection<SpreadsheetRowViewModel>(rows);
        return (vm, rows);
    }

    private static void FocusColumn(SpreadsheetView view, int? columnIndex)
        => typeof(SpreadsheetView)
            .GetField("_focusedColumnIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, columnIndex);

    [AvaloniaFact]
    public void MultiSelectHighlightStaysOnSelectedRowsAfterVirtualizedScroll()
    {
        var (vm, rows) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());

            FocusColumn(view, 0);

            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[1]);
            grid.SelectedItems.Add(rows[4]);
            Pump(window);

            AssertHighlightMatches(grid, vm, 0);
            Assert.True(rows[1].Cells[0].IsSelectionHighlighted);
            Assert.True(rows[4].Cells[0].IsSelectionHighlighted);
            Assert.False(rows[1].Cells[1].IsSelectionHighlighted);

            // Scroll jauh: container didaur-ulang, penanda tidak boleh tertinggal.
            grid.ScrollIntoView(rows[90], grid.Columns[0]);
            Pump(window);
            AssertHighlightMatches(grid, vm, 0);

            // Kembali ke atas: baris yang sama harus tetap ditandai.
            grid.ScrollIntoView(rows[1], grid.Columns[0]);
            Pump(window);
            AssertHighlightMatches(grid, vm, 0);
            Assert.True(rows[1].Cells[0].IsSelectionHighlighted);
            Assert.True(rows[4].Cells[0].IsSelectionHighlighted);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChangingSelectionClearsOldHighlightWithoutLeftovers()
    {
        var (vm, rows) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());
            FocusColumn(view, 0);

            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[1]);
            grid.SelectedItems.Add(rows[4]);
            Pump(window);
            Assert.True(rows[1].Cells[0].IsSelectionHighlighted);

            // Ganti seleksi ke baris lain: penanda lama harus hilang total.
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[2]);
            grid.SelectedItems.Add(rows[3]);
            Pump(window);

            AssertHighlightMatches(grid, vm, 0);
            Assert.False(rows[1].Cells[0].IsSelectionHighlighted);
            Assert.False(rows[4].Cells[0].IsSelectionHighlighted);
            Assert.True(rows[2].Cells[0].IsSelectionHighlighted);
            Assert.True(rows[3].Cells[0].IsSelectionHighlighted);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChangingFocusedColumnMovesTheHighlight()
    {
        var (vm, rows) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());

            FocusColumn(view, 0);
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[1]);
            grid.SelectedItems.Add(rows[4]);
            Pump(window);
            AssertHighlightMatches(grid, vm, 0);

            FocusColumn(view, 2);
            typeof(SpreadsheetView)
                .GetMethod("UpdateCellSelectionVisuals", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, null);
            Pump(window);

            AssertHighlightMatches(grid, vm, 2);
            Assert.False(rows[1].Cells[0].IsSelectionHighlighted);
            Assert.True(rows[1].Cells[2].IsSelectionHighlighted);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Memastikan overlay visual benar-benar ter-bind ke flag (bukan hanya flag di ViewModel),
    /// supaya kesalahan penulisan binding tidak lolos diam-diam.
    /// </summary>
    [AvaloniaFact]
    public void SelectionOverlayIsVisibleOnlyForHighlightedCells()
    {
        var (vm, rows) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());
            FocusColumn(view, 0);

            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[1]);
            grid.SelectedItems.Add(rows[2]);
            Pump(window);

            var highlighted = FindCell(grid, rows[1], 0);
            var notHighlighted = FindCell(grid, rows[1], 1);
            Assert.NotNull(highlighted);
            Assert.NotNull(notHighlighted);
            Assert.True(FindOverlay(highlighted!)?.IsVisible);
            Assert.False(FindOverlay(notHighlighted!)?.IsVisible ?? true);
        }
        finally
        {
            window.Close();
        }
    }

    private static DataGridCell? FindCell(DataGrid grid, SpreadsheetRowViewModel row, int columnIndex)
        => grid.GetVisualDescendants().OfType<DataGridRow>()
            .FirstOrDefault(visualRow => ReferenceEquals(visualRow.DataContext, row))?
            .GetVisualDescendants().OfType<DataGridCell>()
            .FirstOrDefault(cell =>
                DataGridColumn.GetColumnContainingElement(cell)?.Tag is int tag && tag == columnIndex);

    private static Border? FindOverlay(DataGridCell cell)
    {
        var templateRoot = cell.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Tag is int);
        return templateRoot?.Child is Grid grid && grid.Children.Count > 0
            ? grid.Children[0] as Border
            : null;
    }

    /// <summary>
    /// Memeriksa SEMUA baris di ViewModel (bukan hanya yang ter-realize), jadi penanda yang
    /// tertinggal di container hasil daur-ulang ikut terdeteksi.
    /// </summary>
    private static void AssertHighlightMatches(DataGrid grid, SpreadsheetViewModel vm, int highlightedColumn)
    {
        var selected = grid.SelectedItems;
        var multiSelect = selected != null && selected.Count > 1;

        foreach (var row in vm.Rows)
        {
            var shouldHighlight = multiSelect && selected!.Contains(row);
            for (var column = 0; column < row.Cells.Count; column++)
            {
                var expected = shouldHighlight && column == highlightedColumn;
                Assert.True(
                    expected == row.Cells[column].IsSelectionHighlighted,
                    $"Row {row.SheetRowNumber}, column {column}: expected highlight={expected}, " +
                    $"actual={row.Cells[column].IsSelectionHighlighted}");
            }
        }
    }

    private static void Pump(Window window)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}

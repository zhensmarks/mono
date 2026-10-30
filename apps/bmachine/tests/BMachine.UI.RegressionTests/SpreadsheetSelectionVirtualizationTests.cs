using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class SpreadsheetSelectionVirtualizationTests
{
    [AvaloniaFact]
    public void MultiSelectHighlightTracksRowsAfterVirtualizedScroll()
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

        var rows = new List<SpreadsheetRowViewModel>();
        for (var rowIndex = 0; rowIndex < 120; rowIndex++)
        {
            var row = new SpreadsheetRowViewModel(rowIndex, vm);
            for (var column = 0; column < 3; column++)
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

        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());

            typeof(SpreadsheetView)
                .GetField("_focusedColumnIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, 0);

            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[1]);
            grid.SelectedItems.Add(rows[4]);
            Pump(window);
            AssertVisualSelectionMatches(grid, 0);
            Assert.True(FindRow(grid, rows[1])?.GetVisualDescendants().OfType<DataGridCell>()
                .Any(cell => cell.Classes.Contains("cell-selected")));
            Assert.True(FindRow(grid, rows[4])?.GetVisualDescendants().OfType<DataGridCell>()
                .Any(cell => cell.Classes.Contains("cell-selected")));

            CaptureIfRequested(window);

            grid.ScrollIntoView(rows[90], grid.Columns[0]);
            Pump(window);
            AssertVisualSelectionMatches(grid, 0);

            grid.ScrollIntoView(rows[1], grid.Columns[0]);
            Pump(window);
            AssertVisualSelectionMatches(grid, 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static DataGridRow? FindRow(DataGrid grid, SpreadsheetRowViewModel row)
        => grid.GetVisualDescendants().OfType<DataGridRow>()
            .FirstOrDefault(visualRow => ReferenceEquals(visualRow.DataContext, row));

    private static void AssertVisualSelectionMatches(DataGrid grid, int highlightedColumn)
    {
        foreach (var visualRow in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            var selected = grid.SelectedItems.Count > 1 && grid.SelectedItems.Contains(visualRow.DataContext);
            foreach (var cell in visualRow.GetVisualDescendants().OfType<DataGridCell>())
            {
                var column = DataGridColumn.GetColumnContainingElement(cell);
                var columnIndex = column?.Tag is int index ? index : -1;
                var shouldHighlight = selected && columnIndex == highlightedColumn;
                Assert.True(shouldHighlight == cell.Classes.Contains("cell-selected"),
                    $"Row {((visualRow.DataContext as SpreadsheetRowViewModel)?.SheetRowNumber.ToString() ?? "unknown")}, column {columnIndex}, selected={selected}");
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

    private static void CaptureIfRequested(Window window)
    {
        var path = Environment.GetEnvironmentVariable("BMACHINE_TEST_SCREENSHOT");
        if (string.IsNullOrWhiteSpace(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var bitmap = window.CaptureRenderedFrame();
            if (bitmap != null) bitmap.Save(path);
        }
        catch (NotSupportedException)
        {
            // The standard test backend validates visuals but does not expose pixels.
            // A Skia-enabled smoke run can request and save a rendered frame.
        }
    }
}

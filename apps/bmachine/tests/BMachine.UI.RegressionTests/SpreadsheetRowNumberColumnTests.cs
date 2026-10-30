using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Tampilan ala Google Sheets: gutter nomor baris sheet di kiri, header = nama kolom
/// rata tengah (huruf kolom sheet tidak ditampilkan, hanya tooltip).
/// Nomor yang ditampilkan harus nomor baris sheet asli (bukan indeks grid), karena itu
/// patokan yang dipakai user saat mencocokkan dengan spreadsheet.
/// </summary>
public sealed class SpreadsheetRowNumberColumnTests
{
    private static readonly string[] Headers = { "NO FAKTUR", "ORDER TIME", "CUSTOMER NAME" };

    private static (SpreadsheetViewModel Vm, List<SpreadsheetRowViewModel> Rows) BuildViewModel()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        for (var column = 0; column < Headers.Length; column++)
        {
            vm.Columns.Add(new SpreadsheetColumnViewModel
            {
                Header = Headers[column],
                Index = column,
                ColumnLetter = ((char)('A' + column)).ToString()
            });
        }

        var rows = new List<SpreadsheetRowViewModel>();
        for (var index = 0; index < 40; index++)
        {
            var sheetRow = 540 + index;              // data mulai di baris sheet 540
            var row = new SpreadsheetRowViewModel(index + 1, vm, sheetRow);
            for (var column = 0; column < Headers.Length; column++)
            {
                var value = $"R{sheetRow}C{column + 1}";
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

    [AvaloniaFact]
    public void RowNumberColumnShowsRealSheetRowNumbers()
    {
        var (vm, rows) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());

            // Satu kolom tambahan di depan, khusus nomor baris.
            Assert.Equal(vm.Columns.Count + 1, grid.Columns.Count);

            var numberColumn = grid.Columns[0];
            Assert.Null(numberColumn.Tag);
            Assert.True(numberColumn.IsReadOnly);
            Assert.False(numberColumn.CanUserResize);
            var gutterHeader = Assert.IsType<TextBlock>(numberColumn.Header);
            Assert.Equal("#", gutterHeader.Text);
            Assert.Equal(TextAlignment.Center, gutterHeader.TextAlignment);

            // Kolom data pertama tetap memetakan ke kolom VM index 0.
            Assert.Equal(0, Assert.IsType<int>(grid.Columns[1].Tag));

            var firstRow = grid.GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(row => ReferenceEquals(row.DataContext, rows[0]));
            Assert.NotNull(firstRow);

            var numberCell = firstRow!.GetVisualDescendants().OfType<DataGridCell>()
                .FirstOrDefault(cell => ReferenceEquals(
                    DataGridColumn.GetColumnContainingElement(cell), numberColumn));
            Assert.NotNull(numberCell);

            var texts = numberCell!.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text)
                .ToList();
            Assert.Contains("540", texts);
        }
        finally
        {
            window.Close();
        }
    }

    /// Header hanya nama kolom (huruf sheet A/B/C disembunyikan, ada di tooltip)
    /// dan rata tengah horizontal — menyesuaikan isi sel yang juga center.
    [AvaloniaFact]
    public void ColumnHeadersArePlainTextAndCentered()
    {
        var (vm, _) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());

            for (var column = 0; column < vm.Columns.Count; column++)
            {
                var header = Assert.IsType<TextBlock>(grid.Columns[column + 1].Header);
                Assert.Equal(Headers[column], header.Text);
                Assert.Equal(TextAlignment.Center, header.TextAlignment);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RowNumberColumnIsNotResizableAndDoesNotCarryCellIndex()
    {
        var (vm, _) = BuildViewModel();
        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            Pump(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<SpreadsheetView>());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());

            // Tag=null berarti kolom ini dilewati oleh penyimpanan lebar kolom,
            // daftar kolom tersembunyi, dan target Fill Down.
            foreach (var column in grid.Columns.Skip(1))
                Assert.IsType<int>(column.Tag);
        }
        finally
        {
            window.Close();
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

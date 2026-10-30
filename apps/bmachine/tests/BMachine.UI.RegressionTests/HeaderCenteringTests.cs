using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Header harus rata tengah seperti Google Sheets. Theme Fluent membawa kolom Auto
/// sort-icon (MinWidth 32) yang tidak bisa di-style sehingga isi header bergeser 16px ke
/// kiri; SpreadsheetView.FixHeaderContentCentering mengkompensasinya lewat margin Panel
/// (+16 kiri / -16 kanan). Tes ini mengukur posisi render teks header terhadap pusat
/// kolomnya — termasuk header yang teksnya terpotong, yang harus tetap simetris.
/// </summary>
public sealed class HeaderCenteringTests
{
    private static readonly string[] Headers =
    {
        "NO FAKTUR", "ITEM NAME", "TGL DONE", "CUSTOMER NAME VERY LONG TEXT HERE"
    };

    [AvaloniaFact]
    public void HeaderTextIsHorizontallyCentered()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase());
        for (var c = 0; c < Headers.Length; c++)
        {
            vm.Columns.Add(new SpreadsheetColumnViewModel
            {
                Header = Headers[c],
                Index = c,
                ColumnLetter = ((char)('A' + c)).ToString(),
                Width = new DataGridLength(200, DataGridLengthUnitType.Pixel)
            });
        }

        var rows = new ObservableCollection<SpreadsheetRowViewModel>();
        for (var i = 0; i < 3; i++)
        {
            var row = new SpreadsheetRowViewModel(i + 1, vm, 10 + i);
            for (var c = 0; c < Headers.Length; c++)
                row.Cells.Add(new SpreadsheetCellViewModel
                {
                    Value = $"R{i}C{c}",
                    OriginalValue = $"R{i}C{c}",
                    ColumnIndex = c
                });
            rows.Add(row);
            vm.Rows.Add(row);
        }
        vm.FilteredRows = rows;

        var window = new SpreadsheetWindow { DataContext = vm };
        try
        {
            window.Show();
            for (var attempt = 0; attempt < 25; attempt++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var view = window.GetVisualDescendants().OfType<SpreadsheetView>().Single();
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();

            foreach (var header in grid.Columns.Select(col => col.Header).OfType<TextBlock>())
            {
                var headerControl = header.GetVisualAncestors().OfType<DataGridColumnHeader>().First();
                var panel = header.GetVisualAncestors().OfType<Control>()
                    .First(c => c.GetType() == typeof(Panel));
                // Posisi absolut teks dalam header: panel -> inner grid(0) -> presenter(0) -> tb.
                var textX = panel.Bounds.X + header.Bounds.X;
                var textCenter = textX + header.Bounds.Width / 2.0;
                var delta = textCenter - headerControl.Bounds.Width / 2.0;

                // <= 2px: border kolom 1px + pembulatan; tanpa kompensasi delta ini -17px.
                Assert.True(Math.Abs(delta) <= 2,
                    $"Header '{header.Text}' tidak di tengah: delta={delta:F1}px " +
                    $"(panelX={panel.Bounds.X}, tbX={header.Bounds.X}, tbW={header.Bounds.Width})");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
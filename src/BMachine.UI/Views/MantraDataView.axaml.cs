using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;
using BMachine.UI.ViewModels;
using BMachine.UI.Views.Dialogs.MantraData;

namespace BMachine.UI.Views;

public partial class MantraDataView : UserControl
{
    private MantraDataViewModel? _viewModel;
    private string _activeContextColumn = string.Empty;
    private List<string> _lastSelectedColumns = new();

    public MantraDataView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MantraDataViewModel vm)
        {
            _viewModel = vm;
            WireViewModelDialogs(vm);

            vm.Columns.CollectionChanged += OnColumnsCollectionChanged;
            vm.Rows.CollectionChanged += (s, ev) =>
            {
                if (EmptyPrompt != null)
                    EmptyPrompt.IsVisible = vm.Rows.Count == 0;
            };

            if (EmptyPrompt != null)
                EmptyPrompt.IsVisible = vm.Rows.Count == 0;

            if (vm.Columns.Count > 0)
            {
                RebuildColumns();
            }
        }
    }

    private void WireViewModelDialogs(MantraDataViewModel vm)
    {
        vm.RequestProcessPsdDialogFunc = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new ProcessPsdWindow(vm.Settings.LastPsdFolder, vm.Settings.LastPhotoFolder);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            if (dlg.Confirmed)
                return (dlg.PsdFolder, dlg.PhotoFolder);

            return (null, null);
        };

        vm.RequestAlertFunc = async (title, message) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new MantraAlertDialog(title, message);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();
        };

        vm.RequestConfirmFunc = async (title, message) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new MantraConfirmDialog(title, message);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return dlg.Confirmed;
        };

        vm.RequestInputFunc = async (title, prompt, defVal) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new MantraInputDialog(title, prompt, defVal);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return (dlg.Confirmed, dlg.InputValue);
        };

        vm.RequestFindReplaceFunc = async (cols, curCol) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new FindReplaceWindow(cols, curCol);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return (dlg.Confirmed, dlg.FindText, dlg.ReplaceText, dlg.TargetColumn, dlg.MatchCase);
        };

        vm.RequestSplitColumnFunc = async (cols, src) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new SplitColumnWindow(cols, src);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return (dlg.Confirmed, dlg.SourceColumn, dlg.Separator, dlg.NewColumnA, dlg.NewColumnB, dlg.DeleteSource);
        };

        vm.RequestCustomMergeFunc = async (cols, pre, sample) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new CustomMergeWindow(cols, pre, sample);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return (dlg.Confirmed, dlg.SelectedColumns, dlg.MergeFormat, dlg.Separator, dlg.NewColumnName, dlg.DeleteSourceColumns);
        };
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildColumns();
    }

    private void RebuildColumns()
    {
        if (_viewModel == null || MainDataGrid == null) return;

        MainDataGrid.Columns.Clear();

        // Add index / No column as first column
        var rowNumCol = new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(TableDataRow.RowNumber)),
            IsReadOnly = true,
            Width = new DataGridLength(45),
            CanUserSort = false
        };
        MainDataGrid.Columns.Add(rowNumCol);

        foreach (var colName in _viewModel.Columns)
        {
            var col = colName;
            var templateCol = new DataGridTemplateColumn
            {
                Header = col,
                Width = new DataGridLength(120, DataGridLengthUnitType.Pixel),
                CanUserSort = false
            };

            // Display Template
            templateCol.CellTemplate = new FuncDataTemplate<TableDataRow>((initialRow, ns) =>
            {
                var border = new Border
                {
                    Padding = new Thickness(6, 4)
                };

                var tb = new TextBlock
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SolidColorBrush.Parse("#EDEDED"),
                    FontSize = 12
                };
                border.Child = tb;

                System.ComponentModel.PropertyChangedEventHandler? rowHandler = null;
                TableDataRow? boundRow = null;

                void UpdateDisplay(TableDataRow? r)
                {
                    if (r != null)
                    {
                        tb.Text = r[col];
                        var hex = r.TagColor;
                        if (string.IsNullOrEmpty(hex) || hex == "#00000000")
                            border.Background = Brushes.Transparent;
                        else
                        {
                            try { border.Background = SolidColorBrush.Parse(hex); }
                            catch { border.Background = Brushes.Transparent; }
                        }
                    }
                    else
                    {
                        tb.Text = string.Empty;
                        border.Background = Brushes.Transparent;
                    }
                }

                void AttachRow(TableDataRow? newRow)
                {
                    if (boundRow != null && rowHandler != null)
                    {
                        boundRow.PropertyChanged -= rowHandler;
                    }

                    boundRow = newRow;

                    if (boundRow != null)
                    {
                        rowHandler = (s, pe) =>
                        {
                            if (pe.PropertyName == "Item[]" || pe.PropertyName == nameof(TableDataRow.Values) || pe.PropertyName == nameof(TableDataRow.TagColor))
                            {
                                UpdateDisplay(boundRow);
                            }
                        };
                        boundRow.PropertyChanged += rowHandler;
                    }

                    UpdateDisplay(boundRow);
                }

                border.DataContextChanged += (s, e) =>
                {
                    AttachRow(border.DataContext as TableDataRow);
                };

                border.DetachedFromVisualTree += (s, e) =>
                {
                    AttachRow(null);
                };

                if (initialRow != null)
                {
                    AttachRow(initialRow);
                }

                return border;
            });

            // Edit Template
            templateCol.CellEditingTemplate = new FuncDataTemplate<TableDataRow>((initialRow, ns) =>
            {
                var textBox = new TextBox
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Background = SolidColorBrush.Parse("#1A1E27"),
                    Foreground = SolidColorBrush.Parse("#EDEDED"),
                    BorderThickness = new Thickness(1),
                    BorderBrush = SolidColorBrush.Parse("#3B82F6"),
                    FontSize = 12
                };

                bool updating = false;

                void SyncFromRow(TableDataRow? r)
                {
                    updating = true;
                    textBox.Text = r != null ? r[col] : string.Empty;
                    updating = false;
                }

                textBox.DataContextChanged += (s, e) =>
                {
                    SyncFromRow(textBox.DataContext as TableDataRow);
                };

                textBox.TextChanged += (s, e) =>
                {
                    if (!updating && textBox.DataContext is TableDataRow r)
                    {
                        r[col] = textBox.Text ?? string.Empty;
                    }
                };

                if (initialRow != null)
                {
                    SyncFromRow(initialRow);
                }

                return textBox;
            });

            MainDataGrid.Columns.Add(templateCol);
        }
    }

    private void OnJobKindTextBlockLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is DataJobKind kind)
        {
            tb.Text = DataJobKindInfo.Label(kind);
        }
    }

    private async void OnOpenFileClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih File Data Buku Tahunan / ID Card",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Data Files (*.xlsx, *.xls, *.docx)") { Patterns = new[] { "*.xlsx", "*.xls", "*.docx" } },
                new("Excel Files (*.xlsx, *.xls)") { Patterns = new[] { "*.xlsx", "*.xls" } },
                new("Word Files (*.docx)") { Patterns = new[] { "*.docx" } },
                new("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0 && _viewModel != null)
        {
            var path = files[0].Path.LocalPath;
            await _viewModel.LoadFileByPathAsync(path);
        }
    }

    private void OnExportDaterMenuClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ExportDaterCommand.Execute(null);
    }

    private async void OnExportFormatMenuClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.Rows.Count == 0) return;
        if (sender is MenuItem mi && mi.Tag is string format)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var baseName = Path.GetFileNameWithoutExtension(_viewModel.CurrentFilePath);
            if (string.IsNullOrEmpty(baseName)) baseName = "ExportData";

            var defaultName = format switch
            {
                "txt" => $"{baseName}-export.txt",
                "xlsx" => $"{baseName}-dater.xlsx",
                _ => $"{baseName}-export.{format}"
            };

            var ext = format switch
            {
                "txt" => new FilePickerFileType("Text File (*.txt)") { Patterns = new[] { "*.txt" } },
                "csv" => new FilePickerFileType("CSV File (*.csv)") { Patterns = new[] { "*.csv" } },
                "xlsx" => new FilePickerFileType("Excel Workbook (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
                _ => new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            };

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export ke {format.ToUpperInvariant()}",
                DefaultExtension = format,
                SuggestedFileName = defaultName,
                FileTypeChoices = new[] { ext }
            });

            if (file != null)
            {
                await _viewModel.ExportToFormatAsync(format, file.Path.LocalPath);
            }
        }
    }

    private void OnFormulaBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFormulaToColumn();
            e.Handled = true;
        }
    }

    private void OnApplyFormulaColumnClicked(object? sender, RoutedEventArgs e)
    {
        ApplyFormulaToColumn();
    }

    private void OnApplyFormulaSelectionClicked(object? sender, RoutedEventArgs e)
    {
        ApplyFormulaToSelection();
    }

    private void ApplyFormulaToColumn()
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        if (string.IsNullOrEmpty(col))
        {
            _ = _viewModel.RequestAlertFunc?.Invoke("Rumus", "Klik sel di kolom tujuan rumus terlebih dahulu.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_viewModel.FormulaText))
        {
            _ = _viewModel.RequestAlertFunc?.Invoke("Rumus", "Tulis rumus di kotak fx, contoh: =PROPER([NAMA])");
            return;
        }
        _viewModel.ApplyFormulaToColumn(_viewModel.FormulaText, col);
    }

    private void ApplyFormulaToSelection()
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        var rows = GetSelectedRows();
        if (string.IsNullOrEmpty(col) || rows.Count == 0)
        {
            _ = _viewModel.RequestAlertFunc?.Invoke("Rumus", "Pilih sel tujuan, lalu jalankan rumus.");
            return;
        }
        _viewModel.ApplyFormulaToRows(_viewModel.FormulaText, col, rows);
    }

    private string? GetCurrentOrSelectedColumn()
    {
        if (!string.IsNullOrEmpty(_activeContextColumn))
            return _activeContextColumn;

        if (MainDataGrid.CurrentColumn?.Header is string header && _viewModel != null && _viewModel.Columns.Contains(header))
            return header;

        var firstCol = _lastSelectedColumns.FirstOrDefault();
        if (!string.IsNullOrEmpty(firstCol))
            return firstCol;

        return _viewModel?.Columns.FirstOrDefault();
    }

    private List<TableDataRow> GetSelectedRows()
    {
        var list = new List<TableDataRow>();
        if (MainDataGrid.SelectedItems != null)
        {
            foreach (var item in MainDataGrid.SelectedItems)
            {
                if (item is TableDataRow r) list.Add(r);
            }
        }
        if (list.Count == 0 && MainDataGrid.SelectedItem is TableDataRow single)
        {
            list.Add(single);
        }
        return list.Distinct().ToList();
    }

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        var rows = GetSelectedRows();
        _viewModel.SelectedRowCount = rows.Count;
    }

    private void OnDataGridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.DataContext is not TableDataRow row) return;
        var col = e.Column.Header?.ToString();
        if (string.IsNullOrEmpty(col) || _viewModel == null) return;

        var text = row[col];
        if (FormulaEngine.LooksLikeFormula(text))
        {
            try
            {
                var res = FormulaEngine.Evaluate(text, _viewModel.SnapshotRow(row), _viewModel.Columns.ToList());
                row[col] = res;
            }
            catch (FormulaException ex)
            {
                row[col] = "#ERR!";
                _viewModel.StatusMessage = $"Rumus error: {ex.Message}";
            }
        }
    }

    // Keyboard Shortcuts (Ctrl+C, Ctrl+V, Del, etc.)
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox tb && tb != FormulaBox)
        {
            // Allow native textbox editing
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.C)
            {
                CopySelectedToClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.Z)
            {
                _viewModel?.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                _ = _viewModel?.NewTableAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                OnOpenFileClicked(null, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _ = _viewModel?.ProcessPhotoshopAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.H)
            {
                OnMenuFindReplaceClicked(null, e);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                DeleteSelectedRows();
            else
                ClearSelectedCells();
            e.Handled = true;
        }
    }

    private async void CopySelectedToClipboard()
    {
        if (_viewModel == null) return;
        var selectedRows = GetSelectedRows();
        if (selectedRows.Count == 0) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        var sb = new StringBuilder();
        foreach (var row in selectedRows)
        {
            var vals = _viewModel.Columns.Select(c => row[c]);
            sb.AppendLine(string.Join("\t", vals));
        }

        var text = sb.ToString().TrimEnd('\r', '\n');
        await topLevel.Clipboard.SetTextAsync(text);
        _viewModel.StatusMessage = $"{selectedRows.Count} baris disalin ke clipboard.";
    }

    private async void PasteFromClipboard()
    {
        if (_viewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        var clipboardText = await topLevel.Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(clipboardText)) return;

        _viewModel.PushUndo();

        var lines = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        if (lines.Count == 0) return;

        int startRowIndex = 0;
        int startColIndex = 0;

        if (MainDataGrid.SelectedIndex >= 0)
        {
            startRowIndex = MainDataGrid.SelectedIndex;
        }

        var curCol = GetCurrentOrSelectedColumn();
        if (!string.IsNullOrEmpty(curCol))
        {
            startColIndex = _viewModel.Columns.IndexOf(curCol);
            if (startColIndex < 0) startColIndex = 0;
        }

        int pastedRows = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            int targetRowIdx = startRowIndex + i;
            if (targetRowIdx >= _viewModel.Rows.Count)
            {
                var newRow = new TableDataRow { RowNumber = _viewModel.Rows.Count + 1 };
                _viewModel.Rows.Add(newRow);
            }

            var targetRow = _viewModel.Rows[targetRowIdx];
            var cells = lines[i].Split('\t');

            for (int c = 0; c < cells.Length; c++)
            {
                int targetColIdx = startColIndex + c;
                if (targetColIdx < _viewModel.Columns.Count)
                {
                    var colName = _viewModel.Columns[targetColIdx];
                    targetRow[colName] = cells[c].Trim();
                }
            }
            pastedRows++;
        }

        _viewModel.TotalRows = _viewModel.Rows.Count;
        _viewModel.StatusMessage = $"Paste selesai: {pastedRows} baris diperbarui.";
        _viewModel.RefreshFilteredRows();
    }

    private void ClearSelectedCells()
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        var rows = GetSelectedRows();
        if (rows.Count == 0 || string.IsNullOrEmpty(col)) return;

        var cells = rows.Select(r => (r, col)).ToList();
        _viewModel.ClearCells(cells);
    }

    private void DeleteSelectedRows()
    {
        if (_viewModel == null) return;
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;
        _viewModel.DeleteRows(rows);
    }

    // Context Menu Handlers
    private void OnMenuCopyClicked(object? sender, RoutedEventArgs e) => CopySelectedToClipboard();
    private void OnMenuPasteClicked(object? sender, RoutedEventArgs e) => PasteFromClipboard();
    private void OnMenuClearCellsClicked(object? sender, RoutedEventArgs e) => ClearSelectedCells();

    private async void OnMenuCustomMergeClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.Columns.Count < 2)
        {
            if (_viewModel?.RequestAlertFunc != null)
                await _viewModel.RequestAlertFunc("Perhatian", "Dibutuhkan minimal 2 kolom data untuk melakukan merge.");
            return;
        }

        var preselected = new List<string>();
        var cur = GetCurrentOrSelectedColumn();
        if (!string.IsNullOrEmpty(cur)) preselected.Add(cur);

        var sampleRow = _viewModel.Rows.FirstOrDefault();
        if (_viewModel.RequestCustomMergeFunc != null)
        {
            var res = await _viewModel.RequestCustomMergeFunc(_viewModel.Columns, preselected, sampleRow);
            if (res.Confirmed)
            {
                _viewModel.CustomMergeColumns(res.SelectedColumns, res.MergeFormat, res.Separator, res.NewColumnName, res.DeleteSource);
            }
        }
    }

    private void OnMenuTitleCaseClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        var rows = GetSelectedRows();
        if (rows.Count == 0) rows = _viewModel.Rows.ToList();
        _viewModel.ApplyTitleCase(rows, col);
    }

    private void OnMenuDateFormatFullClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        if (string.IsNullOrEmpty(col)) return;
        var rows = GetSelectedRows();
        if (rows.Count == 0) rows = _viewModel.Rows.ToList();
        _viewModel.ApplyDateFormat(rows, col, "Full");
    }

    private void OnMenuPhonePrefixClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        if (string.IsNullOrEmpty(col)) return;
        var rows = GetSelectedRows();
        if (rows.Count == 0) rows = _viewModel.Rows.ToList();
        _viewModel.ApplyPhonePrefix(rows, col);
    }

    private async void OnMenuFindReplaceClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_viewModel.RequestFindReplaceFunc != null)
        {
            var res = await _viewModel.RequestFindReplaceFunc(_viewModel.Columns, GetCurrentOrSelectedColumn());
            if (res.Confirmed)
            {
                _viewModel.FindReplace(res.Find, res.Replace, res.TargetColumn, res.MatchCase);
            }
        }
    }

    private async void OnMenuSplitColumnClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.Columns.Count == 0) return;
        if (_viewModel.RequestSplitColumnFunc != null)
        {
            var res = await _viewModel.RequestSplitColumnFunc(_viewModel.Columns, GetCurrentOrSelectedColumn());
            if (res.Confirmed)
            {
                _viewModel.SplitColumn(res.Source, res.Separator, res.ColA, res.ColB, res.DeleteSource);
            }
        }
    }

    private async void OnMenuInsertColumnLeftClicked(object? sender, RoutedEventArgs e)
    {
        await InsertColumnPrompt(true);
    }

    private async void OnMenuInsertColumnRightClicked(object? sender, RoutedEventArgs e)
    {
        await InsertColumnPrompt(false);
    }

    private async Task InsertColumnPrompt(bool insertLeft)
    {
        if (_viewModel == null) return;
        if (_viewModel.RequestInputFunc != null)
        {
            var res = await _viewModel.RequestInputFunc("Sisipkan Kolom Baru", "Masukkan nama untuk kolom baru:", "KOLOM_BARU");
            if (res.Confirmed && !string.IsNullOrWhiteSpace(res.Value))
            {
                int targetIdx = -1;
                var currentCol = GetCurrentOrSelectedColumn();
                if (!string.IsNullOrEmpty(currentCol))
                {
                    targetIdx = _viewModel.Columns.IndexOf(currentCol);
                    if (!insertLeft) targetIdx += 1;
                }
                _viewModel.InsertColumn(res.Value, targetIdx);
            }
        }
    }

    private void OnMenuMoveColumnLeftClicked(object? sender, RoutedEventArgs e)
    {
        var col = GetCurrentOrSelectedColumn();
        if (!string.IsNullOrEmpty(col)) _viewModel?.MoveColumn(col, -1);
    }

    private void OnMenuMoveColumnRightClicked(object? sender, RoutedEventArgs e)
    {
        var col = GetCurrentOrSelectedColumn();
        if (!string.IsNullOrEmpty(col)) _viewModel?.MoveColumn(col, 1);
    }

    private async void OnMenuRenameColumnClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        if (string.IsNullOrEmpty(col)) return;

        if (_viewModel.RequestInputFunc != null)
        {
            var res = await _viewModel.RequestInputFunc("Ubah Nama Kolom", $"Masukkan nama baru untuk kolom '{col}':", col);
            if (res.Confirmed && !string.IsNullOrWhiteSpace(res.Value))
            {
                _viewModel.RenameColumn(col, res.Value);
            }
        }
    }

    private async void OnMenuDeleteColumnClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var col = GetCurrentOrSelectedColumn();
        if (string.IsNullOrEmpty(col)) return;

        if (_viewModel.RequestConfirmFunc != null)
        {
            var confirm = await _viewModel.RequestConfirmFunc("Konfirmasi Hapus Kolom", $"Hapus kolom '{col}' beserta seluruh datanya?");
            if (confirm)
            {
                _viewModel.DeleteColumn(col);
            }
        }
    }

    private void OnMenuInsertRowClicked(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        var last = rows.LastOrDefault();
        int idx = (last != null && _viewModel != null) ? _viewModel.Rows.IndexOf(last) + 1 : -1;
        _viewModel?.InsertRow(idx);
    }

    private async void OnMenuDeleteRowsClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;

        if (_viewModel.RequestConfirmFunc != null)
        {
            var confirm = await _viewModel.RequestConfirmFunc("Konfirmasi Hapus Baris", $"Hapus {rows.Count} baris data yang dipilih?");
            if (confirm)
            {
                _viewModel.DeleteRows(rows);
            }
        }
    }

    private void OnMenuColorGreenClicked(object? sender, RoutedEventArgs e) => _viewModel?.SetRowHighlight(GetSelectedRows(), "#404ADE80");
    private void OnMenuColorBlueClicked(object? sender, RoutedEventArgs e) => _viewModel?.SetRowHighlight(GetSelectedRows(), "#4060A5FA");
    private void OnMenuColorAmberClicked(object? sender, RoutedEventArgs e) => _viewModel?.SetRowHighlight(GetSelectedRows(), "#40FBBF24");
    private void OnMenuColorRedClicked(object? sender, RoutedEventArgs e) => _viewModel?.SetRowHighlight(GetSelectedRows(), "#40EF4444");
    private void OnMenuColorClearClicked(object? sender, RoutedEventArgs e) => _viewModel?.ClearRowHighlight(GetSelectedRows());

    private void OnMenuTrimSpacesClicked(object? sender, RoutedEventArgs e) => _viewModel?.TrimAllCells();
    private void OnMenuNormalizeGenderClicked(object? sender, RoutedEventArgs e) => _viewModel?.NormalizeGenderColumn(GetCurrentOrSelectedColumn());

    // Drag & Drop Handlers
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            if (DragOverlay != null) DragOverlay.IsVisible = true;
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DragOverlay != null) DragOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DragOverlay != null) DragOverlay.IsVisible = false;
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    var path = file.Path.LocalPath;
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".xlsx" or ".xls" or ".docx" && _viewModel != null)
                    {
                        await _viewModel.LoadFileByPathAsync(path);
                        break;
                    }
                }
            }
        }
    }
}

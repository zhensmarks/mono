using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Data;
using BMachine.UI.ViewModels;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Interactivity;
using System;
using System.Globalization;

namespace BMachine.UI.Views;

public partial class SpreadsheetView : UserControl
{
    private DataGrid? _dataGrid;
    private int? _focusedColumnIndex = null;
    private bool _isExtendingSelection = false;

    public SpreadsheetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SpreadsheetViewModel vm)
        {
            // Unsubscribe old if any (simplified for now as View usually lives with one VM)
            vm.Columns.CollectionChanged += OnColumnsCollectionChanged;
            
            // Build if already has columns
            if (vm.Columns.Any()) RebuildColumns(vm.Columns, vm);
        }
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is SpreadsheetViewModel vm)
        {
            // For simplicity, full rebuild on any change. 
            // Optimization: handle Add/Remove specific items.
            RebuildColumns(vm.Columns, vm);
        }
    }

    private void RebuildColumns(IEnumerable<SpreadsheetColumnViewModel> columns, SpreadsheetViewModel vm)
    {
        // Lazy initialization of DataGrid to avoid XAML issues
        if (_dataGrid == null)
        {
            var container = this.FindControl<Border>("GridContainer");
            if (container == null) return;
            
            _dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserResizeColumns = true,
                CanUserSortColumns = false,
                SelectionMode = DataGridSelectionMode.Extended,
                RowHeaderWidth = 30,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                IsReadOnly = true, // Default to ReadOnly to enable robust selection
                HorizontalGridLinesBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40FFFFFF")),
                VerticalGridLinesBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40FFFFFF"))
            };
            
            // Binding ItemsSource - Explicitly set Source to VM to avoid DataContext issues on initial load
            var itemsSourceBinding = new Binding("FilteredRows") { Source = vm };
            _dataGrid.Bind(DataGrid.ItemsSourceProperty, itemsSourceBinding);

            // Zoom Implementation using LayoutTransformControl
            var scaler = new LayoutTransformControl();
            var transform = new Avalonia.Media.ScaleTransform();
            
            // Explicitly set Source to VM because ScaleTransform doesn't inherit DataContext from visual tree
            var zoomBinding = new Binding("ZoomLevel") { Source = vm };
            
            // Bind ScaleX and ScaleY to ZoomLevel
            transform.Bind(Avalonia.Media.ScaleTransform.ScaleXProperty, zoomBinding);
            transform.Bind(Avalonia.Media.ScaleTransform.ScaleYProperty, zoomBinding);
            
            scaler.LayoutTransform = transform;
            scaler.Child = _dataGrid;

            container.Child = scaler;
            
            _dataGrid.AddHandler(KeyDownEvent, OnDataGridKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _dataGrid.AddHandler(PointerPressedEvent, OnDataGridPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _dataGrid.CellEditEnded += OnDataGridCellEditEnded;
            _dataGrid.CurrentCellChanged += OnDataGridCurrentCellChanged;
        }

        _dataGrid.Columns.Clear();
        
        foreach (var col in columns)
        {
            DataGridTemplateColumn templateCol = new DataGridTemplateColumn
            {
                Header = col.Header,
                Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
                MinWidth = 100,
                // Store Column Index in Tag for Fill Down logic (Column level)
                Tag = col.Index
            };

            // Bind Width to ViewModel
            var widthBinding = new Binding(nameof(SpreadsheetColumnViewModel.Width))
            {
                Source = col,
                Mode = BindingMode.TwoWay
            };
            templateCol.Bind(DataGridColumn.WidthProperty, widthBinding);

            // 1. Dropdown Column
            if (col.IsDropdown)
            {
                // Cell Template (Display)
                templateCol.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    // Wrap in Border to capture clicks on full cell area and store Tag
                    var border = new Border { Background = Avalonia.Media.Brushes.Transparent, Tag = col.Index };
                    
                    var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                    textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));
                    
                    border.Child = textBlock;
                    return border;
                });

                // Editing Template (AutoCompleteBox)
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var autoComplete = new AutoCompleteBox 
                    { 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        FilterMode = AutoCompleteFilterMode.Contains,
                        MinimumPrefixLength = 0,
                        IsTextCompletionEnabled = true 
                    };
                    
                    // Bind ItemsSource to Column's DropdownItems
                    autoComplete.Bind(AutoCompleteBox.ItemsSourceProperty, new Binding(nameof(SpreadsheetColumnViewModel.DropdownItems)) { Source = col });
                    
                    // Bind Text to Cell Value (TwoWay)
                    autoComplete.Bind(AutoCompleteBox.TextProperty, new Binding($"Cells[{col.Index}].Value") { Mode = BindingMode.TwoWay });
                    
                    return autoComplete;
                });
            }
            // 2. Date Column
            else if (col.IsDate)
            {
                // Cell Template (Display)
                templateCol.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var border = new Border { Background = Avalonia.Media.Brushes.Transparent, Tag = col.Index };

                    var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                    textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));
                    
                    border.Child = textBlock;
                    return border;
                });

                // Editing Template (DatePicker)
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var picker = new CalendarDatePicker { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                    // Bind SelectedDate with Converter
                    picker.Bind(CalendarDatePicker.SelectedDateProperty, 
                        new Binding($"Cells[{col.Index}].Value") { 
                            Mode = BindingMode.TwoWay, 
                            Converter = StringToDateTimeConverter.Instance 
                        });
                    return picker;
                });
            }
            // 3. Text Column (Default) - Converted to TemplateColumn
            else
            {
                // Cell Template (Display)
                templateCol.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var border = new Border { Background = Avalonia.Media.Brushes.Transparent, Tag = col.Index };

                    var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                    textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));

                    border.Child = textBlock;
                    return border;
                });

                // Editing Template (TextBox)
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var textBox = new TextBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                    textBox.Bind(TextBox.TextProperty, new Binding($"Cells[{col.Index}].Value") { Mode = BindingMode.TwoWay });
                    return textBox;
                });
            }
            
            // Bind IsVisible to the ColumnViewModel's IsVisible property.
            var visibilityBinding = new Binding(nameof(SpreadsheetColumnViewModel.IsVisible))
            {
                Source = col,
                Mode = BindingMode.TwoWay
            };
            
            templateCol.Bind(DataGridColumn.IsVisibleProperty, visibilityBinding);

            _dataGrid.Columns.Add(templateCol);
        }
    }

    private void OnDataGridKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        // Ctrl+D (Fill Down - Row based)
        if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Avalonia.Input.Key.D)
        {
            FillDownRows();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.F2)
        {
            if (_dataGrid != null)
            {
                _dataGrid.IsReadOnly = false;
                _dataGrid.BeginEdit();
                e.Handled = true;
            }
        }
        else if (e.Key == Avalonia.Input.Key.Delete || e.Key == Avalonia.Input.Key.Back)
        {
            if (DataContext is SpreadsheetViewModel vm && _dataGrid != null)
            {
                // Resolve current cell from DataGrid
                var currentColumn = _dataGrid.CurrentColumn;
                // Use SelectedItem (extended selection)
                var currentRow = _dataGrid.SelectedItem as SpreadsheetRowViewModel;

                if (currentColumn != null && currentRow != null)
                {
                    // Use Tag (Column Index) stored during column creation
                    if (currentColumn.Tag is int colIndex && colIndex >= 0 && colIndex < currentRow.Cells.Count)
                    {
                        vm.SelectedCell = currentRow.Cells[colIndex];
                        // Execute Command
                        if (vm.ClearSelectedDateCommand.CanExecute(null))
                        {
                            vm.ClearSelectedDateCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
            }
        }
    }

    private void FillDownRows()
    {
        if (_dataGrid == null || DataContext is not SpreadsheetViewModel vm) return;

        // Get selected rows
        var selectedItems = _dataGrid.SelectedItems;
        if (selectedItems == null || selectedItems.Count < 2) return;

        // VISUAL ORDER FILL: Find the topmost row in the CURRENT FILTERED VIEW
        var selectedRows = selectedItems.Cast<SpreadsheetRowViewModel>().ToList();
        var visualOrder = vm.FilteredRows.ToList();
        
        // Find first selected row in visual order
        var sourceRow = visualOrder.FirstOrDefault(r => selectedRows.Contains(r));
        if (sourceRow == null) return; // Safety check

        vm.StatusText = $"Fill from Row {sourceRow.OriginalRowIndex + 1} ({visualOrder.IndexOf(sourceRow) + 1} visually)...";
        
        // Determine which columns to fill
        // If a column is focused, only fill that column
        // Otherwise, fill all columns
        int startCol = 0;
        int endCol = sourceRow.Cells.Count;
        
        if (_focusedColumnIndex.HasValue)
        {
            // Single column fill
            startCol = _focusedColumnIndex.Value;
            endCol = _focusedColumnIndex.Value + 1;
        }
        
        // Copy cells from source to other rows
        int count = 0;
        var targetIndices = new System.Collections.Generic.List<int>();
        foreach (var targetRow in selectedRows)
        {
            if (targetRow == sourceRow) continue; // Skip source

            targetIndices.Add(targetRow.OriginalRowIndex + 1);
            // Copy each cell value in the range
            for (int colIndex = startCol; colIndex < endCol && colIndex < sourceRow.Cells.Count && colIndex < targetRow.Cells.Count; colIndex++)
            {
                targetRow.Cells[colIndex].Value = sourceRow.Cells[colIndex].Value;
            }
            count++;
        }
        
        // Show column name for debugging
        string columnName;
        string sourceVal = "";
        if (_focusedColumnIndex.HasValue)
        {
            var colVM = vm.Columns.FirstOrDefault(c => c.Index == _focusedColumnIndex.Value);
            columnName = colVM != null ? $"'{colVM.Header}' (col {_focusedColumnIndex.Value})" : $"col {_focusedColumnIndex.Value}";
            if (_focusedColumnIndex.Value < sourceRow.Cells.Count)
                sourceVal = $" val='{sourceRow.Cells[_focusedColumnIndex.Value].Value}'";
        }
        else
        {
            columnName = "all cols";
        }
        vm.StatusText = $"Filled {count} rows from Row#{sourceRow.OriginalRowIndex + 1} to [{string.Join(",", targetIndices)}] ({columnName}){sourceVal}";
    }

    private void OnDataGridPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // FORCE FOCUS UPDATE: Update _focusedColumnIndex to the column under the mouse
        // This fixes the issue where Shift+Click doesn't update CurrentCell/FocusedColumn as expected
        // Walk up from source to find a Control with Tag = colIndex
        var visual = e.Source as Avalonia.Visual;
        while (visual != null)
        {
            if (visual is Control control && control.Tag is int tagIndex)
            {
                _focusedColumnIndex = tagIndex;
                System.Diagnostics.Debug.WriteLine($"PointerPressed: Forced Focus to Column {tagIndex}");
                break;
            }
            // Stop if we hit the DataGrid itself (avoid finding unrelated tags)
            if (visual is DataGrid) break;
            
            visual = visual.GetVisualParent();
        }

        // Track if user is extending selection (Shift/Ctrl held)
        // This prevents CurrentCellChanged from overwriting _focusedColumnIndex
        _isExtendingSelection = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) 
                             || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);

        // Only handle double-click (ClickCount >= 2)
        // Skip if Shift or Ctrl held (those are for selection)
        if (e.ClickCount >= 2 && !_isExtendingSelection)
        {
            e.Handled = true; // Stop DataGrid from processing
            if (_dataGrid != null && DataContext is SpreadsheetViewModel vm)
            {
                _dataGrid.IsReadOnly = false;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_dataGrid != null) _dataGrid.BeginEdit();
                }, Avalonia.Threading.DispatcherPriority.Input);
                vm.StatusText = "Editing...";
            }
        }
    }

    private void OnDataGridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (_dataGrid == null) return;
        
        // Revert to ReadOnly to restore robust selection behavior
        _dataGrid.IsReadOnly = true;
    }

    private void OnDataGridCurrentCellChanged(object? sender, EventArgs e)
    {
        if (_dataGrid == null || DataContext is not SpreadsheetViewModel vm) return;
        
        // DON'T update focused column during Shift/Ctrl selection extension
        // User clicked on column X first, then Shift+Clicked to extend rows
        // We want to keep column X as the fill target
        if (_isExtendingSelection) return;
        
        // Track which column is currently focused
        var currentColumn = _dataGrid.CurrentColumn;
        if (currentColumn != null)
        {
            // Find the column index by matching header
            var header = currentColumn.Header?.ToString();
            var colVM = vm.Columns.FirstOrDefault(c => c.Header == header);
            _focusedColumnIndex = colVM?.Index;
        }
        else
        {
            _focusedColumnIndex = null;
        }
    }
}

// Converter for Date Binding
public class StringToDateTimeConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringToDateTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string s && DateTime.TryParse(s, out var date))
        {
            return date;
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is DateTime date)
        {
            // Format to match Google Sheets preference if possible, or standard ISO/User Locale
            // User requested "6 Februari 2026" style in screenshot, but underlying value might be different.
            // Using a standard format that Sheets understands is safest, e.g. yyyy-MM-dd or keeping locale.
            // Let's use 'yyyy-MM-dd' or standard string.
            return date.ToString("yyyy-MM-dd"); // ISO 8601 is safe for API usually.
            // Or use 'd MMMM yyyy' for Indonesian locale if user wants that visual?
            // "6 Februari 2026" -> 'd MMMM yyyy'.
            // If I return ISO, the TextBlock template will show ISO.
            // If the user entered format is specific, I should try to Match it?
            // For now, I'll return 'yyyy-MM-dd' as it is robust.
        }
        return value?.ToString();
    }
}

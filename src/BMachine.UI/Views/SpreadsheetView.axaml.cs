using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
            DataGridColumn dataGridCol;

            // 1. Dropdown Column
            if (col.IsDropdown)
            {
                var templateCol = new DataGridTemplateColumn
                {
                    Header = col.Header,
                    Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
                    MinWidth = 100
                };

                // Cell Template (Display)
                templateCol.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                    textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));
                    return textBlock;
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

                dataGridCol = templateCol;
            }
            // 2. Date Column
            else if (col.IsDate)
            {
                var templateCol = new DataGridTemplateColumn
                {
                    Header = col.Header,
                    Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
                    MinWidth = 100
                };

                // Cell Template (Display)
                templateCol.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                    textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));
                    return textBlock;
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

                dataGridCol = templateCol;
            }
            // 3. Text Column (Default)
            else
            {
                var bindingPath = $"Cells[{col.Index}].Value";
                dataGridCol = new DataGridTextColumn
                {
                    Header = col.Header,
                    Binding = new Binding(bindingPath) { Mode = BindingMode.TwoWay },
                    IsReadOnly = false,
                    Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
                    MinWidth = 100
                };
            }
            
            // Bind IsVisible to the ColumnViewModel's IsVisible property.
            var visibilityBinding = new Binding(nameof(SpreadsheetColumnViewModel.IsVisible))
            {
                Source = col,
                Mode = BindingMode.TwoWay
            };
            
            dataGridCol.Bind(DataGridColumn.IsVisibleProperty, visibilityBinding);

            // Store Column Index in Tag for Fill Down logic
            dataGridCol.Tag = col.Index;

            _dataGrid.Columns.Add(dataGridCol);
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
        
        var columnName = _focusedColumnIndex.HasValue ? $"col {_focusedColumnIndex.Value}" : "all cols";
        vm.StatusText = $"Filled {count} rows from Row#{sourceRow.OriginalRowIndex + 1} to [{string.Join(",", targetIndices)}] ({columnName})";
    }

    private void OnDataGridPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Only handle double-click (ClickCount >= 2)
        // Skip if Shift or Ctrl held (those are for selection)
        if (e.ClickCount >= 2 
            && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)
            && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
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

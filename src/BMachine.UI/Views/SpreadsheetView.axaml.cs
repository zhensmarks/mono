using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

        var refreshBtn = this.FindControl<Button>("RefreshBtn");
        if (refreshBtn != null)
        {
            refreshBtn.AddHandler(Button.ClickEvent, (s, e) =>
            {
                SyncActualColumnWidthsToViewModel();
                if (DataContext is SpreadsheetViewModel vm && !vm.IsLoading)
                {
                    _ = vm.SaveColumnWidthsAsync();
                }
            }, RoutingStrategies.Tunnel);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly List<Action> _columnCleanups = new();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SpreadsheetViewModel vm)
        {
            vm.Columns.CollectionChanged -= OnColumnsCollectionChanged;
            vm.Columns.CollectionChanged += OnColumnsCollectionChanged;

            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            
            // Build if already has columns and not currently loading
            if (vm.Columns.Any() && !vm.IsLoading) RebuildColumns(vm.Columns, vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is SpreadsheetViewModel vm && e.PropertyName == nameof(SpreadsheetViewModel.IsLoading) && !vm.IsLoading)
        {
            if (vm.Columns.Any())
            {
                RebuildColumns(vm.Columns, vm);
            }
        }
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is SpreadsheetViewModel vm && !vm.IsLoading)
        {
            RebuildColumns(vm.Columns, vm);
        }
    }

    private void RebuildColumns(IEnumerable<SpreadsheetColumnViewModel> columns, SpreadsheetViewModel vm)
    {
        // Detach prior column event handlers to avoid accumulating closures
        foreach (var cleanup in _columnCleanups)
        {
            try { cleanup(); } catch { }
        }
        _columnCleanups.Clear();
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
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                MinColumnWidth = 30,
                IsReadOnly = false,
                BorderThickness = new Avalonia.Thickness(0),
                Background = Avalonia.Media.Brushes.Transparent
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
            _dataGrid.AddHandler(PointerReleasedEvent, OnDataGridPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            _dataGrid.AddHandler(PointerCaptureLostEvent, OnDataGridPointerCaptureLost, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            _dataGrid.BeginningEdit += OnDataGridBeginningEdit;
            _dataGrid.CellEditEnded += OnDataGridCellEditEnded;
            _dataGrid.CurrentCellChanged += OnDataGridCurrentCellChanged;
            _dataGrid.SelectionChanged += OnDataGridSelectionChanged;
            _dataGrid.AddHandler(ScrollViewer.ScrollChangedEvent, (s, e) => UpdateCellSelectionVisuals());

            // Click on empty area to deselect
            container.PointerPressed += (s, e) =>
            {
                // Only deselect if click is directly on the container (not on a DataGrid cell)
                if (e.Source == container || e.Source is Border b && b.Name == "GridContainer")
                {
                    _dataGrid.SelectedItems.Clear();
                    _dataGrid.SelectedItem = null;
                    UpdateCellSelectionVisuals();
                }
            };
        }

        _dataGrid.Columns.Clear();
        
        foreach (var col in columns)
        {
            DataGridTemplateColumn templateCol = new DataGridTemplateColumn
            {
                Header = col.Header,
                Width = col.Width,
                MinWidth = 30,
                // Store Column Index in Tag for Fill Down logic (Column level)
                Tag = col.Index
            };

            // Sync from View to ViewModel when user resizes column in DataGrid UI
            templateCol.PropertyChanged += (s, e) =>
            {
                if (e.Property == DataGridColumn.WidthProperty)
                {
                    if (templateCol.Width.IsAbsolute && templateCol.Width.Value >= 30 && Math.Abs(col.Width.Value - templateCol.Width.Value) > 0.5)
                    {
                        col.Width = templateCol.Width;
                    }
                }
            };

            // Sync from ViewModel to View if ViewModel width changes programmatically
            System.ComponentModel.PropertyChangedEventHandler colWidthHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(SpreadsheetColumnViewModel.Width))
                {
                    if (col.Width.IsAbsolute && col.Width.Value >= 30 && Math.Abs(templateCol.Width.Value - col.Width.Value) > 0.5)
                    {
                        templateCol.Width = col.Width;
                    }
                }
            };
            col.PropertyChanged += colWidthHandler;
            _columnCleanups.Add(() => col.PropertyChanged -= colWidthHandler);

            // 1. Dropdown Column
            if (col.IsDropdown)
            {
                // Cell Template (Display)
                templateCol.CellTemplate = CreateCellDisplayTemplate(vm, col);

                // Editing Template (ComboBox) - sized to the cell so row height never jumps
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var comboBox = new ComboBox 
                    { 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        MinHeight = 0,
                        FontSize = 12,
                        MaxDropDownHeight = 220
                    };

                    var items = new List<string>(col.DropdownItems);
                    var curVal = (row != null && col.Index < row.Cells.Count) ? row.Cells[col.Index].Value : null;
                    if (!string.IsNullOrEmpty(curVal) && !items.Contains(curVal))
                    {
                        items.Add(curVal);
                    }
                    if (!items.Contains(""))
                    {
                        items.Insert(0, "");
                    }
                    comboBox.ItemsSource = items;
                    comboBox.Bind(ComboBox.SelectedItemProperty, new Binding($"Cells[{col.Index}].Value") { Mode = BindingMode.TwoWay });

                    return comboBox;
                });
            }
            // 2. Date Column
            else if (col.IsDate)
            {
                // Cell Template (Display)
                templateCol.CellTemplate = CreateCellDisplayTemplate(vm, col);

                // Editing Template (Wide TODAY Button + Manual Date Picker Button, no typing)
                // All controls are stretch-filled so entering edit mode never changes width/height.
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var grid = new Grid 
                    { 
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Margin = new Thickness(1),
                        ClipToBounds = true
                    };

                    var todayBtn = new Button 
                    { 
                        Content = "TODAY",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 10,
                        Padding = new Thickness(4, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        MinHeight = 0,
                        MinWidth = 0,
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1)
                    };
                    todayBtn.Classes.Add("DateBtn");
                    ToolTip.SetTip(todayBtn, "Isi dengan tanggal hari ini");
                    
                    todayBtn.Click += (s, e) => 
                    {
                        if (col.Index < row.Cells.Count)
                        {
                            row.Cells[col.Index].Value = DateTime.Today.ToString("yyyy-MM-dd");
                        }
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (_dataGrid != null)
                                {
                                    _dataGrid.CommitEdit();
                                }
                            }
                            catch { }
                        });
                    };

                    Grid.SetColumn(todayBtn, 0);

                    var manualBtn = new Button 
                    { 
                        Padding = new Thickness(4, 0),
                        Margin = new Thickness(2, 0, 0, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        MinHeight = 0,
                        MinWidth = 0,
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1)
                    };
                    manualBtn.Classes.Add("DateBtn");
                    ToolTip.SetTip(manualBtn, "Pilih tanggal manual...");

                    var icon = new PathIcon { Width = 13, Height = 13 };
                    if (Application.Current?.TryFindResource("IconCalendarFilled", out var iconRes) == true && iconRes is Avalonia.Media.Geometry geom)
                    {
                        icon.Data = geom;
                    }
                    manualBtn.Content = icon;

                    var flyout = new Flyout
                    {
                        Placement = PlacementMode.BottomEdgeAlignedRight
                    };
                    var calendar = new Avalonia.Controls.Calendar
                    {
                        SelectionMode = CalendarSelectionMode.SingleDate
                    };
                    if (col.Index < row.Cells.Count && DateTime.TryParse(row.Cells[col.Index].Value, out var curDate))
                    {
                        calendar.SelectedDate = curDate;
                        calendar.DisplayDate = curDate;
                    }
                    else
                    {
                        calendar.SelectedDate = DateTime.Today;
                        calendar.DisplayDate = DateTime.Today;
                    }

                    calendar.SelectedDatesChanged += (s, e) =>
                    {
                        if (calendar.SelectedDate.HasValue && col.Index < row.Cells.Count)
                        {
                            row.Cells[col.Index].Value = calendar.SelectedDate.Value.ToString("yyyy-MM-dd");
                            flyout.Hide();
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    if (_dataGrid != null)
                                    {
                                        _dataGrid.CommitEdit();
                                    }
                                }
                                catch { }
                            });
                        }
                    };

                    flyout.Content = calendar;
                    manualBtn.Flyout = flyout;

                    Grid.SetColumn(manualBtn, 1);

                    grid.Children.Add(todayBtn);
                    grid.Children.Add(manualBtn);

                    return grid;
                });
            }
            // 3. Text Column (Default) - Converted to TemplateColumn
            else
            {
                // Cell Template (Display)
                templateCol.CellTemplate = CreateCellDisplayTemplate(vm, col);

                // Editing Template (TextBox) - fills the cell exactly, so row height stays stable
                templateCol.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
                {
                    var textBox = new TextBox 
                    { 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        TextAlignment = Avalonia.Media.TextAlignment.Center,
                        MinHeight = 0,
                        Margin = new Thickness(-6, -4),
                        Padding = new Thickness(6, 4),
                        FontSize = 12
                    };
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

    /// <summary>
    /// Shared Excel-like display cell: centered text plus a small "fill handle"
    /// square rendered on the active cell's corner (toggled via the .fh styles).
    /// </summary>
    private Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel> CreateCellDisplayTemplate(SpreadsheetViewModel vm, SpreadsheetColumnViewModel col)
    {
        return new Avalonia.Controls.Templates.FuncDataTemplate<SpreadsheetRowViewModel>((row, ns) =>
        {
            // Wrap in Border to capture clicks on full cell area and store Tag
            var border = new Border { Tag = col.Index };
            border.Bind(Border.BackgroundProperty, new Binding($"Cells[{col.Index}].Background") { FallbackValue = Avalonia.Media.Brushes.Transparent });

            var grid = new Grid();

            var textBlock = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Margin = new Thickness(4, 0),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            textBlock.Bind(TextBlock.TextProperty, new Binding($"Cells[{col.Index}].Value"));
            textBlock.Bind(TextBlock.FontWeightProperty, new Binding($"Cells[{col.Index}].FontWeight") { FallbackValue = Avalonia.Media.FontWeight.Normal, TargetNullValue = Avalonia.Media.FontWeight.Normal });
            textBlock.Bind(TextBlock.FontStyleProperty, new Binding($"Cells[{col.Index}].FontStyle") { FallbackValue = Avalonia.Media.FontStyle.Normal, TargetNullValue = Avalonia.Media.FontStyle.Normal });
            grid.Children.Add(textBlock);

            // Fill handle: tiny accent square, visible only on the current cell
            var handle = new Border
            {
                Classes = { "fh" },
                Width = 9,
                Height = 9,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(2, 2, 1, 1),
                CornerRadius = new CornerRadius(1.5),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };
            if (Application.Current?.TryFindResource("AccentColorBrush", out var accent) == true && accent is Avalonia.Media.IBrush accentBrush)
                handle.Background = accentBrush;
            else
                handle.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3B82F6"));
            if (Application.Current?.TryFindResource("TextOnPrimaryBrush", out var onPrimary) == true && onPrimary is Avalonia.Media.IBrush onPrimaryBrush)
                handle.BorderBrush = onPrimaryBrush;
            grid.Children.Add(handle);

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// A single click must never drop the cell into edit mode.
    /// Editing is only allowed through a double-click, F2, or a programmatic BeginEdit.
    /// </summary>
    private void OnDataGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.EditingEventArgs is Avalonia.Input.PointerPressedEventArgs pp && pp.ClickCount < 2)
            e.Cancel = true;
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
            if (DataContext is SpreadsheetViewModel vm && _dataGrid != null && _focusedColumnIndex.HasValue)
            {
                var colIndex = _focusedColumnIndex.Value;
                var selectedRows = _dataGrid.SelectedItems.Cast<SpreadsheetRowViewModel>().ToList();
                
                if (selectedRows.Count > 0)
                {
                    foreach (var row in selectedRows)
                    {
                        if (colIndex >= 0 && colIndex < row.Cells.Count)
                        {
                            row.Cells[colIndex].Value = ""; // Clear content
                        }
                    }
                    vm.StatusText = $"Cleared {selectedRows.Count} cells in focused column.";
                    e.Handled = true;
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
                if (DataContext is SpreadsheetViewModel vm && tagIndex >= 0 && tagIndex < vm.Columns.Count)
                {
                    _focusedColumnIndex = tagIndex;
                    System.Diagnostics.Debug.WriteLine($"PointerPressed: Forced Focus to Column {tagIndex}");
                    break;
                }
            }
            // Stop if we hit the DataGrid itself (avoid finding unrelated tags)
            if (visual is DataGrid) break;
            
            visual = visual.GetVisualParent();
        }

        // Track if user is extending selection (Shift/Ctrl held)
        // This prevents CurrentCellChanged from overwriting _focusedColumnIndex
        _isExtendingSelection = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) 
                             || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);

        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateCellSelectionVisuals, Avalonia.Threading.DispatcherPriority.Render);

        // Only handle double-click (ClickCount >= 2)
        // Skip if Shift or Ctrl held (those are for selection)
        if (e.ClickCount >= 2 && !_isExtendingSelection)
        {
            if (_dataGrid != null && DataContext is SpreadsheetViewModel vm)
            {
                _dataGrid.IsReadOnly = false;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _dataGrid?.BeginEdit();
                    }
                    catch { }
                }, Avalonia.Threading.DispatcherPriority.Input);
                vm.StatusText = "Editing...";
            }
        }
    }

    private void OnDataGridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (DataContext is SpreadsheetViewModel vm)
        {
            vm.StatusText = "Ready";
        }
    }

    private void OnDataGridCurrentCellChanged(object? sender, EventArgs e)
    {
        if (_dataGrid == null || DataContext is not SpreadsheetViewModel vm) return;
        
        // DON'T update focused column during Shift/Ctrl selection extension
        // User clicked on column X first, then Shift+Clicked to extend rows
        // We want to keep column X as the fill target
        if (!_isExtendingSelection)
        {
            // Track which column is currently focused
            var currentColumn = _dataGrid.CurrentColumn;
            if (currentColumn != null && currentColumn.Tag is int colIndex)
            {
                _focusedColumnIndex = colIndex;
            }
            else
            {
                _focusedColumnIndex = null;
            }
        }

        UpdateCellSelectionVisuals();
    }

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCellSelectionVisuals();
    }

    /// <summary>
    /// Highlights ONLY the cells in the active/focused column for selected rows,
    /// so multiple selection does not highlight the entire horizontal rows.
    /// </summary>
    private void UpdateCellSelectionVisuals()
    {
        if (_dataGrid == null) return;

        var selectedItems = _dataGrid.SelectedItems;
        bool hasMultiple = selectedItems != null && selectedItems.Count > 1;
        int? targetCol = _focusedColumnIndex;

        foreach (var desc in _dataGrid.GetVisualDescendants())
        {
            if (desc is DataGridRow dataGridRow)
            {
                bool isRowSelected = hasMultiple && selectedItems!.Contains(dataGridRow.DataContext);

                var cellsPresenter = dataGridRow.GetVisualDescendants()
                    .OfType<DataGridCellsPresenter>().FirstOrDefault();
                if (cellsPresenter == null) continue;

                for (int i = 0; i < cellsPresenter.Children.Count; i++)
                {
                    if (cellsPresenter.Children[i] is not DataGridCell cell) continue;

                    var column = DataGridColumn.GetColumnContainingElement(cell);
                    int colIndex = column?.Tag is int tag ? tag : -1;
                    bool isCellSelected = isRowSelected && targetCol.HasValue && colIndex == targetCol.Value;

                    if (isCellSelected)
                    {
                        if (!cell.Classes.Contains("cell-selected"))
                            cell.Classes.Add("cell-selected");
                    }
                    else
                    {
                        if (cell.Classes.Contains("cell-selected"))
                            cell.Classes.Remove("cell-selected");
                    }
                }
            }
        }
    }

    private void OnDataGridPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        SyncActualColumnWidthsToViewModel();
        if (DataContext is SpreadsheetViewModel vm && !vm.IsLoading)
        {
            _ = vm.SaveColumnWidthsAsync();
        }
    }

    private void OnDataGridPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        SyncActualColumnWidthsToViewModel();
        if (DataContext is SpreadsheetViewModel vm && !vm.IsLoading)
        {
            _ = vm.SaveColumnWidthsAsync();
        }
    }

    private void SyncActualColumnWidthsToViewModel()
    {
        if (_dataGrid == null || DataContext is not SpreadsheetViewModel vm || vm.IsLoading) return;

        bool changed = false;
        foreach (var dgc in _dataGrid.Columns)
        {
            if (dgc.Tag is int idx && idx >= 0 && idx < vm.Columns.Count)
            {
                var colVM = vm.Columns[idx];
                // Prioritize explicit user-dragged width if set and absolute
                double w = (dgc.Width.IsAbsolute && dgc.Width.Value >= 30)
                    ? dgc.Width.Value
                    : (dgc.ActualWidth >= 30 ? dgc.ActualWidth : 0);

                bool isDifferent = !colVM.Width.IsAbsolute || Math.Abs(colVM.Width.Value - w) > 0.5;
                if (w >= 30 && isDifferent)
                {
                    colVM.Width = new DataGridLength(Math.Round(w, 1), DataGridLengthUnitType.Pixel);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _ = vm.SaveColumnWidthsAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SyncActualColumnWidthsToViewModel();
        if (DataContext is SpreadsheetViewModel vm)
        {
            _ = vm.SaveColumnWidthsAsync();
        }
        base.OnDetachedFromVisualTree(e);
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

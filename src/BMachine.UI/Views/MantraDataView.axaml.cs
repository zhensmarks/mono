using System;
using System;
using System.Globalization;
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
using Avalonia.Data.Converters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;
using ImageMagick;
using BMachine.UI.ViewModels;
using BMachine.UI.Views.Dialogs.MantraData;

namespace BMachine.UI.Views;

public partial class MantraDataView : UserControl
{
    private MantraDataViewModel? _viewModel;
    private string _activeContextColumn = string.Empty;
    private List<string> _lastSelectedColumns = new();

    private readonly HashSet<(TableDataRow Row, string Column)> _selectedCells = new();
    private int _anchorRowIdx = -1;
    private int _anchorColIdx = -1;

    private readonly Dictionary<string, Action> _menuActions = new();
    private readonly List<(KeyGesture Gesture, Action Action)> _activeShortcuts = new();

    public MantraDataView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        InitializeMenuActions();
        RebuildShortcuts();

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnDataGridPointerTunnel, RoutingStrategies.Tunnel);

        Loaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var sv = MainDataGrid.GetVisualDescendants()
                    .OfType<ScrollViewer>().FirstOrDefault();
                if (sv != null)
                    sv.ScrollChanged += (_, _) => RefreshCellVisuals();
            });
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel != null) UpdateSegTabHighlight(_viewModel.PhotoStatusFilter);
                if (_viewModel != null) UpdateSegTabHighlight(_viewModel.PhotoStatusFilter);
                ApplyContextMenuSettings();
            });
        };
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
            UpdateSegTabHighlight(vm.PhotoStatusFilter);
            UpdateSegTabHighlight(vm.PhotoStatusFilter);
            ApplyContextMenuSettings();
            RebuildShortcuts();
        }
    }

    private void WireViewModelDialogs(MantraDataViewModel vm)
    {
vm.RequestProcessPsdDialogFunc = async () =>
        {
            var dlg = new ProcessPsdWindow(
                !string.IsNullOrWhiteSpace(vm.MasterPsdFolderPath) ? vm.MasterPsdFolderPath : vm.Settings.LastPsdFolder,
                !string.IsNullOrWhiteSpace(vm.PhotoFolderPath) ? vm.PhotoFolderPath : vm.Settings.LastPhotoFolder,
                vm.Columns.ToList());
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            if (dlg.Confirmed)
                return (dlg.PsdFolder, dlg.PhotoFolder, dlg.IsRevision, dlg.RevisionFields);

            return (null, null, false, new List<string>());
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

        vm.RequestTransformFunc = async (headers, rows) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var sample = rows.Take(8)
                .Select(r => headers.Select(h => r.Values.TryGetValue(h, out var v) ? v ?? "" : "").ToList())
                .ToList();

            var preselected = new List<string>();
            var cur = GetCurrentOrSelectedColumn();
            if (!string.IsNullOrEmpty(cur)) preselected.Add(cur);

            var dlg = new TransformWindow(headers, sample, _viewModel.TransformService, _viewModel.AiService, _viewModel.Settings, preselected);
            dlg.PreviewChanged += () => _viewModel.ApplyTransformPreview(headers, dlg.BuildSpecs());
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return new TransformDialogResult(
                dlg.Confirmed,
                dlg.Layers
            );
        };

        vm.RequestCleanerFunc = async (suggestions) =>
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            var dlg = new CleanerWindow(suggestions);
            if (topLevel != null)
                await dlg.ShowDialog(topLevel);
            else
                dlg.Show();

            return new CleanerDialogResult(dlg.Applied, dlg.SelectedCodes);
        };

        vm.RequestManualPhotoFunc = async (row) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Pilih foto untuk {row["Nama"]}",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Foto") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png" } }
                }
            });

            return files.Count > 0 ? files[0].Path.LocalPath : null;
        };

        vm.RequestMasterPsdFolderFunc = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pilih folder master PSD",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        vm.RequestPhotoFolderFunc = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pilih folder foto",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
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
                Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
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

// Edit Template (identik dengan tampilan: transparan, tanpa border ekstra, padding sama)
            templateCol.CellEditingTemplate = new FuncDataTemplate<TableDataRow>((initialRow, ns) =>
            {
                var textBox = new TextBox
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Background = Brushes.Transparent,
                    Foreground = SolidColorBrush.Parse("#EDEDED"),
                    BorderThickness = new Thickness(0),
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(6, 4),
                    TextWrapping = TextWrapping.Wrap,
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

        var reviewCol = new DataGridTemplateColumn
        {
            Header = "Foto",
            Width = new DataGridLength(320),
            CanUserSort = false
        };
        reviewCol.CellTemplate = new FuncDataTemplate<TableDataRow>((row, ns) =>
        {
            // Kartu foto: thumbnail potret 3:4, nama file, pill status, dan tombol pilih.
            var card = new Border
            {
                Background = SolidColorBrush.Parse("#141418"),
                BorderBrush = SolidColorBrush.Parse("#27272C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7),
                Margin = new Thickness(2, 3),
                ClipToBounds = true
            };
            var grid = new Grid
            {
                ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("Auto, *, Auto"),
                Margin = new Thickness(0)
            };

            var thumbClip = new Border
            {
                Width = 36,
                Height = 48,
                CornerRadius = new CornerRadius(6),
                Background = SolidColorBrush.Parse("#0C0C0F"),
                BorderBrush = SolidColorBrush.Parse("#2A2A31"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                ClipToBounds = true
            };
            var thumbGrid = new Grid();
            var image = new Image
            {
                Stretch = Avalonia.Media.Stretch.UniformToFill,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                IsVisible = false
            };
            var placeholder = new TextBlock
            {
                Text = "BELUM\nADA FOTO",
                FontSize = 9.5,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("#56565F"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
            };
            thumbGrid.Children.Add(image);
            thumbGrid.Children.Add(placeholder);
            thumbClip.Child = thumbGrid;
            var infoPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Vertical,
                Spacing = 4,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0)
            };

            var fileName = new TextBlock
            {
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = SolidColorBrush.Parse("#D4D4D8")
            };

            var pill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1.5),
                Background = SolidColorBrush.Parse("#21262D"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            var pillText = new TextBlock
            {
                FontSize = 9,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("#8B949E"),
                Text = "BELUM DIPERIKSA"
            };
            pill.Child = pillText;

            infoPanel.Children.Add(fileName);
            infoPanel.Children.Add(pill);

            var btnPilih = new Button
            {
                Content = "Pilih",
                FontSize = 10,
                Padding = new Thickness(7, 3),
                MinHeight = 22,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = SolidColorBrush.Parse("#1E1E24"),
                Foreground = SolidColorBrush.Parse("#E4E4E7"),
                BorderBrush = SolidColorBrush.Parse("#2E2E36"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            btnPilih.Command = _viewModel.SelectManualPhotoCommand;

            static void LoadThumbnailAsync(Image img, string path)
            {
                Task.Run(() =>
                {
                    Avalonia.Media.Imaging.Bitmap? bmp = null;
                    try
                    {
                        // Avalonia decode mengabaikan EXIF orientation: foto potret dari
                        // handphone tampil miring. AutoOrient lewat Magick dulu.
                        using var magick = new MagickImage(path);
                        magick.AutoOrient();
                        magick.Thumbnail(new MagickGeometry(256, 256));
                        using var ms = new MemoryStream();
                        magick.Format = MagickFormat.Png;
                        magick.Write(ms);
                        ms.Position = 0;
                        bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                    }
                    catch
                    {
                        try
                        {
                            using var stream = File.OpenRead(path);
                            bmp = new Avalonia.Media.Imaging.Bitmap(stream);
                        }
                        catch { bmp = null; }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (img.Tag is string tag && tag == path)
                        {
                            img.Source = bmp;
                            img.IsVisible = bmp != null;
                        }
                    });
                });
            }

            void SetStatus(string text, string textHex, string pillArgb, string cardHex)
            {
                pillText.Text = text;
                pillText.Foreground = SolidColorBrush.Parse(textHex);
                pill.Background = SolidColorBrush.Parse(pillArgb);
                card.BorderBrush = SolidColorBrush.Parse(cardHex);
            }

            void UpdateDisplay(TableDataRow? r)
            {
                var path = r != null ? r["_MATCHED_PHOTO_PATH"] : string.Empty;

                static void DropBitmap(Image img)
                {
                    if (img.Source is Avalonia.Media.Imaging.Bitmap old) old.Dispose();
                    img.Source = null;
                    img.Tag = null;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    DropBitmap(image);
                    image.IsVisible = false;
                    placeholder.IsVisible = true;
                    fileName.Text = "Belum ada foto";
                    fileName.Foreground = SolidColorBrush.Parse("#8B949E");
                    Avalonia.Controls.ToolTip.SetTip(fileName, "Tidak ada kandidat foto untuk baris ini");
                    SetStatus("BELUM ADA", "#FBBF24", "#2A2410", "#27272C");
                    return;
                }

                if (!File.Exists(path))
                {
                    DropBitmap(image);
                    image.IsVisible = false;
                    placeholder.IsVisible = true;
                    fileName.Text = Path.GetFileName(path) + " (hilang)";
                    fileName.Foreground = SolidColorBrush.Parse("#F87171");
                    SetStatus("FILE HILANG", "#F87171", "#3A1B1D", "#5C1F22");
                    return;
                }

                placeholder.IsVisible = false;
                fileName.Text = Path.GetFileName(path);
                fileName.Foreground = SolidColorBrush.Parse("#E4E4E7");
                Avalonia.Controls.ToolTip.SetTip(fileName, r!.MatchNote);

                if (r!.IsPhotoMatched)
                    SetStatus($"COCOK {r.MatchScore}", "#3FB950", "#0D2818", "#1F4D2E");
                else
                    SetStatus($"REVIEW {r.MatchScore}", "#FBBF24", "#2A2410", "#3A3118");

                // Foto yang sama tidak perlu di-decode ulang (UpdateDisplay dipanggil
                // setiap ada perubahan cell di baris yang sama).
                if (image.Tag is string currentPath && currentPath == path && image.Source != null)
                    return;

                DropBitmap(image);
                image.Tag = path;
                LoadThumbnailAsync(image, path);
            }

            TableDataRow? boundRow = null;
            System.ComponentModel.PropertyChangedEventHandler? rowHandler = null;

            void AttachRow(TableDataRow? newRow)
            {
                if (boundRow != null && rowHandler != null)
                {
                    boundRow.PropertyChanged -= rowHandler;
                }

                boundRow = newRow;

                if (boundRow != null)
                {
                    btnPilih.CommandParameter = newRow;
                    rowHandler = (s, pe) =>
                    {
                        if (pe.PropertyName == "Item[]" || pe.PropertyName == nameof(TableDataRow.Values))
                        {
                            UpdateDisplay(boundRow);
                        }
                    };
                    boundRow.PropertyChanged += rowHandler;
                }

                UpdateDisplay(boundRow);
            }

            grid.DataContextChanged += (s, e) =>
            {
                AttachRow(grid.DataContext as TableDataRow);
            };

            grid.DetachedFromVisualTree += (s, e) =>
            {
                AttachRow(null);
            };

            if (row != null)
            {
                AttachRow(row);
            }

            Grid.SetColumn(thumbClip, 0);
            Grid.SetColumn(infoPanel, 1);
            Grid.SetColumn(btnPilih, 2);
            grid.Children.Add(thumbClip);
            grid.Children.Add(infoPanel);
            grid.Children.Add(btnPilih);
            card.Child = grid;
            return card;
            return card;
        });
        MainDataGrid.Columns.Add(reviewCol);
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
                new("Data Files (*.xlsx, *.xls, *.xlsm, *.csv, *.tsv, *.txt, *.docx)") { Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm", "*.csv", "*.tsv", "*.txt", "*.docx" } },
                new("Excel Files (*.xlsx, *.xls, *.xlsm, *.csv, *.tsv)") { Patterns = new[] { "*.xlsx", "*.xls", "*.xlsm", "*.csv", "*.tsv" } },
                new("Text/CSV Files (*.csv, *.tsv, *.txt)") { Patterns = new[] { "*.csv", "*.tsv", "*.txt" } },
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

        if (_selectedCells.Count > 0)
        {
            var firstCol = _selectedCells.First().Column;
            if (!string.IsNullOrEmpty(firstCol) && _viewModel != null && _viewModel.Columns.Contains(firstCol))
                return firstCol;
        }

        if (MainDataGrid.CurrentColumn?.Header is string header && _viewModel != null && _viewModel.Columns.Contains(header))
            return header;

        var lastCol = _lastSelectedColumns.FirstOrDefault();
        if (!string.IsNullOrEmpty(lastCol))
            return lastCol;

        return _viewModel?.Columns.FirstOrDefault();
    }

    private List<TableDataRow> GetSelectedRows()
    {
        if (_selectedCells.Count > 0)
            return _selectedCells.Select(c => c.Row).Distinct().ToList();
        if (MainDataGrid.SelectedItem is TableDataRow single)
            return new List<TableDataRow> { single };
        return new List<TableDataRow>();
    }

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        var distinctRows = _selectedCells.Select(c => c.Row).Distinct().Count();
        _viewModel.SelectedRowCount = distinctRows;
    }

private void OnDataGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.EditingEventArgs is PointerPressedEventArgs pp && pp.ClickCount < 2)
            e.Cancel = true;
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

        RefreshCellVisuals();
    }

    // Keyboard Shortcuts (Ctrl+C, Ctrl+V, Del, etc.)
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            // Allow native textbox editing
            return;
        }

        // 1. Check custom context menu shortcuts
        foreach (var (gesture, action) in _activeShortcuts)
        {
            if (gesture.Matches(e))
            {
                action();
                e.Handled = true;
                return;
            }
        }

        // 2. Built-in global shortcuts (Undo, Redo, ClearData, NewTable, OpenFile, ProcessPhotoshop, SelectAll, DeleteSelectedRows)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Z)
            {
                _viewModel?.Redo();
                e.Handled = true;
            }
            else if (e.Key == Key.Z)
            {
                _viewModel?.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.W)
            {
                _viewModel?.ClearData();
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                HandleNewTableAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                OnOpenFileClicked(null, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                HandleProcessPhotoshopAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                SelectAllCells();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            DeleteSelectedRows();
            e.Handled = true;
        }
    }

private async void CopySelectedToClipboard()
    {
        if (_viewModel == null) return;

        if (_selectedCells.Count == 0)
        {
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
            return;
        }

        var rowIndices = new Dictionary<TableDataRow, int>();
        for (int i = 0; i < _viewModel.FilteredRows.Count; i++)
            rowIndices[_viewModel.FilteredRows[i]] = i;

        var colIndices = new Dictionary<string, int>();
        for (int i = 0; i < _viewModel.Columns.Count; i++)
            colIndices[_viewModel.Columns[i]] = i;

        var sortedCells = _selectedCells
            .Where(c => rowIndices.ContainsKey(c.Row) && colIndices.ContainsKey(c.Column))
            .OrderBy(c => rowIndices[c.Row])
            .ThenBy(c => colIndices[c.Column])
            .ToList();

        if (sortedCells.Count == 0) return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard == null) return;

        var sb2 = new StringBuilder();
        foreach (var rowGroup in sortedCells.GroupBy(c => c.Row))
        {
            var cellTexts = rowGroup.Select(c => c.Row[c.Column] ?? "").ToList();
            sb2.AppendLine(string.Join("\t", cellTexts));
        }

        var resultText = sb2.ToString().TrimEnd('\r', '\n');
        await top.Clipboard.SetTextAsync(resultText);
        _viewModel.StatusMessage = sortedCells.Count == 1
            ? "1 sel disalin ke clipboard."
            : $"{sortedCells.Count} sel disalin ke clipboard.";
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

        if (_selectedCells.Count > 0)
        {
            var firstCell = _selectedCells.First();
            startRowIndex = _viewModel.FilteredRows.IndexOf(firstCell.Row);
            if (startRowIndex < 0) startRowIndex = 0;
        }
        else if (MainDataGrid.SelectedIndex >= 0)
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
        if (_selectedCells.Count == 0) return;

        var cells = _selectedCells.ToList();
        _viewModel.ClearCells(cells);
        _selectedCells.Clear();
        RefreshCellVisuals();
        _viewModel.SelectedRowCount = 0;
        _viewModel.StatusMessage = $"{cells.Count} sel dikosongkan.";
    }

private void DeleteSelectedRows()
    {
        if (_viewModel == null) return;
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;
        _viewModel.DeleteRows(rows);
        _selectedCells.Clear();
        RefreshCellVisuals();
        _viewModel.SelectedRowCount = 0;
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

    private void OnMenuAutoFitClicked(object? sender, RoutedEventArgs e)
    {
        if (MainDataGrid == null) return;
        foreach (var col in MainDataGrid.Columns)
        {
            col.Width = new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.SizeToCells);
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
    private void InitializeMenuActions()
    {
        _menuActions["Copy"] = () => CopySelectedToClipboard();
        _menuActions["Paste"] = () => PasteFromClipboard();
        _menuActions["ClearCells"] = () => ClearSelectedCells();
        _menuActions["CustomMerge"] = () => OnMenuCustomMergeClicked(null, new RoutedEventArgs());
        _menuActions["TitleCase"] = () => OnMenuTitleCaseClicked(null, new RoutedEventArgs());
        _menuActions["DateFormatFull"] = () => OnMenuDateFormatFullClicked(null, new RoutedEventArgs());
        _menuActions["PhonePrefix"] = () => OnMenuPhonePrefixClicked(null, new RoutedEventArgs());
        _menuActions["FindReplace"] = () => OnMenuFindReplaceClicked(null, new RoutedEventArgs());
        _menuActions["SplitColumn"] = () => OnMenuSplitColumnClicked(null, new RoutedEventArgs());
        _menuActions["InsertColumnLeft"] = () => OnMenuInsertColumnLeftClicked(null, new RoutedEventArgs());
        _menuActions["InsertColumnRight"] = () => OnMenuInsertColumnRightClicked(null, new RoutedEventArgs());
        _menuActions["MoveColumnLeft"] = () => OnMenuMoveColumnLeftClicked(null, new RoutedEventArgs());
        _menuActions["MoveColumnRight"] = () => OnMenuMoveColumnRightClicked(null, new RoutedEventArgs());
        _menuActions["RenameColumn"] = () => OnMenuRenameColumnClicked(null, new RoutedEventArgs());
        _menuActions["DeleteColumn"] = () => OnMenuDeleteColumnClicked(null, new RoutedEventArgs());
        _menuActions["AutoFit"] = () => OnMenuAutoFitClicked(null, new RoutedEventArgs());
        _menuActions["InsertRow"] = () => OnMenuInsertRowClicked(null, new RoutedEventArgs());
        _menuActions["DeleteRows"] = () => OnMenuDeleteRowsClicked(null, new RoutedEventArgs());
        _menuActions["ColorGreen"] = () => OnMenuColorGreenClicked(null, new RoutedEventArgs());
        _menuActions["ColorBlue"] = () => OnMenuColorBlueClicked(null, new RoutedEventArgs());
        _menuActions["ColorAmber"] = () => OnMenuColorAmberClicked(null, new RoutedEventArgs());
        _menuActions["ColorRed"] = () => OnMenuColorRedClicked(null, new RoutedEventArgs());
        _menuActions["ColorClear"] = () => OnMenuColorClearClicked(null, new RoutedEventArgs());
    }

    private void RebuildShortcuts()
    {
        _activeShortcuts.Clear();
        var settings = _viewModel?.Settings ?? MantraDataSettings.Load();

        foreach (var (key, _, _, _) in MantraContextMenuSettingsViewModel.DefaultMenuItems)
        {
            var shortcutStr = settings.GetMenuShortcut(key);
            if (!string.IsNullOrWhiteSpace(shortcutStr) && _menuActions.TryGetValue(key, out var action))
            {
                try
                {
                    var gesture = KeyGesture.Parse(shortcutStr);
                    _activeShortcuts.Add((gesture, action));
                }
                catch { }
            }
        }
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        ApplyContextMenuSettings();
    }

    private void ApplyContextMenuSettings()
    {
        var cm = MainDataGrid?.ContextMenu;
        if (cm == null) return;
        var settings = _viewModel?.Settings ?? MantraDataSettings.Load();

        foreach (var obj in cm.Items)
        {
            if (obj is MenuItem mi)
            {
                if (mi.Tag is string key && !string.IsNullOrEmpty(key))
                {
                    mi.IsVisible = settings.GetMenuVisibility(key);
                    var sc = settings.GetMenuShortcut(key);
                    try
                    {
                        mi.InputGesture = string.IsNullOrWhiteSpace(sc) ? null : KeyGesture.Parse(sc);
                    }
                    catch
                    {
                        mi.InputGesture = null;
                    }
                }

                if (mi.Items.Count > 0)
                {
                    foreach (var sub in mi.Items)
                    {
                        if (sub is MenuItem subMi && subMi.Tag is string subKey && !string.IsNullOrEmpty(subKey))
                        {
                            subMi.IsVisible = settings.GetMenuVisibility(subKey);
                            var subSc = settings.GetMenuShortcut(subKey);
                            try
                            {
                                subMi.InputGesture = string.IsNullOrWhiteSpace(subSc) ? null : KeyGesture.Parse(subSc);
                            }
                            catch
                            {
                                subMi.InputGesture = null;
                            }
                        }
                    }
                }
            }
        }

        UpdateSeparatorVisibility(cm);
    }

    private static void UpdateSeparatorVisibility(ContextMenu cm)
    {
        bool hasVisibleItemInGroup = false;
        Separator? lastSeparator = null;

        for (int i = 0; i < cm.Items.Count; i++)
        {
            var item = cm.Items[i];
            if (item is Separator sep)
            {
                sep.IsVisible = hasVisibleItemInGroup;
                hasVisibleItemInGroup = false;
                lastSeparator = sep;
            }
            else if (item is MenuItem mi && mi.IsVisible)
            {
                hasVisibleItemInGroup = true;
            }
        }

        if (!hasVisibleItemInGroup && lastSeparator != null)
        {
            lastSeparator.IsVisible = false;
        }
    }

    private async void OnMenuConfigureContextMenuClicked(object? sender, RoutedEventArgs e)
    {
        var settings = _viewModel?.Settings ?? MantraDataSettings.Load();
        var vm = new MantraContextMenuSettingsViewModel(settings);
        var dlg = new MantraContextMenuSettingsDialog(vm);

        var topLevel = TopLevel.GetTopLevel(this) as Window;
        if (topLevel != null)
            await dlg.ShowDialog(topLevel);
        else
            dlg.Show();

        if (dlg.Confirmed)
        {
            ApplyContextMenuSettings();
            RebuildShortcuts();
        }
    }

    // --- EXCEL-LIKE CELL SELECTION (Custom Layer) ---
    private void OnDataGridPointerTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBox) return;
        if (_viewModel == null || _viewModel.Rows.Count == 0) return;

        var source = e.Source as Visual;
        if (source == null) return;

        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        var colHeader = FindAncestor<DataGridColumnHeader>(source);
        if (colHeader != null)
        {
            HandleColumnHeaderClick(colHeader, isCtrl, isShift);
            e.Handled = true;
            return;
        }

        var rowHeader = FindAncestor<DataGridRowHeader>(source);
        if (rowHeader != null)
        {
            HandleRowHeaderClick(rowHeader, isCtrl, isShift);
            e.Handled = true;
            return;
        }

        if (!isCtrl)
        {
            _selectedCells.Clear();
        }

        var cell = FindAncestor<DataGridCell>(source);
        if (cell != null)
        {
            HandleCellClick(cell, isCtrl, isShift);
        }
    }

    private void HandleCellClick(DataGridCell cell, bool isCtrl, bool isShift)
    {
        if (_viewModel == null) return;
        var row = DataGridRow.GetRowContainingElement(cell);
        if (row == null || row.DataContext is not TableDataRow dataRow) return;

        int rowIdx = row.Index;
        var column = DataGridColumn.GetColumnContainingElement(cell);
        if (column == null) return;
        int colIdx = MainDataGrid.Columns.IndexOf(column);
        if (colIdx < 1 || colIdx >= MainDataGrid.Columns.Count) return;

        var colName = column.Header?.ToString();
        if (string.IsNullOrEmpty(colName) || colName == "#") return;

        if (isShift && _anchorRowIdx >= 0 && _anchorColIdx >= 0)
        {
            int minR = Math.Min(_anchorRowIdx, rowIdx);
            int maxR = Math.Max(_anchorRowIdx, rowIdx);
            int minC = Math.Min(_anchorColIdx, colIdx);
            int maxC = Math.Max(_anchorColIdx, colIdx);

            if (!isCtrl) _selectedCells.Clear();

            for (int r = minR; r <= maxR; r++)
            {
                if (r < 0 || r >= _viewModel.FilteredRows.Count) continue;
                var rRow = _viewModel.FilteredRows[r];
                for (int c = minC; c <= maxC; c++)
                {
                    if (c < 1 || c >= MainDataGrid.Columns.Count) continue;
                    var cName = MainDataGrid.Columns[c].Header?.ToString();
                    if (!string.IsNullOrEmpty(cName) && cName != "#")
                        _selectedCells.Add((rRow, cName));
                }
            }
        }
        else if (isCtrl)
        {
            var pair = (dataRow, colName);
            if (!_selectedCells.Remove(pair))
                _selectedCells.Add(pair);
            _anchorRowIdx = rowIdx;
            _anchorColIdx = colIdx;
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add((dataRow, colName));
            _anchorRowIdx = rowIdx;
            _anchorColIdx = colIdx;
        }

        RefreshCellVisuals();
        UpdateSelectionStatus();
    }

    private void HandleColumnHeaderClick(DataGridColumnHeader header, bool isCtrl, bool isShift)
    {
        if (_viewModel == null) return;

        var column = DataGridColumn.GetColumnContainingElement(header);
        if (column == null) return;
        int colIdx = MainDataGrid.Columns.IndexOf(column);
        if (colIdx < 1 || colIdx >= MainDataGrid.Columns.Count) return;

        var colName = column.Header?.ToString();
        if (string.IsNullOrEmpty(colName) || colName == "#") return;

        if (isShift && _anchorColIdx >= 0)
        {
            int minC = Math.Min(_anchorColIdx, colIdx);
            int maxC = Math.Max(_anchorColIdx, colIdx);
            _selectedCells.Clear();
            _lastSelectedColumns.Clear();
            foreach (var row in _viewModel.FilteredRows)
            {
                for (int c = minC; c <= maxC; c++)
                {
                    if (c < 1 || c >= MainDataGrid.Columns.Count) continue;
                    var cn = MainDataGrid.Columns[c].Header?.ToString();
                    if (!string.IsNullOrEmpty(cn) && cn != "#")
                    {
                        _selectedCells.Add((row, cn));
                        _lastSelectedColumns.Add(cn);
                    }
                }
            }
            _viewModel.StatusMessage = $"{_lastSelectedColumns.Count} kolom terpilih ({string.Join(", ", _lastSelectedColumns)}) - {_viewModel.FilteredRows.Count} baris.";
        }
        else
        {
            if (!isCtrl)
            {
                _selectedCells.Clear();
                _lastSelectedColumns.Clear();
            }
            foreach (var row in _viewModel.FilteredRows)
                _selectedCells.Add((row, colName));
            if (!_lastSelectedColumns.Contains(colName, StringComparer.OrdinalIgnoreCase))
                _lastSelectedColumns.Add(colName);
            _anchorColIdx = colIdx;
            _viewModel.StatusMessage = _lastSelectedColumns.Count > 1
                ? $"{_lastSelectedColumns.Count} kolom terpilih ({string.Join(", ", _lastSelectedColumns)})."
                : $"Kolom '{colName}' terpilih ({_viewModel.FilteredRows.Count} baris).";
        }

        RefreshCellVisuals();
        UpdateSelectionStatus();
    }

    private void HandleRowHeaderClick(DataGridRowHeader rowHeader, bool isCtrl, bool isShift)
    {
        if (_viewModel == null) return;
        var row = DataGridRow.GetRowContainingElement(rowHeader);
        if (row == null || row.DataContext is not TableDataRow dataRow) return;

        int rowIdx = row.Index;

        if (isShift && _anchorRowIdx >= 0)
        {
            int minR = Math.Min(_anchorRowIdx, rowIdx);
            int maxR = Math.Max(_anchorRowIdx, rowIdx);
            _selectedCells.Clear();
            for (int r = minR; r <= maxR; r++)
            {
                if (r < 0 || r >= _viewModel.FilteredRows.Count) continue;
                var rRow = _viewModel.FilteredRows[r];
                foreach (var col in _viewModel.Columns)
                    _selectedCells.Add((rRow, col));
            }
            _viewModel.StatusMessage = $"{(maxR - minR + 1)} baris terpilih (baris {minR + 1} s/d {maxR + 1}).";
        }
        else
        {
            if (!isCtrl) _selectedCells.Clear();
            foreach (var col in _viewModel.Columns)
                _selectedCells.Add((dataRow, col));
            _anchorRowIdx = rowIdx;
            _viewModel.StatusMessage = $"Baris {rowIdx + 1} terpilih (seluruh kolom).";
        }

        RefreshCellVisuals();
        UpdateSelectionStatus();
    }

    private void SelectAllCells()
    {
        if (_viewModel == null) return;
        _selectedCells.Clear();
        _lastSelectedColumns.Clear();
        foreach (var row in _viewModel.FilteredRows)
        {
            foreach (var col in _viewModel.Columns)
                _selectedCells.Add((row, col));
        }
        RefreshCellVisuals();
        UpdateSelectionStatus();
        _viewModel.StatusMessage = $"Semua sel terpilih ({_viewModel.FilteredRows.Count} baris x {_viewModel.Columns.Count} kolom).";
    }

    private void UpdateSelectionStatus()
    {
        if (_viewModel == null) return;
        var distinctRows = _selectedCells.Select(c => c.Row).Distinct().Count();
        _viewModel.SelectedRowCount = distinctRows;
        _lastSelectedColumns = _selectedCells.Select(c => c.Column).Distinct().ToList();
    }

    private void RefreshCellVisuals()
    {
        if (_viewModel == null || MainDataGrid == null) return;

        foreach (var desc in MainDataGrid.GetVisualDescendants())
        {
            if (desc is DataGridRow dataGridRow && dataGridRow.DataContext is TableDataRow row)
            {
                var cellsPresenter = dataGridRow.GetVisualDescendants()
                    .OfType<DataGridCellsPresenter>().FirstOrDefault();
                if (cellsPresenter == null) continue;

                for (int i = 0; i < cellsPresenter.Children.Count; i++)
                {
                    if (cellsPresenter.Children[i] is not DataGridCell cell) continue;
                    if (!cell.IsVisible) continue;

                    var column = DataGridColumn.GetColumnContainingElement(cell);
                    if (column == null) continue;
                    var colName = column.Header?.ToString();
                    if (string.IsNullOrEmpty(colName) || colName == "#") continue;

                    bool isSelected = _selectedCells.Contains((row, colName));

                    if (isSelected)
                    {
                        cell.Background = new SolidColorBrush(Color.Parse("#2A3F66"));
                        cell.BorderThickness = new Thickness(1);
                        cell.BorderBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
                    }
                    else
                    {
                        cell.Background = Brushes.Transparent;
                        cell.BorderThickness = new Thickness(1);
                        cell.BorderBrush = Brushes.Transparent;
                    }
                }
            }
        }
    }

    private static T? FindAncestor<T>(Visual? visual) where T : Visual
    {
        while (visual != null)
        {
            if (visual is T result) return result;
            visual = visual.GetVisualParent();
        }
        return null;
    }

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
                    if (ext is ".xlsx" or ".xls" or ".xlsm" or ".csv" or ".tsv" or ".txt" or ".docx" && _viewModel != null)
                    {
                        await _viewModel.LoadFileByPathAsync(path);
                        break;
                    }
                }
            }
        }
    }

    private async void HandleProcessPhotoshopAsync()
    {
        try
        {
            if (_viewModel != null)
                await _viewModel.ProcessPhotoshopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ProcessPhotoshopAsync error: {ex}");
        }
    }

    private async void HandleNewTableAsync()
    {
        try
        {
            if (_viewModel != null)
                await _viewModel.NewTableAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NewTableAsync error: {ex}");
        }
    }

    private void OnPhotoFilterTabClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && sender is Button btn && btn.Tag is string key && !string.IsNullOrEmpty(key))
        {
            _viewModel.PhotoStatusFilter = key;
            UpdateSegTabHighlight(key);
        }
    }

    private void UpdateSegTabHighlight(string activeKey)
    {
        var panel = this.FindControl<StackPanel>("SegTabPanel");
        if (panel == null) return;
        foreach (var child in panel.Children)
        {
            if (child is Button b)
            {
                bool isActive = string.Equals(b.Tag as string, activeKey, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    if (!b.Classes.Contains("Active")) b.Classes.Add("Active");
                }
                else
                {
                    b.Classes.Remove("Active");
                }
            }
        }
    }

    private static string? ExtractFolderFromDrag(DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return null;
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return null;
        var first = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(first)) return null;
        if (Directory.Exists(first)) return first;
        return File.Exists(first) ? Path.GetDirectoryName(first) : null;
    }

    private void OnFolderDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            var ok = ExtractFolderFromDrag(e) != null;
            b.BorderBrush = ok ? SolidColorBrush.Parse("#388BFD") : SolidColorBrush.Parse("#F87171");
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnFolderDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ExtractFolderFromDrag(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFolderDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderBrush = SolidColorBrush.Parse("#303038");
        e.Handled = true;
    }

    private void OnMasterPsdDrop(object? sender, DragEventArgs e)
    {
        var folder = ExtractFolderFromDrag(e);
        if (!string.IsNullOrEmpty(folder) && _viewModel != null)
            _viewModel.SetMasterPsdFolder(folder);
        if (sender is Border b) b.BorderBrush = SolidColorBrush.Parse("#303038");
        e.Handled = true;
    }

    private void OnPhotoDrop(object? sender, DragEventArgs e)
    {
        var folder = ExtractFolderFromDrag(e);
        if (!string.IsNullOrEmpty(folder) && _viewModel != null)
            _viewModel.SetPhotoFolder(folder);
        if (sender is Border b) b.BorderBrush = SolidColorBrush.Parse("#303038");
        e.Handled = true;
    }
}

public class PhotoFilterEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p
            ? string.Equals(s, p, StringComparison.OrdinalIgnoreCase)
            : false;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

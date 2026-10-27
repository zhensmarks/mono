using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class DocPanelView : UserControl
{
    public DocPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeActiveTabTracking();
        SubscribeActiveTabTracking();
    }

    private BatchViewModel? _trackedVm;

    private void SubscribeActiveTabTracking()
    {
        var vm = ResolveBatchVm();
        if (ReferenceEquals(_trackedVm, vm)) return;

        if (_trackedVm != null)
        {
            _trackedVm.PropertyChanged -= OnVmPropertyChanged;
            _trackedVm.DocTabs.CollectionChanged -= OnDocTabsChanged;
        }
        _trackedVm = vm;
        if (_trackedVm != null)
        {
            _trackedVm.PropertyChanged += OnVmPropertyChanged;
            _trackedVm.DocTabs.CollectionChanged += OnDocTabsChanged;
        }

        UpdateActiveChips();
    }

    private void OnDocTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Chip baru/terhapus: perbarui highlight setelah container dibuat.
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateActiveChips,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchViewModel.ActiveDocTab))
            UpdateActiveChips();
    }

    private void UpdateActiveChips()
    {
        var active = _trackedVm?.ActiveDocTab;
        var chips = this.GetVisualDescendants()
            .OfType<Avalonia.Controls.Border>()
            .Where(b => b.Classes.Contains("docChip"))
            .ToList();

        foreach (var chip in chips)
        {
            if (ReferenceEquals(chip.DataContext, active))
                chip.Classes.Add("docTabActive");
            else
                chip.Classes.Remove("docTabActive");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLogo1Drop(object? sender, Avalonia.Input.DragEventArgs e) => HandleLogoDrop(e, "1");
    private void OnLogo2Drop(object? sender, Avalonia.Input.DragEventArgs e) => HandleLogoDrop(e, "2");

    private void HandleLogoDrop(Avalonia.Input.DragEventArgs e, string slot)
    {
        // BatchVM should be available from DataContext since we inherit it or bind it
        BatchViewModel? batchVm = null;
        
        if (DataContext is DashboardViewModel dashboard)
            batchVm = dashboard.BatchVM;
        else if (DataContext is BatchViewModel vm)
            batchVm = vm;

        if (batchVm == null) return;

        var paths = new List<string>();

        if (e.Data.Contains(Avalonia.Input.DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    try
                    {
                        var path = file.Path?.LocalPath ?? file.Path?.ToString();
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                    catch { /* skip */ }
                }
            }
        }

        if (paths.Count == 0 && e.Data.Contains(Avalonia.Input.DataFormats.FileNames))
        {
            var names = e.Data.GetFileNames();
            if (names != null) paths.AddRange(names);
        }
        
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
            var ext = System.IO.Path.GetExtension(path);
            if (imageExtensions.Any(ext2 => ext.Equals(ext2, StringComparison.OrdinalIgnoreCase)))
            {
                _ = batchVm.ProcessLogoFile(path, slot);
                e.Handled = true; // Stop event from bubbling
                return;
            }
        }
    }

    private void OnDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        e.DragEffects = Avalonia.Input.DragDropEffects.Copy;
        e.Handled = true;
    }
    private void OnOpenDocSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var batchVm = ResolveBatchVm();
        if (batchVm == null) return;

        var dialog = new Dialogs.DocSettingsWindow(batchVm);

        if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window owner)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
    }

    private void OnDocChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Border chip) return;
        if (chip.DataContext is not BatchViewModel.DocTabItem tab) return;
        var vm = ResolveBatchVm();
        if (vm == null) return;

        var props = e.GetCurrentPoint(chip).Properties;

        if (props.IsMiddleButtonPressed)
        {
            // Klik tengah = hapus tab ini.
            vm.CloseDocTabCommand.Execute(tab);
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            // Klik kanan = menu (hapus / info).
            ShowDocChipMenu(chip, tab, vm);
            e.Handled = true;
            return;
        }

        vm.SetActiveDocTabCommand.Execute(tab);
        e.Handled = true;
    }

    private void OnDocAddChipPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint((Avalonia.Controls.Control?)sender).Properties;
        if (!props.IsLeftButtonPressed) return;

        ResolveBatchVm()?.AddDocTabCommand.Execute(null);
        e.Handled = true;
    }

    private void ShowDocChipMenu(Avalonia.Controls.Border chip, BatchViewModel.DocTabItem tab, BatchViewModel vm)
    {
        var menu = new ContextMenu();

        var delete = new MenuItem
        {
            Header = $"Hapus tab {tab.Number}",
            IsEnabled = vm.DocTabs.Count > 1
        };
        delete.Click += (_, _) => vm.CloseDocTabCommand.Execute(tab);

        var info = new MenuItem
        {
            Header = string.IsNullOrWhiteSpace(tab.Name) ? "(tab kosong)" : tab.Name,
            IsEnabled = false
        };

        menu.Items.Add(info);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Open(chip);
    }

    private BatchViewModel? ResolveBatchVm()
    {
        if (DataContext is DashboardViewModel dashboard)
            return dashboard.BatchVM;
        if (DataContext is BatchViewModel vm)
            return vm;
        return null;
    }
}

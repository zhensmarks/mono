using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using BMachine.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.Views;

public partial class ToolboxWindow : Window
{
    private readonly string _settingsPath;

    public ToolboxWindow()
    {
        InitializeComponent();
        var vm = new ToolboxViewModel();
        DataContext = vm;

        vm.PsdBucinVM.OnTypingModeExecuted += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(50);
                var txt = this.FindControl<AutoCompleteBox>("TypingModeTextBox");
                if (txt != null)
                {
                    var innerTextBox = txt.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                    (innerTextBox ?? (Control)txt).Focus();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Drag & Drop
        AddHandler(DragDrop.DropEvent, Drop);
        DragDrop.SetAllowDrop(this, true);

        // Window state memory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var bmachineDir = Path.Combine(appData, "BMachine");
        Directory.CreateDirectory(bmachineDir);
        _settingsPath = Path.Combine(bmachineDir, "ToolboxWindowState.json");
        LoadWindowState();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SaveWindowState();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && (e.Key == Key.E || e.Key == Key.N))
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new BMachine.UI.Messages.RequestOpenExplorerWindowMessage());
            e.Handled = true;
            return;
        }

        if ((e.Source is TextBox || e.Source is AutoCompleteBox) && e.Key != Key.Escape) return;

        if (DataContext is ToolboxViewModel vm && vm.IsPsdBucinVisible && vm.PsdBucinVM.IsManualModeActive)
        {
            string key = e.Key.ToString();
            // Handle numeric keys D1-D9 and NumPad1-NumPad9
            if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1]))
            {
                key = key.Substring(1);
            }
            else if (key.StartsWith("NumPad") && key.Length == 7 && char.IsDigit(key[6]))
            {
                key = key.Substring(6);
            }

            vm.PsdBucinVM.HandleShortcut(key);
        }
    }

    private class WindowStateConfig
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool? IsTypingMode { get; set; }
    }

    private void LoadWindowState()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var config = JsonSerializer.Deserialize<WindowStateConfig>(json);
                if (config != null)
                {
                    if (config.Width > 0) Width = config.Width;
                    if (config.Height > 0) Height = config.Height;
                    if (config.X.HasValue && config.Y.HasValue)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Position = new Avalonia.PixelPoint(config.X.Value, config.Y.Value);
                    }
                    if (config.IsTypingMode.HasValue && DataContext is ToolboxViewModel vm)
                    {
                        vm.PsdBucinVM.IsTypingMode = config.IsTypingMode.Value;
                    }
                }
            }
        }
        catch { }
    }

    private void SaveWindowState()
    {
        try
        {
            var config = new WindowStateConfig 
            { 
                Width = Bounds.Width, 
                Height = Bounds.Height,
                X = Position.X,
                Y = Position.Y,
                IsTypingMode = (DataContext as ToolboxViewModel)?.PsdBucinVM.IsTypingMode
            };
            var json = JsonSerializer.Serialize(config);
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToList();
        if (files == null || files.Count == 0 || DataContext is not ToolboxViewModel vm) return;

        var firstPath = files[0];
        bool isDir = Directory.Exists(firstPath);

        if (vm.IsPsdBucinVisible)
        {
            if (isDir)
            {
                bool isPhotoZone = (e.Source as Visual)?.GetVisualAncestors().Any(x => x.Name == "PhotoDropZone") == true || (e.Source as Control)?.Name == "PhotoDropZone";
                bool isMasterZone = (e.Source as Visual)?.GetVisualAncestors().Any(x => x.Name == "MasterDropZone") == true || (e.Source as Control)?.Name == "MasterDropZone";

                if (isPhotoZone)
                {
                    vm.PsdBucinVM.PhotoDirectory = firstPath;
                }
                else if (isMasterZone)
                {
                    vm.PsdBucinVM.MasterDirectory = firstPath;
                }
                else
                {
                    // Simple heuristic: if it contains PSDs, it's Master. Otherwise Photo.
                    bool hasPsd = Directory.EnumerateFiles(firstPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Any(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
                    
                    if (hasPsd) vm.PsdBucinVM.MasterDirectory = firstPath;
                    else vm.PsdBucinVM.PhotoDirectory = firstPath;
                }
            }
            else
            {
                _ = vm.PsdBucinVM.ProcessManualFilesAsync(files);
            }
        }
        else if (vm.IsMantraNamaVisible)
        {
            if (isDir)
            {
                vm.MantraNamaVM.SourceDirectory = firstPath;
            }
            else
            {
                vm.MantraNamaVM.SourceDirectory = Path.GetDirectoryName(firstPath) ?? "";
            }
        }
        else if (vm.IsMantraGandaVisible)
        {
            if (!isDir)
            {
                vm.MantraGandaVM.SourceFile = firstPath;
            }
            else
            {
                vm.MantraGandaVM.DataFolder = firstPath;
            }
        }
    }

    private async void OnPsdSelectMasterClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Master PSD",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            vm.PsdBucinVM.MasterDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnPsdSelectPhotoClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Foto JPG (Otomatis)",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            vm.PsdBucinVM.PhotoDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnPsdManualClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih Foto-foto JPG (Mode Manual)",
            AllowMultiple = true
        });

        if (files != null && files.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            _ = vm.PsdBucinVM.ProcessManualFilesAsync(paths);
        }
    }

    private async void OnNamaSelectFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Target",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            vm.MantraNamaVM.SourceDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnGandaSelectFileClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih File Master",
            AllowMultiple = false
        });

        if (files != null && files.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            vm.MantraGandaVM.SourceFile = files[0].Path.LocalPath;
        }
    }

    private async void OnGandaSelectDataFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Data",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0 && DataContext is ToolboxViewModel vm)
        {
            vm.MantraGandaVM.DataFolder = folders[0].Path.LocalPath;
        }
    }
}

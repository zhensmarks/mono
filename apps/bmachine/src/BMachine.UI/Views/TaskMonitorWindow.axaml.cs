using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BMachine.UI.Views;

public partial class TaskMonitorWindow : Window
{
    /// <summary>Dipanggil sekali setelah window dibuat. Mengembalikan posisi tersimpan (x,y) atau null.</summary>
    public Func<System.Threading.Tasks.Task<PixelPoint?>>? LoadPositionAsync { get; set; }

    /// <summary>Dipanggil saat window dipindah. Menyimpan posisi terakhir.</summary>
    public Func<PixelPoint, System.Threading.Tasks.Task>? SavePositionAsync { get; set; }

    public TaskMonitorWindow()
    {
        InitializeComponent();
        PositionChanged += OnPositionChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (SavePositionAsync == null || WindowState != WindowState.Normal) return;
        _ = SavePositionAsync(Position);
    }
}

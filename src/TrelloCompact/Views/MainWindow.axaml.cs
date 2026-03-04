using Avalonia;
using Avalonia.Controls;
using TrelloCompact.Services;
using TrelloCompact.ViewModels;

namespace TrelloCompact.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        // Restore saved window size/position
        var cfg = _settings.Load();
        Width = cfg.WindowWidth > 0 ? cfg.WindowWidth : 800;
        Height = cfg.WindowHeight > 0 ? cfg.WindowHeight : 600;

        if (cfg.WindowX >= 0 && cfg.WindowY >= 0)
        {
            Position = new PixelPoint(cfg.WindowX, cfg.WindowY);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (cfg.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Save window size/position
        var cfg = _settings.Load();
        cfg.IsMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            cfg.WindowWidth = Width;
            cfg.WindowHeight = Height;
            cfg.WindowX = Position.X;
            cfg.WindowY = Position.Y;
        }

        _settings.Save(cfg);
        base.OnClosing(e);
    }
}

using System;
using Avalonia.Controls;

namespace BMachine.UI.Views;

internal static class CompactDialogSizing
{
    public static void FitToScreen(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window);
        if (screen is null) return;

        var scale = screen.Scaling;
        var availableWidth = Math.Max(1, screen.WorkingArea.Width / scale - 24);
        var availableHeight = Math.Max(1, screen.WorkingArea.Height / scale - 24);
        window.MinWidth = Math.Min(window.MinWidth, availableWidth);
        window.MinHeight = Math.Min(window.MinHeight, availableHeight);
        window.MaxWidth = availableWidth;
        window.MaxHeight = availableHeight;
        window.Width = Math.Min(window.Width, availableWidth);
        window.Height = Math.Min(window.Height, availableHeight);

        var area = screen.WorkingArea;
        var x = Math.Clamp(window.Position.X, area.X,
            Math.Max(area.X, area.Right - (int)Math.Ceiling(window.Width * scale)));
        var y = Math.Clamp(window.Position.Y, area.Y,
            Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(window.Height * scale)));
        window.Position = new Avalonia.PixelPoint(x, y);
    }
}

using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using Avalonia;

namespace PixelcutCompact.Converters;

public class BooleanToStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isProcessing && isProcessing)
        {
            // Orange for processing
            return SolidColorBrush.Parse("#F59E0B"); 
        }
        // Green for ready/idle
        return SolidColorBrush.Parse("#10B981");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToGeometryConverter : IValueConverter
{
    public StreamGeometry? TrueGeometry { get; set; }
    public StreamGeometry? FalseGeometry { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return TrueGeometry;
        }
        return FalseGeometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LuminanceToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            var color = brush.Color;
            // Calculate relative luminance
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            return luminance > 0.5 ? Brushes.Black : Brushes.White;
        }
        else if (value is Color color)
        {
             var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
             return luminance > 0.5 ? Brushes.Black : Brushes.White;
        }
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return new SolidColorBrush(color);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    return new Avalonia.Media.Imaging.Bitmap(path);
                }
            }
            catch { }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

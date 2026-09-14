using Avalonia.Controls;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace BMachine.UI.Converters;

public class DoubleToGridLengthConverter : IValueConverter
{
    public static readonly DoubleToGridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is double d && d > 0)
        {
            return new GridLength(d, GridUnitType.Pixel);
        }
        return new GridLength(400, GridUnitType.Pixel);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is GridLength gl && gl.IsAbsolute)
        {
            return gl.Value;
        }
        return 400.0;
    }
}

using Avalonia.Data.Converters;
using Avalonia.Data;
using Avalonia;
using System;
using System.Globalization;

namespace BMachine.UI.Converters;

public class IndexToBoolConverter : IValueConverter
{
    public static readonly IndexToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intVal && parameter is string paramStr && int.TryParse(paramStr, out int targetVal))
        {
            return intVal == targetVal;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolVal && boolVal && parameter is string paramStr && int.TryParse(paramStr, out int targetVal))
        {
            return targetVal;
        }
        return AvaloniaProperty.UnsetValue;
    }
}



public class BoolInverter : IValueConverter
{
    public static readonly BoolInverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

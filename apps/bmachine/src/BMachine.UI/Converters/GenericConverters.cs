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

public class IconConverter : IValueConverter
{
    public static readonly IconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current != null)
        {
            if (Application.Current.TryGetResource(key, null, out var resource))
            {
                return resource;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex)); }
            catch { /* fall through to default */ }
        }
        return Avalonia.Media.Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>Hex string <-> Avalonia Color (TwoWay) untuk ColorPicker.</summary>
public class HexToColorConverter : IValueConverter
{
    public static readonly HexToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return Avalonia.Media.Color.Parse(hex); }
            catch { /* fall through */ }
        }
        return Avalonia.Media.Color.Parse("#4A9EFF");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Avalonia.Media.Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>True -> Bold (root folder), otherwise Normal (subfolder).</summary>
public class RootFontWeightConverter : IValueConverter
{
    public static readonly RootFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && parameter is string p)
        {
            return s.Equals(p, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public class EnumMatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        
        string checkValue = value.ToString() ?? "";
        string targetValue = parameter.ToString() ?? "";
        
        return checkValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter != null)
        {
            try
            {
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, parameter.ToString()!);
                }
            }
            catch {}
        }
        return AvaloniaProperty.UnsetValue;
    }
}

public class StringToUpperConverter : IValueConverter
{
    public static readonly StringToUpperConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value = bool (isActive), parameter = string (tab label)
        var label = parameter as string ?? "";
        if (value is bool isActive && isActive)
        {
            return label.ToUpperInvariant();
        }
        return label;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public class ContrastColorConverter : IValueConverter
{
    public static readonly ContrastColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Avalonia.Media.ISolidColorBrush sb)
        {
            var color = sb.Color;
            double luma = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luma > 0.5 ? Avalonia.Media.Brushes.Black : Avalonia.Media.Brushes.White;
        }
        else if (value is Avalonia.Media.Color color)
        {
            double luma = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luma > 0.5 ? Avalonia.Media.Brushes.Black : Avalonia.Media.Brushes.White;
        }
        return Avalonia.Media.Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

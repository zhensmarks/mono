using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace BMachine.UI.Converters;

public class BoolToColorConverter : IMultiValueConverter
{
    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not bool isDone) return new SolidColorBrush(Color.Parse("#46505C"));

        var part = parameter?.ToString() ?? "border";

        if (isDone)
        {
            return part switch
            {
                "bg" => new SolidColorBrush(Color.Parse("#173F2D")),
                "border" => new SolidColorBrush(Color.Parse("#3FAF78")),
                "dot" => new SolidColorBrush(Color.Parse("#45C98A")),
                "text" => new SolidColorBrush(Color.Parse("#9DE8C1")),
                _ => new SolidColorBrush(Color.Parse("#3FAF78"))
            };
        }

        return part switch
        {
            "bg" => Brushes.Transparent,
            "border" => new SolidColorBrush(Color.Parse("#46505C")),
            "dot" => new SolidColorBrush(Color.Parse("#7B8794")),
            "text" => new SolidColorBrush(Color.Parse("#AAB4BF")),
            _ => new SolidColorBrush(Color.Parse("#46505C"))
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

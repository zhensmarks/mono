using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace FrameStudio.App.Services;

public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly IBrush Selected = Brush.Parse("#303923");
    private static readonly IBrush Normal = Brush.Parse("#1D222A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Selected : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

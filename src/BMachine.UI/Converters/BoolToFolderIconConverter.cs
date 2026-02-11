using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia;

namespace BMachine.UI.Converters;

public class BoolToFolderIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory && isDirectory)
        {
            // Folder Icon Geometry
            return Application.Current?.FindResource("IconFolder") as StreamGeometry;
        }
        
        // File Icon Geometry
        return Application.Current?.FindResource("IconFile") as StreamGeometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

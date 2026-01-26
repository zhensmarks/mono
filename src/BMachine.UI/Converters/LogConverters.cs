using Avalonia.Data.Converters;
using System.Globalization;
using BMachine.UI.Models;

namespace BMachine.UI.ViewModels;

public static class LogConverters
{
    public static readonly IValueConverter IsInfo = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.Info);
    public static readonly IValueConverter IsSystem = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.System);
    public static readonly IValueConverter IsSuccess = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.Success);
    public static readonly IValueConverter IsWarning = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.Warning);
    public static readonly IValueConverter IsError = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.Error);
    public static readonly IValueConverter IsDebug = new FuncValueConverter<LogLevel, bool>(l => l == LogLevel.Debug);
}

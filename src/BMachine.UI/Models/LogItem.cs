using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace BMachine.UI.Models;

public enum LogLevel
{
    Standard, // Default (White)
    Info,     // Blue (Explicit [INFO])
    Success,
    Warning,
    Error,
    Debug,
    System
}

public partial class LogItem : ObservableObject
{
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private LogLevel _level;
    
    // Helper for UI Binding
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color))]
    private IBrush? _customColor;

    public IBrush Color
    {
        get
        {
             if (CustomColor != null) return CustomColor;
             
             return Level switch
             {
                 LogLevel.Success => SolidColorBrush.Parse("#4ade80"), // Green
                 LogLevel.Error => SolidColorBrush.Parse("#f87171"),   // Red
                 LogLevel.Warning => SolidColorBrush.Parse("#facc15"), // Yellow
                 LogLevel.System => SolidColorBrush.Parse("#22d3ee"),  // Cyan
                 LogLevel.Info => SolidColorBrush.Parse("#60a5fa"),    // Blue
                 LogLevel.Debug => SolidColorBrush.Parse("#9ca3af"),   // Gray
                 LogLevel.Standard => SolidColorBrush.Parse("#e5e7eb"), // White
                 _ => SolidColorBrush.Parse("#e5e7eb")
             };
        }
        set
        {
            CustomColor = value;
        }
    }

    public LogItem(string message, LogLevel level = LogLevel.Standard)
    {
        Message = message;
        Level = level;
        Timestamp = DateTime.Now;
    }
}

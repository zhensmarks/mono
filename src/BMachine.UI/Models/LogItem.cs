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

    public IBrush? Color
    {
        get
        {
             if (CustomColor != null) return CustomColor;
             return null; // Let View handle Theme Resources via Style/Triggers
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

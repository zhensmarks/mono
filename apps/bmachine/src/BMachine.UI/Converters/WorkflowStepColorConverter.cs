using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Converters;

public class WorkflowStepColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MantraDataWorkflowStep currentStep || parameter is not string stepParam)
            return new SolidColorBrush(Color.Parse("#1C1C1F")); // Default dark

        var targetStep = stepParam switch
        {
            "Import" => MantraDataWorkflowStep.Import,
            "Clean" => MantraDataWorkflowStep.Clean,
            "Transform" => MantraDataWorkflowStep.Transform,
            "Export" => MantraDataWorkflowStep.Export,
            "Done" => MantraDataWorkflowStep.Done,
            _ => MantraDataWorkflowStep.None
        };

        // Active step = blue, Completed = green, Pending = gray
        if (currentStep == targetStep)
            return new SolidColorBrush(Color.Parse("#3B82F6")); // Blue active
        
        if (IsCompleted(currentStep, targetStep))
            return new SolidColorBrush(Color.Parse("#22C55E")); // Green completed
        
        return new SolidColorBrush(Color.Parse("#52525B")); // Gray pending
    }

    private bool IsCompleted(MantraDataWorkflowStep current, MantraDataWorkflowStep target)
    {
        return (int)current > (int)target;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

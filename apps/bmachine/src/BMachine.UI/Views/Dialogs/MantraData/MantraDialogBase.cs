using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia;

namespace BMachine.UI.Views.Dialogs.MantraData;

/// <summary>
/// Base window for all MantraData dialogs: frameless custom chrome
/// (NoChrome + custom drag/title bar + close button), matching ToolboxWindow.
/// </summary>
public abstract class MantraDialogBase : Window
{
    protected MantraDialogBase()
    {
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsPointerInsideButton(e.Source)) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(this);
            if (pos.Y <= 45)
            {
                BeginMoveDrag(e);
            }
        }
    }

    protected void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsPointerInsideButton(e.Source)) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private static bool IsPointerInsideButton(object? source)
    {
        for (Visual? visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button) return true;
        }

        return false;
    }

    protected void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

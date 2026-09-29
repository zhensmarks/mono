using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace BMachine.UI.Controls;

public class AutoCompleteBoxHelper : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnableAutoPopupProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBoxHelper, AutoCompleteBox, bool>("EnableAutoPopup");

    public static void SetEnableAutoPopup(AutoCompleteBox element, bool value) => element.SetValue(EnableAutoPopupProperty, value);
    public static bool GetEnableAutoPopup(AutoCompleteBox element) => element.GetValue(EnableAutoPopupProperty);

    private static readonly AttachedProperty<long> LastClosedTicksProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBoxHelper, AutoCompleteBox, long>("LastClosedTicks");

    private static readonly AttachedProperty<bool> OpenPendingProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBoxHelper, AutoCompleteBox, bool>("OpenPending");

    static AutoCompleteBoxHelper()
    {
        EnableAutoPopupProperty.Changed.AddClassHandler<AutoCompleteBox>((box, e) =>
        {
            if (e.NewValue is bool b && b) Hook(box);
        });
    }

    public static void Hook(AutoCompleteBox? box)
    {
        if (box == null) return;

        box.DropDownClosed -= OnDropDownClosed;
        box.DropDownClosed += OnDropDownClosed;
        box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        box.RemoveHandler(InputElement.GotFocusEvent, OnGotFocusHandler);
        box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        box.AddHandler(InputElement.GotFocusEvent, OnGotFocusHandler, RoutingStrategies.Bubble);
    }

    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is AutoCompleteBox box)
        {
            box.SetValue(LastClosedTicksProperty, Environment.TickCount64);
            box.SetValue(OpenPendingProperty, false);
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is AutoCompleteBox box && box.IsEnabled) OpenDropDownSafe(box);
    }

    private static void OnGotFocusHandler(object? sender, GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox box && box.IsEnabled) OpenDropDownSafe(box);
    }

    public static void OnGotFocus(AutoCompleteBox? box, GotFocusEventArgs? e = null)
    {
        if (box != null && box.IsEnabled) OpenDropDownSafe(box);
    }

    public static void OpenDropDownSafe(AutoCompleteBox box)
    {
        if (!box.IsEnabled || box.IsDropDownOpen || box.GetValue(OpenPendingProperty)) return;
        if (Environment.TickCount64 - box.GetValue(LastClosedTicksProperty) < 350) return;

        box.SetValue(OpenPendingProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            box.SetValue(OpenPendingProperty, false);
            if (box.GetVisualRoot() == null || !box.IsEnabled || box.IsDropDownOpen) return;
            if (Environment.TickCount64 - box.GetValue(LastClosedTicksProperty) < 350) return;

            try
            {
                box.PopulateComplete();
                box.IsDropDownOpen = true;
            }
            catch (InvalidOperationException)
            {
                // Visual tree can detach while popup closes.
            }
            catch (ArgumentOutOfRangeException)
            {
                // Avalonia 11 bug: collection mutated during OnDetachedFromVisualTreeCore
                // when closing popup during item selection. Suppress to prevent app crash.
            }
        }, DispatcherPriority.Background);
    }
}

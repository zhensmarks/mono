using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Runtime.InteropServices;
using Avalonia.Controls.Primitives;

namespace BMachine.UI.Views;

public partial class GdriveWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPosition;

    public GdriveWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        
        // Right-click to close
        PointerReleased += (s, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                Close();
            }
        };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // No manual move needed
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // No manual capture needed
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
    }
}

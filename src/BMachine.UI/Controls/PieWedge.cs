using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BMachine.UI.Controls;

/// <summary>
/// Custom control that draws a pie wedge (arc segment) for radial menu highlight.
/// </summary>
public class PieWedge : Control
{
    public static readonly StyledProperty<double> StartAngleProperty =
        AvaloniaProperty.Register<PieWedge, double>(nameof(StartAngle), 0);

    public static readonly StyledProperty<double> SweepAngleProperty =
        AvaloniaProperty.Register<PieWedge, double>(nameof(SweepAngle), 45);

    public static readonly StyledProperty<double> InnerRadiusProperty =
        AvaloniaProperty.Register<PieWedge, double>(nameof(InnerRadius), 15);

    public static readonly StyledProperty<double> OuterRadiusProperty =
        AvaloniaProperty.Register<PieWedge, double>(nameof(OuterRadius), 70);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PieWedge, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<bool> ShowHoverIndicatorsProperty =
        AvaloniaProperty.Register<PieWedge, bool>(nameof(ShowHoverIndicators), false);

    public static readonly StyledProperty<IBrush?> HoverStrokeProperty =
        AvaloniaProperty.Register<PieWedge, IBrush?>(nameof(HoverStroke));

    public double StartAngle
    {
        get => GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    public double InnerRadius
    {
        get => GetValue(InnerRadiusProperty);
        set => SetValue(InnerRadiusProperty, value);
    }

    public double OuterRadius
    {
        get => GetValue(OuterRadiusProperty);
        set => SetValue(OuterRadiusProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool ShowHoverIndicators
    {
        get => GetValue(ShowHoverIndicatorsProperty);
        set => SetValue(ShowHoverIndicatorsProperty, value);
    }

    public IBrush? HoverStroke
    {
        get => GetValue(HoverStrokeProperty);
        set => SetValue(HoverStrokeProperty, value);
    }

    static PieWedge()
    {
        AffectsRender<PieWedge>(
            StartAngleProperty, SweepAngleProperty,
            InnerRadiusProperty, OuterRadiusProperty,
            FillProperty, ShowHoverIndicatorsProperty, HoverStrokeProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var fill = Fill;
        if (fill == null || SweepAngle == 0) return;

        var cx = Bounds.Width / 2;
        var cy = Bounds.Height / 2;

        // Convert angles: our convention is 0=Top, clockwise
        // Avalonia uses standard math: 0=Right, counter-clockwise for arcs
        // We need to convert to radians for point calculations
        double startRad = (StartAngle - 90) * Math.PI / 180.0;
        double endRad = (StartAngle + SweepAngle - 90) * Math.PI / 180.0;

        // Calculate corner points
        var outerStart = new Point(cx + OuterRadius * Math.Cos(startRad), cy + OuterRadius * Math.Sin(startRad));
        var outerEnd = new Point(cx + OuterRadius * Math.Cos(endRad), cy + OuterRadius * Math.Sin(endRad));
        var innerStart = new Point(cx + InnerRadius * Math.Cos(startRad), cy + InnerRadius * Math.Sin(startRad));
        var innerEnd = new Point(cx + InnerRadius * Math.Cos(endRad), cy + InnerRadius * Math.Sin(endRad));

        bool isLargeArc = Math.Abs(SweepAngle) > 180;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            // Start at inner start
            ctx.BeginFigure(innerStart, true);
            
            // Line to outer start
            ctx.LineTo(outerStart);
            
            // Arc along outer radius to outer end
            ctx.ArcTo(outerEnd, new Size(OuterRadius, OuterRadius), 0, isLargeArc, SweepDirection.Clockwise);
            
            // Line to inner end
            ctx.LineTo(innerEnd);
            
            // Arc along inner radius back to inner start (counter-clockwise)
            ctx.ArcTo(innerStart, new Size(InnerRadius, InnerRadius), 0, isLargeArc, SweepDirection.CounterClockwise);
            
            // Close
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geom);
        
            // Draw hover indicators if enabled
        if (ShowHoverIndicators && HoverStroke != null)
        {
            // Outer bright highlight arc (inset by 1.5px to prevent window clipping)
            double strokeOuter = OuterRadius - 1.5;
            Point arcStart = new Point(cx + strokeOuter * Math.Cos(startRad), cy + strokeOuter * Math.Sin(startRad));
            Point arcEnd = new Point(cx + strokeOuter * Math.Cos(endRad), cy + strokeOuter * Math.Sin(endRad));
            
            var arcGeom = new StreamGeometry();
            using (var ctx = arcGeom.Open())
            {
                ctx.BeginFigure(arcStart, false);
                ctx.ArcTo(arcEnd, new Size(strokeOuter, strokeOuter), 0, isLargeArc, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(HoverStroke, thickness: 5, lineCap: PenLineCap.Flat), arcGeom);

            // Inner triangle pointer ( chevron pointing OUTWARD )
            double midRad = (startRad + endRad) / 2.0;

            // The tip points outwards (away from center)
            double tipRad = InnerRadius + 10.0; 
            Point tip = new Point(cx + tipRad * Math.Cos(midRad), cy + tipRad * Math.Sin(midRad));
            
            // The base sits on the inner radius
            double baseRad = InnerRadius;
            double spread = 0.08; // radians offset for the wider base width
            Point base1 = new Point(cx + baseRad * Math.Cos(midRad - spread), cy + baseRad * Math.Sin(midRad - spread));
            Point base2 = new Point(cx + baseRad * Math.Cos(midRad + spread), cy + baseRad * Math.Sin(midRad + spread));

            var triGeom = new StreamGeometry();
            using (var ctx = triGeom.Open())
            {
                ctx.BeginFigure(tip, true);
                ctx.LineTo(base1);
                ctx.LineTo(base2);
                ctx.EndFigure(true);
            }
            context.DrawGeometry(HoverStroke, null, triGeom);
        }
    }
}

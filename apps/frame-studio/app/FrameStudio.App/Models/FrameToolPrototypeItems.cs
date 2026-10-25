namespace FrameStudio.App.Models;

public enum FrameMaskShape
{
    Rectangle,
    Polygon
}

public readonly record struct NormalizedFramePoint(double X, double Y);

/// <summary>
/// Frame geometry stored in artboard-normalized coordinates, independent of canvas zoom or pixel size.
/// </summary>
public sealed class FrameMask
{
    private FrameMask(FrameMaskShape shape, IEnumerable<NormalizedFramePoint> points)
    {
        Shape = shape;
        Points = Array.AsReadOnly(points.ToArray());
    }

    public FrameMaskShape Shape { get; }
    public IReadOnlyList<NormalizedFramePoint> Points { get; }
    public string ShapeLabel => Shape == FrameMaskShape.Rectangle ? "Persegi panjang" : "Poligon";

    public bool IsValid => Points.Count >= 3
        && Points.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)
            && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1)
        && Math.Abs(SignedDoubleArea(Points)) > 0.0001;

    public static FrameMask Rectangle(NormalizedFramePoint first, NormalizedFramePoint second)
    {
        var left = Math.Clamp(Math.Min(first.X, second.X), 0, 1);
        var right = Math.Clamp(Math.Max(first.X, second.X), 0, 1);
        var top = Math.Clamp(Math.Min(first.Y, second.Y), 0, 1);
        var bottom = Math.Clamp(Math.Max(first.Y, second.Y), 0, 1);
        if (right - left < 0.01 || bottom - top < 0.01)
            throw new ArgumentException("Frame persegi panjang terlalu kecil.");

        return new FrameMask(FrameMaskShape.Rectangle,
        [
            new(left, top),
            new(right, top),
            new(right, bottom),
            new(left, bottom)
        ]);
    }

    public static FrameMask Polygon(IEnumerable<NormalizedFramePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var normalizedPoints = points.ToArray();
        var mask = new FrameMask(FrameMaskShape.Polygon, normalizedPoints);
        if (!mask.IsValid)
            throw new ArgumentException("Poligon perlu memiliki sedikitnya tiga titik yang membentuk area tertutup.", nameof(points));
        return mask;
    }

    public static FrameMask ExamplePolygon() => Polygon(
    [
        new(0.07, 0.19),
        new(0.58, 0.15),
        new(0.66, 0.25),
        new(0.64, 0.68),
        new(0.54, 0.79),
        new(0.08, 0.77)
    ]);

    private static double SignedDoubleArea(IReadOnlyList<NormalizedFramePoint> points)
    {
        var area = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            area += current.X * next.Y - next.X * current.Y;
        }
        return area;
    }
}

public enum FrameInsertionPoint
{
    BetweenBackgroundAndForeground,
    AboveForeground
}

public sealed record SavedFramePreset(string Name, FrameMask Mask, FrameInsertionPoint InsertionPoint)
{
    public string Description => $"{Mask.ShapeLabel} · {(InsertionPoint == FrameInsertionPoint.BetweenBackgroundAndForeground ? "di antara layer" : "di atas ornamen")}";
}

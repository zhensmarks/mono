using System;
using Avalonia;
using Avalonia.Controls;
using BMachine.UI.Models;

namespace BMachine.UI.Controls;

/// <summary>
/// Items panel for the Master browser tree.
///
/// Children that are PSD/PSB files *with* a JPG preview are laid out two per
/// row (half width each) so they form a 2-column thumbnail grid. Folders and
/// files without a preview span the full row like a normal list entry.
///
/// This is used as the <c>ItemsPanel</c> of <c>TreeViewItem</c>, which keeps
/// the folder chevron/expand behaviour of the TreeView intact.
/// </summary>
public class ThumbnailWrapPanel : Panel
{
    /// <summary>Horizontal gap between the two thumbnail columns.</summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ThumbnailWrapPanel, double>(nameof(ColumnSpacing), 8d);

    /// <summary>Vertical gap between rows.</summary>
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ThumbnailWrapPanel, double>(nameof(RowSpacing), 2d);

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>
    /// True for a leaf node that has a paired JPG preview, i.e. the ones that
    /// should render as a thumbnail card instead of a full-width row.
    /// </summary>
    private static bool IsThumbnail(Control child) =>
        child.DataContext is MasterNode node && node.HasPreview;

    protected override Size MeasureOverride(Size availableSize)
    {
        double fullWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? 200d
            : availableSize.Width;
        double halfWidth = Math.Max(0d, (fullWidth - ColumnSpacing) / 2d);

        double y = 0d;
        double rowHeight = 0d;
        int halfCount = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            if (IsThumbnail(child))
            {
                child.Measure(new Size(halfWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                halfCount++;

                if (halfCount == 2)
                {
                    y += rowHeight + RowSpacing;
                    rowHeight = 0d;
                    halfCount = 0;
                }
            }
            else
            {
                if (halfCount == 1)
                {
                    y += rowHeight + RowSpacing;
                    rowHeight = 0d;
                    halfCount = 0;
                }

                child.Measure(new Size(fullWidth, double.PositiveInfinity));
                y += child.DesiredSize.Height + RowSpacing;
            }
        }

        // Trailing single thumbnail waiting for its partner.
        if (halfCount == 1)
            y += rowHeight;

        return new Size(fullWidth, Math.Max(0d, y));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double fullWidth = finalSize.Width;
        double halfWidth = Math.Max(0d, (fullWidth - ColumnSpacing) / 2d);

        double y = 0d;
        double rowHeight = 0d;
        int halfCount = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            if (IsThumbnail(child))
            {
                double x = halfCount == 0 ? 0d : halfWidth + ColumnSpacing;
                double h = child.DesiredSize.Height;

                child.Arrange(new Rect(x, y, halfWidth, h));
                rowHeight = Math.Max(rowHeight, h);
                halfCount++;

                if (halfCount == 2)
                {
                    y += rowHeight + RowSpacing;
                    rowHeight = 0d;
                    halfCount = 0;
                }
            }
            else
            {
                if (halfCount == 1)
                {
                    y += rowHeight + RowSpacing;
                    rowHeight = 0d;
                    halfCount = 0;
                }

                double h = child.DesiredSize.Height;
                child.Arrange(new Rect(0d, y, fullWidth, h));
                y += h + RowSpacing;
            }
        }

        return finalSize;
    }
}

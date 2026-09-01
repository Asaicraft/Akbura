using System;
using Avalonia;
using Avalonia.Controls;

namespace Akbura;

public sealed class Row : Panel
{
    static Row()
    {
        AffectsMeasure<Row>(
            ColumnsProperty,
            ColumnSpacingProperty);

        // Grid.ColumnSpan is defined on a child,
        // but changing it must invalidate the parent Row layout.
        AffectsParentMeasure<Row>(Grid.ColumnSpanProperty);
    }

    /// <summary>
    /// Defines the <see cref="Columns"/> property.
    /// </summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<Row, int>(
            nameof(Columns),
            12);

    /// <summary>
    /// Defines the <see cref="ColumnSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<Row, double>(
            nameof(ColumnSpacing),
            0);

    /// <summary>
    /// Gets or sets the number of logical columns in the row.
    /// </summary>
    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between logical columns.
    /// </summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
            return default;

        var columns = Math.Max(1, Columns);
        var spacing = Math.Max(0, ColumnSpacing);

        var width = availableSize.Width;

        // Row normally receives a finite width from its parent.
        // Handle infinite width as well for Auto-sized containers.
        if (double.IsPositiveInfinity(width))
            width = MeasureDesiredWidth(columns, spacing);

        var height = MeasureChildren(width, columns, spacing);

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;

        var columns = Math.Max(1, Columns);
        var spacing = Math.Max(0, ColumnSpacing);

        var columnWidth = GetColumnWidth(
            finalSize.Width,
            columns,
            spacing);

        var y = 0d;
        var index = 0;

        while (index < Children.Count)
        {
            var rowStart = index;
            var usedColumns = 0;
            var rowHeight = 0d;

            // Determine which children belong to the current row
            // and calculate the maximum height of that row.
            while (index < Children.Count)
            {
                var child = Children[index];
                var span = GetColumnSpan(child, columns);

                if (usedColumns > 0 &&
                    usedColumns + span > columns)
                {
                    break;
                }

                usedColumns += span;

                rowHeight = Math.Max(
                    rowHeight,
                    child.DesiredSize.Height);

                index++;

                if (usedColumns == columns)
                    break;
            }

            // Arrange all children in the current row.
            var column = 0;

            for (var i = rowStart; i < index; i++)
            {
                var child = Children[i];
                var span = GetColumnSpan(child, columns);

                var x = column * (columnWidth + spacing);

                var width =
                    columnWidth * span +
                    spacing * (span - 1);

                child.Arrange(
                    new Rect(
                        x,
                        y,
                        width,
                        rowHeight));

                column += span;
            }

            y += rowHeight;
        }

        return finalSize;
    }

    private double MeasureChildren(double width, int columns, double spacing)
    {
        var columnWidth = GetColumnWidth(
            width,
            columns,
            spacing);

        var usedColumns = 0;
        var rowHeight = 0d;
        var totalHeight = 0d;

        foreach (var child in Children)
        {
            var span = GetColumnSpan(child, columns);

            // Start a new row if the next child does not fit
            // into the remaining logical columns.
            if (usedColumns > 0 &&
                usedColumns + span > columns)
            {
                totalHeight += rowHeight;

                usedColumns = 0;
                rowHeight = 0;
            }

            var childWidth =
                columnWidth * span +
                spacing * (span - 1);

            child.Measure(
                new Size(
                    childWidth,
                    double.PositiveInfinity));

            rowHeight = Math.Max(
                rowHeight,
                child.DesiredSize.Height);

            usedColumns += span;

            // The row is exactly filled.
            if (usedColumns == columns)
            {
                totalHeight += rowHeight;

                usedColumns = 0;
                rowHeight = 0;
            }
        }

        // Add the last partially filled row.
        if (usedColumns > 0)
            totalHeight += rowHeight;

        return totalHeight;
    }

    /// <summary>
    /// Calculates the desired width when the parent
    /// measures this panel with an infinite width.
    /// </summary>
    private double MeasureDesiredWidth(int columns, double spacing)
    {
        var columnWidth = 0d;

        foreach (var child in Children)
        {
            var span = GetColumnSpan(child, columns);

            child.Measure(
                new Size(
                    double.PositiveInfinity,
                    double.PositiveInfinity));

            // Calculate the minimum logical column width
            // required to fit this child.
            var requiredColumnWidth =
                (child.DesiredSize.Width -
                 spacing * (span - 1)) / span;

            columnWidth = Math.Max(
                columnWidth,
                Math.Max(0, requiredColumnWidth));
        }

        return columnWidth * columns +
               spacing * (columns - 1);
    }

    private static int GetColumnSpan(
        Control child,
        int columns)
    {
        return Math.Clamp(Grid.GetColumnSpan(child), 1, columns);
    }

    private static double GetColumnWidth(
        double totalWidth,
        int columns,
        double spacing)
    {
        var totalSpacing = spacing * (columns - 1);

        return Math.Max(0, (totalWidth - totalSpacing) / columns);
    }
}

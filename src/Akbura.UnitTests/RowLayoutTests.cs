using Avalonia;
using Avalonia.Controls;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RowLayoutTests
{
    [Fact]
    public void RowSpacing_DoesNotAffectSingleRow()
    {
        var row = new Row
        {
            Columns = 12,
            RowSpacing = 20d
        };

        var child = CreateChild(
            columnSpan: 12,
            height: 50d);

        row.Children.Add(child);

        row.Measure(
            new Size(
                1200d,
                double.PositiveInfinity));

        Assert.Equal(
            50d,
            row.DesiredSize.Height);
    }

    [Fact]
    public void RowSpacing_IsAddedOnceBetweenTwoRows()
    {
        var row = new Row
        {
            Columns = 12,
            RowSpacing = 20d
        };

        row.Children.Add(
            CreateChild(
                columnSpan: 12,
                height: 50d));
        row.Children.Add(
            CreateChild(
                columnSpan: 12,
                height: 30d));

        row.Measure(
            new Size(
                1200d,
                double.PositiveInfinity));

        Assert.Equal(
            100d,
            row.DesiredSize.Height);
    }

    [Fact]
    public void RowSpacing_ChangeInvalidatesMeasurement()
    {
        var row = new Row
        {
            Columns = 12
        };

        row.Children.Add(
            CreateChild(
                columnSpan: 12,
                height: 50d));
        row.Children.Add(
            CreateChild(
                columnSpan: 12,
                height: 30d));

        var availableSize = new Size(
            1200d,
            double.PositiveInfinity);

        row.Measure(availableSize);

        Assert.Equal(
            80d,
            row.DesiredSize.Height);

        row.RowSpacing = 20d;
        row.Measure(availableSize);

        Assert.Equal(
            100d,
            row.DesiredSize.Height);
    }

    [Fact]
    public void RowSpacing_IsAppliedBetweenMixedSpanRows()
    {
        var row = new Row
        {
            Columns = 12,
            ColumnSpacing = 8d,
            RowSpacing = 10d
        };

        var first = CreateChild(
            columnSpan: 8,
            height: 40d);
        var second = CreateChild(
            columnSpan: 4,
            height: 60d);
        var third = CreateChild(
            columnSpan: 4,
            height: 20d);
        var fourth = CreateChild(
            columnSpan: 4,
            height: 30d);
        var fifth = CreateChild(
            columnSpan: 4,
            height: 25d);
        var sixth = CreateChild(
            columnSpan: 12,
            height: 50d);

        row.Children.Add(first);
        row.Children.Add(second);
        row.Children.Add(third);
        row.Children.Add(fourth);
        row.Children.Add(fifth);
        row.Children.Add(sixth);

        row.Measure(
            new Size(
                1200d,
                double.PositiveInfinity));
        row.Arrange(
            new Rect(
                0d,
                0d,
                1200d,
                row.DesiredSize.Height));

        Assert.Equal(
            160d,
            row.DesiredSize.Height);
        Assert.Equal(0d, first.Bounds.Y);
        Assert.Equal(0d, second.Bounds.Y);
        Assert.Equal(70d, third.Bounds.Y);
        Assert.Equal(70d, fourth.Bounds.Y);
        Assert.Equal(70d, fifth.Bounds.Y);
        Assert.Equal(110d, sixth.Bounds.Y);
    }

    [Fact]
    public void NegativeRowSpacing_IsTreatedAsZero()
    {
        var row = new Row
        {
            Columns = 12,
            RowSpacing = -20d
        };

        var first = CreateChild(
            columnSpan: 12,
            height: 50d);
        var second = CreateChild(
            columnSpan: 12,
            height: 30d);

        row.Children.Add(first);
        row.Children.Add(second);

        row.Measure(
            new Size(
                1200d,
                double.PositiveInfinity));
        row.Arrange(
            new Rect(
                0d,
                0d,
                1200d,
                row.DesiredSize.Height));

        Assert.Equal(
            80d,
            row.DesiredSize.Height);
        Assert.Equal(50d, second.Bounds.Y);
    }

    private static Border CreateChild(
        int columnSpan,
        double height)
    {
        var child = new Border
        {
            Height = height,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        Grid.SetColumnSpan(
            child,
            columnSpan);

        return child;
    }
}

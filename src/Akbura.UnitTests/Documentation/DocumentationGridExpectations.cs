using Avalonia.Controls;

namespace Akbura.UnitTests;

internal static class DocumentationGridExpectations
{
    private readonly record struct Definition(GridLength Length, double Min = 0d, double Max = double.PositiveInfinity);

    private static Definition Auto(double min = 0d, double max = double.PositiveInfinity) =>
        new(GridLength.Auto, min, max);
    private static Definition Star(double value = 1d, double min = 0d, double max = double.PositiveInfinity) =>
        new(new GridLength(value, GridUnitType.Star), min, max);
    private static Definition Pixel(double value) => new(new GridLength(value, GridUnitType.Pixel));

    public static void AssertDefinitions(string id, Grid grid)
    {
        // Expected native values are independent of the generator's parsing helpers.
        // Calling the same grid-length parser on both sides would hide a shared bug.
        var (columns, rows) = Expected(id);
        Assert.Equal(columns.Length, grid.ColumnDefinitions.Count);
        Assert.Equal(rows.Length, grid.RowDefinitions.Count);
        for (var i = 0; i < columns.Length; i++)
        {
            Assert.Equal(columns[i].Length, grid.ColumnDefinitions[i].Width);
            Assert.Equal(columns[i].Min, grid.ColumnDefinitions[i].MinWidth);
            Assert.Equal(columns[i].Max, grid.ColumnDefinitions[i].MaxWidth);
        }
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Equal(rows[i].Length, grid.RowDefinitions[i].Height);
            Assert.Equal(rows[i].Min, grid.RowDefinitions[i].MinHeight);
            Assert.Equal(rows[i].Max, grid.RowDefinitions[i].MaxHeight);
        }
        Assert.Empty(grid.Children); // The documented XML comments are not children.
    }

    private static (Definition[] Columns, Definition[] Rows) Expected(string id) => id switch
    {
        "grid.introduction" => (new[] { Star(), Pixel(100d), Auto() }, new[] { Auto(), Star(2d), Pixel(48d) }),
        "grid.lengths" => ([Auto(), Star(), Star(2d), Pixel(100d)], []),
        "grid.commas" => ([Star(), Pixel(100d), Auto()], []),
        "grid.whitespace" => ([], [Auto(), Star(), Star(2d), Pixel(48d)]),
        "grid.constraints" => (
        [
            Star(min: 100d), Star(max: 300d), Star(min: 100d, max: 300d),
            Star(min: 100d, max: 300d), Auto(max: 100d),
        ], []),
        "grid.minimum" => ([Star(min: 100d)], []),
        "grid.maximum" => ([Star(max: 300d)], []),
        "grid.min-max" => ([Star(min: 100d, max: 300d)], []),
        "grid.explicit-length" => ([Star(2d, min: 100d, max: 300d), Auto(max: 100d)], []),
        "grid.complete" => ([Auto(), Star(min: 120d, max: 320d), Star(2d)],
            [Auto(), Star(), Pixel(48d)]),
        _ => throw new InvalidOperationException($"Missing native Grid expectations for '{id}'."),
    };
}

using Avalonia;
using Avalonia.Controls;

namespace Akbura.FeatureGallery.Markup;

public sealed class ResponsiveGrid : AvaloniaObject
{
    public static readonly AttachedProperty<ColumnDefinitions?> ColumnDefinitionsProperty =
        AvaloniaProperty.RegisterAttached<ResponsiveGrid, Grid, ColumnDefinitions?>(
            "ColumnDefinitions");

    public static readonly AttachedProperty<RowDefinitions?> RowDefinitionsProperty =
        AvaloniaProperty.RegisterAttached<ResponsiveGrid, Grid, RowDefinitions?>(
            "RowDefinitions");

    static ResponsiveGrid()
    {
        ColumnDefinitionsProperty.Changed.AddClassHandler<Grid>(
            static (grid, args) =>
                grid.ColumnDefinitions =
                    (ColumnDefinitions?)args.NewValue ?? new ColumnDefinitions());
        RowDefinitionsProperty.Changed.AddClassHandler<Grid>(
            static (grid, args) =>
                grid.RowDefinitions =
                    (RowDefinitions?)args.NewValue ?? new RowDefinitions());
    }

    public static ColumnDefinitions? GetColumnDefinitions(Grid target) =>
        target.GetValue(ColumnDefinitionsProperty);

    public static void SetColumnDefinitions(Grid target, ColumnDefinitions? value) =>
        target.SetValue(ColumnDefinitionsProperty, value);

    public static RowDefinitions? GetRowDefinitions(Grid target) =>
        target.GetValue(RowDefinitionsProperty);

    public static void SetRowDefinitions(Grid target, RowDefinitions? value) =>
        target.SetValue(RowDefinitionsProperty, value);
}

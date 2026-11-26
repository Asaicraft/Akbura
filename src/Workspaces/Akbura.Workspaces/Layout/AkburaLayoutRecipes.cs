using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Defines the bundled layout recipes and loads their embedded Akbura source.
/// </summary>
internal static class AkburaLayoutRecipes
{
    private const string Controls = "using Avalonia.Controls;";
    private const string Styles = "using Akbura.Styles.akcss;";
    private const string Variants = "using Akbura.Markup;";
    private const string Row = "Akbura.Row";
    private const string Border = "Avalonia.Controls.Border";
    private const string TextBlock = "Avalonia.Controls.TextBlock";
    private const string StackPanel = "Avalonia.Controls.StackPanel";

    private static readonly AkburaLayoutVariantRequirement Medium = new("md", "Akbura.Markup.mdExtension", 768d);
    private static readonly AkburaLayoutVariantRequirement Large = new("lg", "Akbura.Markup.lgExtension", 1024d);

    public static ImmutableArray<AkburaLayoutRecipeDescriptor> Descriptors { get; } =
    [
        new(
            "responsive-card-grid", 1, "Responsive card grid",
            "Wrap cards into one, two, or three columns as the window grows.",
            ["layout", "responsive", "grid", "cards"], "akbura", "ResponsiveCardGrid.akbura",
            ["using Akbura;", Variants, Controls, Styles],
            [Row, Border, TextBlock],
            [new("gap", Row, Row, ["double"]), new("col-span", Border, "Avalonia.Controls.Control", ["int"])],
            [Medium, Large],
            ["Give Row a finite available width.", "Load the standard spacing resource with a double value for --spacing."],
            [
                "Row uses 12 logical columns. Each card spans 12 below 768 DIPs, 6 from 768 DIPs, and 4 from 1024 DIPs.",
                "Cards wrap when their spans exhaust the current row. The gap utility sets both row and column spacing.",
            ],
            ["Adapt the sample children to the collection and item content used by the application."],
            ["The breakpoints follow the top-level client width. Row lays out its children using the width allocated by its parent."]),
        new(
            "responsive-spacing", 1, "Responsive spacing",
            "Increase a border's padding at medium and large window widths.",
            ["layout", "responsive", "spacing", "padding"], "akbura", "ResponsiveSpacing.akbura",
            [Variants, Controls, Styles],
            [Border, TextBlock],
            [new("p", Border, "Avalonia.Controls.Decorator", ["double"])],
            [Medium, Large],
            ["Load the standard spacing resource with a double value for --spacing."],
            [
                "Padding uses scale values 2 below 768 DIPs, 4 from 768 DIPs, and 6 from 1024 DIPs.",
                "As the window narrows the remaining active breakpoint or base value takes effect.",
                "The final padding in DIPs is the utility value multiplied by the current --spacing resource.",
            ],
            ["Keep competing local padding assignments out of the sample element when adopting these utilities."],
            ["The breakpoints follow the top-level client width. Padding depends on the active spacing resource."]),
        new(
            "responsive-secondary-content", 1, "Responsive secondary content",
            "Show supplementary content below the primary content on wider windows.",
            ["layout", "responsive", "visibility"], "akbura", "ResponsiveSecondaryContent.akbura",
            [Variants, Controls, Styles],
            [StackPanel, Border, TextBlock],
            [
                new("gap", StackPanel, StackPanel, ["double"]),
                new("hidden", Border, "Avalonia.Controls.Control", []),
                new("visible", Border, "Avalonia.Controls.Control", []),
            ],
            [Large],
            ["Place the vertical stack in a parent that allows its children to stretch across the available width.",
             "Load the standard spacing resource with a double value for --spacing."],
            [
                "The primary content stretches across the available width in both states.",
                "Below 1024 DIPs only the primary content participates in layout and the stack has zero gap.",
                "From 1024 DIPs the secondary content is visible below the primary with a gap of four spacing units. Narrowing the window removes both.",
            ],
            ["Put essential controls and information in the primary section."],
            ["The breakpoint follows the top-level client width. This sample uses a vertical stack."]),
    ];

    public static string ReadExample(AkburaLayoutRecipeDescriptor recipe)
    {
        var filename = "Akbura.Workspaces.Layout.Recipes." + recipe.SuggestedFileName;
        using var stream = typeof(AkburaLayoutRecipes).Assembly.GetManifestResourceStream(filename) ??
            throw new InvalidOperationException($"Missing embedded layout recipe '{filename}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

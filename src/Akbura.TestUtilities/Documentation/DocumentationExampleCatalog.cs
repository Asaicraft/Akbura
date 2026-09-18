using System;
using System.Collections.Generic;
using System.Linq;

namespace Akbura.TestUtilities.Documentation;

/// <summary>
/// Explicit compilation contexts, not a heuristic that silently skips a failing snippet.
/// The original block always remains an unchanged substring of the complete source.
/// </summary>
internal sealed record DocumentationExample(
    string Id,
    string Path,
    string Section,
    int Ordinal = 0,
    string Prefix = "",
    string Suffix = "",
    string CompanionCSharp = "",
    string NamespaceName = "Demo",
    string ComponentName = "DocumentationSample",
    string? ExpectedDiagnostic = null,
    string Context = "Complete component; no markup scaffold.")
{
    public MarkdownCodeBlock ReadBlock() => MarkdownDocumentation.Get(Path, Section, Ordinal);

    public string CompleteSource(MarkdownCodeBlock block) => Prefix + block.Code + Suffix;
}

internal static class DocumentationExampleCatalog
{
    public const string ForeachPage = "akbura/foreach.md";
    public const string ConditionalPage = "akbura/conditional-markup.md";
    public const string GridPage = "akbura/grid.md";
    public const string MainPage = "index.md";
    private const string Controls = "using Avalonia.Controls;\n";

    // This model is test scaffolding. The conditional page refers to Person but
    // does not provide its declaration; only the properties read by its examples are supplied.
    private const string PersonModel = """
        namespace Demo;
        public sealed class Person
        {
            public string Name { get; set; } = "";
            public bool Selected { get; set; }
            public bool CanEdit { get; set; }
        }
        """;

    public static IReadOnlyList<DocumentationExample> All { get; } = new DocumentationExample[]
    {
        new("foreach.basic", ForeachPage, "Basic syntax"),
        new("foreach.property-element", ForeachPage, "Where a loop can be placed"),
        new("foreach.invalid-border", ForeachPage, "Where a loop can be placed", 1,
            Prefix: Controls,
            ExpectedDiagnostic: "AKBURA_SEMANTIC_UnsupportedForeachContentDestination",
            Context: "Negative example exactly as documented; add the normal Avalonia.Controls import only."),
        new("foreach.observable", ForeachPage, "Observable sources"),
        new("foreach.immutable", ForeachPage, "Immutable and other enumerable sources"),
        new("foreach.keys", ForeachPage, "Explicit keys"),
        new("foreach.x-id", ForeachPage, "The `x.id` shorthand"),
        new("foreach.index", ForeachPage, "The source index"),
        new("foreach.guards", ForeachPage, "Local code, `continue`, and `break`"),
        new("foreach.nested", ForeachPage, "Nested loops"),

        new("conditional.toggle", ConditionalPage, "Syntax"),
        new("conditional.scalar", ConditionalPage, "Content destinations",
            Prefix: Controls + "state bool expanded = false;\n",
            Context: "Supply the expanded state used by the preceding Syntax section and the Controls import."),
        new("conditional.item-template", ConditionalPage, "Templates",
            Prefix: Controls + "using Demo;\n" +
                "state Person[] people = [new Person { Name = \"Ada\", Selected = true }, " +
                "new Person { Name = \"Grace\", Selected = false }];\n" +
                "<ItemsControl ItemsSource={people}>\n",
            Suffix: "\n</ItemsControl>\n", CompanionCSharp: PersonModel,
            Context: "Property-element fragment: supply its native ItemsControl host, two fixture items, and Person."),
        new("conditional.root-template", ConditionalPage, "Templates", 1,
            Prefix: Controls + "using Demo;\n" +
                "state Person model = new Person { Name = \"Ada\", Selected = true };\n" +
                "<ContentControl Content={model}>\n",
            Suffix: "\n</ContentControl>\n", CompanionCSharp: PersonModel,
            Context: "Property-element fragment: supply its native ContentControl host, a fixture item, and Person."),
        new("conditional.pattern", ConditionalPage, "C# and branch-local scope",
            Prefix: Controls + "using Demo;\nstate object model = new Person { Name = \"Ada\" };\n",
            CompanionCSharp: PersonModel,
            Context: "Supply the model value and Person type omitted from the scope demonstration."),

        new("grid.introduction", GridPage, "", Prefix: Controls,
            Context: "Supply the normal Avalonia.Controls import; keep the XML comment and definition literals."),
        new("grid.lengths", GridPage, "Grid lengths", Prefix: Controls),
        new("grid.commas", GridPage, "Separating definitions", Prefix: Controls),
        new("grid.whitespace", GridPage, "Separating definitions", 1, Prefix: Controls),
        new("grid.constraints", GridPage, "Minimum and maximum sizes", Prefix: Controls),
        new("grid.minimum", GridPage, "Minimum size", Prefix: Controls),
        new("grid.maximum", GridPage, "Maximum size", Prefix: Controls),
        new("grid.min-max", GridPage, "Minimum and maximum size", Prefix: Controls),
        new("grid.explicit-length", GridPage, "Minimum and maximum size", 1, Prefix: Controls),
        new("grid.complete", GridPage, "Complete example", Prefix: Controls),

        new("main.counter", MainPage, "Create your first component",
            NamespaceName: "MyApp.Components", ComponentName: "Counter"),
        new("main.component", MainPage, "Components and Markup", Prefix: Controls,
            Context: "Supply the normal Avalonia.Controls import; keep both documented state declarations."),
        new("main.resources", MainPage, "Dictionary Resources"),
        new("main.styles", MainPage, "Avalonia Styles"),
    };

    // Inventory assertions cover EVERY akbura fence on these pages. A new block
    // fails the inventory test until it receives a runnable context or a negative expectation.
    public static IReadOnlyList<string> FullyCoveredPages { get; } = new[]
    {
        ForeachPage, ConditionalPage, GridPage,
    };

    public static IEnumerable<DocumentationExample> Positive => All.Where(example => example.ExpectedDiagnostic == null);

    public static DocumentationExample Get(string id) => All.Single(example => example.Id == id);

    public static IEnumerable<object[]> PositiveModes()
    {
        foreach (var example in Positive)
        {
            yield return new object[] { example.Id, false };
            yield return new object[] { example.Id, true };
        }
    }
}

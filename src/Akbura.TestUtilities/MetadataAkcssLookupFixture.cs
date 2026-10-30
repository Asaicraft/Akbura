using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace Akbura.TestUtilities;

/// <summary>
/// Builds isolated annotated assemblies for metadata reconstruction and parity tests.
/// </summary>
internal static class MetadataAkcssLookupFixture
{
    private const string Attributes = """
        namespace Akbura.CompilerAnotations {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class AkcssModuleReferenceAttribute(System.Type type) : System.Attribute {}
            public sealed class AkcssModuleAttribute(string path) : System.Attribute {}
            public sealed class AkcssSymbolAttribute : System.Attribute {
                public string Name { get; set; } = "";
                public string MetadataName { get; set; } = "";
                public int Kind { get; set; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class AkcssOperationAttribute : System.Attribute {
                public int Order { get; set; }
                public int ParentOrder { get; set; } = -1;
                public int Kind { get; set; }
                public bool HasErrors { get; set; }
                public string[] AppliedSymbols { get; set; } = [];
            }
        }
        """;

    public static string Style(string carrier, string metadataName, params string[] operations) =>
        $"[AkcssSymbol(Name = {Quote(carrier)}, MetadataName = {Quote(metadataName)})]\n" +
        string.Join("\n", operations) + $"\npublic class {carrier} {{}}";

    public static string Apply(int order, params string[] names) =>
        $"[AkcssOperation(Order = {order}, Kind = 2, AppliedSymbols = new string[] {{ {string.Join(", ", names.Select(Quote))} }})]";

    public static string Operation(int kind, int order, int parent = -1) =>
        $"[AkcssOperation(Order = {order}, Kind = {kind}, ParentOrder = {parent})]";

    public static CSharpCompilation Create(params (string Name, string[] Symbols)[] modules)
    {
        var source = "using Akbura.CompilerAnotations;\n" +
            string.Join("\n", modules.Select(module => $"[assembly: AkcssModuleReference(typeof({module.Name}))]")) + "\n" + Attributes + "\n" +
            string.Join("\n", modules.Select(module => $"[AkcssModule({Quote(module.Name)})] public static class {module.Name} {{\n{string.Join("\n", module.Symbols)}\n}}"));
        return CSharpCompilation.Create("MetadataLookup_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static MetadataReference Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static CSharpCompilation Consumer(MetadataReference reference) => CSharpCompilation.Create(
        "MetadataConsumer_" + Guid.NewGuid().ToString("N"), references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), reference],
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public static IEnumerable<(string Name, CSharpCompilation Compilation)> Cases()
    {
        yield return ("NoApply", Create(("A", [Style("Seed", "Seed", Operation(1, 10), Operation(0, 20, 10), Operation(3, 30))]),
            ("B", [Style("Other", "Other", Operation(3, 0))])));
        yield return ("OneApply", Create(("A", [Style("Seed", "Seed"), Style("Owner", "Owner", Apply(0, "Seed"))])));
        yield return ("MultipleModules", Create(("A", [Style("Seed", "Seed"), Style("Owner", "Owner", Apply(0, "Seed"), Apply(1, "Seed"))]),
            ("B", [Style("Owner", "OtherOwner", Apply(0, "Seed"))])));
        yield return ("Duplicates", Create(("A", [Style("Seed", "Shared"), Style("Owner", "OwnerA", Apply(0, "Shared"))]),
            ("B", [Style("Seed", "Shared"), Style("Owner", "OwnerB", Apply(0, "Shared"))]),
            ("C", [Style("Owner", "OwnerC", Apply(0, "Shared"))]),
            ("D", [Style("First", "Shared"), Style("Second", "Shared"), Style("Owner", "OwnerD", Apply(0, "Shared"))])));
        yield return ("Missing", Create(("A", [Style("Seed", "Seed"), Style("Owner", "Owner", Apply(0, "Missing")),
            Style("Partial", "Partial", Apply(0, "Seed", "Missing", "Seed")), Style("Ordinal", "Ordinal", Apply(0, "seed"))])));
        yield return ("EmptyApply", Create(("A", [Style("Owner", "Owner", Apply(0))])));
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}

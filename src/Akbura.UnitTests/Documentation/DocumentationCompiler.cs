using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.TestUtilities.Documentation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Reflection;

namespace Akbura.UnitTests;

internal static class DocumentationCompiler
{
    private static readonly ConcurrentDictionary<(string Id, bool Structural), Lazy<CompiledDocumentationExample>>
        Cache = new();

    public static CompiledDocumentationExample Compile(string id, bool structural) =>
        Cache.GetOrAdd((id, structural), static key => new Lazy<CompiledDocumentationExample>(
            () => CompileCore(DocumentationExampleCatalog.Get(key.Id), key.Structural))).Value;

    public static DocumentationCompilation CreateCompilation(DocumentationExample example, bool structural = false)
    {
        var block = example.ReadBlock();
        var source = example.CompleteSource(block);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural) options = options.WithPreprocessorSymbols("DEBUG");

        // This partial adds only an isolated engine and state inspection. It does
        // not replace a documented state with a test-only property, or reimplement rendering.
        var host = $$"""
            namespace {{example.NamespaceName}};
            public partial class {{example.ComponentName}} : global::Akbura.AkburaControl
            {
                public {{example.ComponentName}}() : base(global::Akbura.Engine.AkburaEngine.Empty) { }

                public global::System.Collections.Immutable.ImmutableArray<global::Akbura.ComponentTree.State>
                    DocumentationStates() => GetStates();
            }
            """;

        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(host, options, path: "DocumentationHost.cs"),
        };
        if (!string.IsNullOrEmpty(example.CompanionCSharp))
        {
            trees.Add(CSharpSyntaxTree.ParseText(example.CompanionCSharp, options,
                path: "DocumentationFixtureModel.cs"));
        }

        var csharp = CSharpCompilation.Create(
            "Documentation_" + Guid.NewGuid().ToString("N"), trees,
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText(source, example.ComponentName + ".akbura");
        var compilation = new AkburaCompilation(csharp, [tree], rootNamespace: example.NamespaceName);
        var semantic = compilation.GetSemanticModel(tree);
        return new(example, block, source, options, csharp, tree, semantic);
    }

    private static CompiledDocumentationExample CompileCore(DocumentationExample example, bool structural)
    {
        Assert.Null(example.ExpectedDiagnostic);
        var fixture = CreateCompilation(example, structural);
        var root = fixture.Tree.GetRoot();
        var syntaxDiagnostics = root.GetDiagnostics().ToArray();
        Assert.True(syntaxDiagnostics.Length == 0, fixture.DescribeFailure("Akbura syntax",
            string.Join(Environment.NewLine, syntaxDiagnostics.Select(diagnostic =>
                diagnostic.Code + ": " + diagnostic.Message))));

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(root).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(root);
        Assert.True(semanticDiagnostics.IsEmpty, fixture.DescribeFailure("Akbura semantics",
            string.Join(Environment.NewLine, semanticDiagnostics.Select(diagnostic =>
                diagnostic.Code + ": " + diagnostic.Message))));

        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            example.ComponentName + ".akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var compilation = fixture.CSharp.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated,
            fixture.ParseOptions, path: example.ComponentName + ".g.cs"));
        var errors = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, fixture.DescribeFailure("Generated C#",
            string.Join(Environment.NewLine, errors.AsEnumerable())) + "\n\n" + generated);

        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, fixture.DescribeFailure("Emit",
            string.Join(Environment.NewLine, emitted.Diagnostics)) + "\n\n" + generated);
        return new(example, fixture.Block, output.ToArray());
    }
}

internal sealed record DocumentationCompilation(
    DocumentationExample Example,
    MarkdownCodeBlock Block,
    string Source,
    CSharpParseOptions ParseOptions,
    CSharpCompilation CSharp,
    AkburaSyntaxTree Tree,
    AkburaSemanticModel SemanticModel)
{
    public string DescribeFailure(string stage, string diagnostics) =>
        $"{Example.Id}: {stage} failed at {Block.Location}.\n" +
        $"Context: {Example.Context}\n{diagnostics}\n\nDocumented block:\n{Block.Code}\n" +
        $"\nComplete test source (explicit fixture included):\n{Source}";
}

internal sealed class CompiledDocumentationExample
{
    private readonly Lazy<Type> _type;

    public CompiledDocumentationExample(DocumentationExample example, MarkdownCodeBlock block, byte[] image)
    {
        Example = example;
        Block = block;
        _type = new Lazy<Type>(() => Assembly.Load(image).GetType(
            example.NamespaceName + "." + example.ComponentName, throwOnError: true)!);
    }

    public DocumentationExample Example { get; }
    public MarkdownCodeBlock Block { get; }
    public Type Type => _type.Value;
}

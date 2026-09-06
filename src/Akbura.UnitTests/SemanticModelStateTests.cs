using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using AkburaOperation = Akbura.Language.Operations.IOperation;

namespace Akbura.UnitTests;

public sealed class SemanticModelStateTests
{
    [Fact]
    public void ImportedLeafResults_KeepFreshOwnersAndMatchFreshSemanticAndGeneratedResults()
    {
        var tree = ParseComponent(
            "using Avalonia.Controls;\r\nparam double input = 10;\r\nstate int count = 2;\r\n" +
            "<Border Width={input + count} />\r\n");
        var csharp = CreateCompilation();
        var previous = new AkburaCompilation(csharp, [tree], "Demo");
        var oldModel = previous.GetSemanticModel(tree);
        var parameter = Assert.Single(tree.GetRoot().Members.OfType<ParamDeclarationSyntax>());
        var attribute = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupPlainAttributeSyntax>());
        var oldParameter = Assert.IsType<ParamSymbol>(oldModel.GetDeclaredSymbol(parameter));
        var oldOperation = Assert.IsAssignableFrom<AkburaOperation>(oldModel.GetOperation(attribute));
        var oldDiagnostics = DescribeDiagnostics(oldModel, tree);
        var oldSource = Generate(oldModel, tree);
        var states = previous.GetReusableSemanticStates();
        var state = states[tree];
        Assert.True(state.CachedResultCount > 0);
        Assert.Same(oldParameter, state.DeclarationSymbolInfos[parameter].Symbol);

        var current = new AkburaCompilation(csharp, [tree], "Demo");
#if STATS
        using var measurement = GenerationStatistics.BeginMeasurement();
#endif
        Assert.True(current.TryImportSemanticState(tree, state));
        Assert.Same(state, current.GetReusableSemanticStates()[tree]);
#if STATS
        Assert.Equal(0, measurement.GetSnapshot().SemanticModelCreatedCount);
#endif
        var currentModel = current.GetSemanticModel(tree);
        var freshModel = new AkburaCompilation(csharp, [tree], "Demo").GetSemanticModel(tree);

        Assert.NotSame(oldModel, currentModel);
        Assert.Same(current, currentModel.Compilation);
        Assert.Same(currentModel, currentModel.BindingSession.RootBinder.SemanticModel);
        Assert.Same(current, currentModel.GetBinder(attribute).Compilation);
        Assert.NotSame(oldModel.BindingSession, currentModel.BindingSession);
        Assert.NotSame(oldModel.DeclarationSymbols, currentModel.DeclarationSymbols);
        Assert.Same(oldParameter, currentModel.GetDeclaredSymbol(parameter));
        Assert.Equal(freshModel.GetDeclaredSymbol(parameter)!.ToDisplayString(), oldParameter.ToDisplayString());

        var currentOperation = Assert.IsAssignableFrom<AkburaOperation>(currentModel.GetOperation(attribute));
        Assert.NotSame(oldOperation, currentOperation);
        Assert.Equal(DescribeOperation(freshModel.GetOperation(attribute)), DescribeOperation(currentOperation));
        var oldBound = oldModel.BindingSession.BindOperationSyntax(attribute);
        var currentBound = currentModel.BindingSession.BindOperationSyntax(attribute);
        Assert.NotSame(oldBound, currentBound);
        Assert.Same(current, currentBound.Binder.Compilation);
        Assert.Equal(oldDiagnostics, DescribeDiagnostics(currentModel, tree));
        Assert.Equal(DescribeDiagnostics(freshModel, tree), DescribeDiagnostics(currentModel, tree));
        Assert.Equal(oldSource, Generate(currentModel, tree));
        Assert.Equal(Generate(freshModel, tree), Generate(currentModel, tree));

        var generatedTree = CSharpSyntaxTree.ParseText(
            Generate(currentModel, tree), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = csharp.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .ToArray();
        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Import_RejectsChangedTreeCompilationOptionsAndAlreadyPublishedModel()
    {
        const string source = "param int input = 1;\r\n";
        var tree = ParseComponent(source);
        var csharp = CreateCompilation();
        var previous = new AkburaCompilation(csharp, [tree], "Demo");
        var parameter = Assert.Single(tree.GetRoot().Members.OfType<ParamDeclarationSyntax>());
        _ = previous.GetSemanticModel(tree).GetDeclaredSymbol(parameter);
        var state = previous.GetReusableSemanticStates()[tree];

        var reparsed = ParseComponent(source);
        Assert.False(new AkburaCompilation(csharp, [reparsed], "Demo").TryImportSemanticState(reparsed, state));
        var changedCSharp = csharp.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal sealed class Unrelated { }\r\n"));
        Assert.False(new AkburaCompilation(changedCSharp, [tree], "Demo").TryImportSemanticState(tree, state));
        Assert.False(new AkburaCompilation(csharp, [tree], "Other").TryImportSemanticState(tree, state));
        Assert.False(new AkburaCompilation(csharp, [tree], "Demo", "other-directory").TryImportSemanticState(tree, state));
        var published = new AkburaCompilation(csharp, [tree], "Demo");
        _ = published.GetSemanticModel(tree);
        Assert.False(published.TryImportSemanticState(tree, state));
    }

    [Fact]
    public void Capture_ExcludesProbeBoundSymbolsAndMutableComponentGraphs()
    {
        var tree = ParseComponent(
            "using Avalonia.Controls;\r\nparam View? child;\r\n<Border />\r\n");
        var csharp = CreateCompilation();
        var compilation = new AkburaCompilation(csharp, [tree], "Demo");
        var model = compilation.GetSemanticModel(tree);
        var parameter = Assert.Single(tree.GetRoot().Members.OfType<ParamDeclarationSyntax>());
        var symbol = Assert.IsType<ParamSymbol>(model.GetDeclaredSymbol(parameter));
        Assert.NotNull(symbol.Type.Symbol);
        Assert.NotSame(csharp.Assembly, symbol.Type.Symbol!.ContainingAssembly);
        _ = model.GetDeclaredSymbol(tree.GetRoot());
        _ = model.GetSemanticDiagnostics(tree.GetRoot());

        var state = compilation.GetReusableSemanticStates()[tree];
        Assert.False(state.DeclarationSymbolInfos.ContainsKey(parameter));
        Assert.False(state.SymbolInfos.ContainsKey(parameter));
        Assert.False(state.DeclarationSymbolInfos.ContainsKey(tree.GetRoot()));
        Assert.False(state.SymbolInfos.ContainsKey(tree.GetRoot()));

        var current = new AkburaCompilation(csharp, [tree], "Demo");
        Assert.True(current.TryImportSemanticState(tree, state));
        Assert.NotSame(symbol, current.GetSemanticModel(tree).GetDeclaredSymbol(parameter));
        Assert.Equal(DescribeDiagnostics(model, tree), DescribeDiagnostics(current.GetSemanticModel(tree), tree));
    }

    [Fact]
    public void Capture_UnqueriedModelDoesNotProduceAnEmptyReusableEntry()
    {
        var tree = ParseComponent("param int input = 1;\r\n");
        var compilation = new AkburaCompilation(CreateCompilation(), [tree]);
        _ = compilation.GetSemanticModel(tree);

        Assert.Empty(compilation.GetReusableSemanticStates());
    }

    private static string[] DescribeDiagnostics(AkburaSemanticModel model, ComponentSyntaxTree tree)
    {
        return model.GetSemanticDiagnostics(tree.GetRoot())
            .Select(static diagnostic => diagnostic.Code + ":" + diagnostic.Span + ":" + diagnostic.Message)
            .ToArray();
    }

    private static string DescribeOperation(AkburaOperation? operation)
    {
        if (operation == null)
        {
            return "null";
        }

        return operation.Kind + ":" + operation.HasErrors + ":" + operation.TargetSymbol?.ToDisplayString() + ":" +
            operation.TypeSymbol?.ToDisplayString() + ":" + operation.CSharpDefinition.ToDisplayString() + "[" +
            string.Join(",", operation.Children.Select(DescribeOperation)) + "]";
    }

    private static string Generate(AkburaSemanticModel model, ComponentSyntaxTree tree)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetDeclaredSymbol(tree.GetRoot()));
        return ComponentDocumentWriter.Generate(component, model, "View.akbura", new Dictionary<AkburaSyntax, string>()).ToString();
    }

    private static ComponentSyntaxTree ParseComponent(string source) =>
        ComponentSyntaxTree.ParseText(SourceText.From(source), "View.akbura");

    private static CSharpCompilation CreateCompilation() => CSharpCompilation.Create(
        "SemanticModelStateTests",
        references: SymbolTests.CreateAvaloniaReferences(),
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}

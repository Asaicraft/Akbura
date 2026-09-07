using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkcssLookupReuseTests
{
    [Fact]
    public void RepeatedApply_PreservesLocalPrecedenceImportOrderAndUtilityParameters()
    {
        var component = ParseComponent(
            """
            @akcss {
                @using Shared.First.akcss;
                @using Shared.Second.akcss;
                .shared { }
                .first { @apply shared imported enabled scale-2; }
                .second { @apply shared imported enabled scale-3; }
            }
            """);
        var firstImport = ParseStyles(
            """
            .shared { }
            .imported { }
            @utilities {
                .enabled { }
                .scale-(double value) { }
            }
            """, "First");
        var secondImport = ParseStyles(".imported { }", "Second");
        var model = CreateModel(component, firstImport, secondImport);
        var applies = GetApplies(component);
        var first = BindApply(model, applies[0]);
        var second = BindApply(model, applies[1]);

        Assert.Equal(4, first.AppliedSymbols.Length);
        Assert.Equal(4, second.AppliedSymbols.Length);
        Assert.Same(component.GetRoot(), first.AppliedSymbols[0].DeclarationSyntax!.Root);
        Assert.Same(firstImport.GetRoot(), first.AppliedSymbols[1].DeclarationSyntax!.Root);
        Assert.Equal("shared", first.AppliedSymbols[0].ClassName);
        Assert.Equal("imported", first.AppliedSymbols[1].ClassName);
        var parameterless = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(first.AppliedSymbols[2]);
        Assert.Equal("enabled", parameterless.Name);
        Assert.Empty(parameterless.Parameters);
        var parameterized = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(first.AppliedSymbols[3]);
        var parameter = Assert.Single(parameterized.Parameters);
        Assert.Equal("scale", parameterized.Name);
        Assert.Equal("value", parameter.CSharpName);
        Assert.Equal(0, parameter.Ordinal);
        Assert.Equal(SpecialType.System_Double, parameter.CSharpParameter!.Type.SpecialType);

        for (var i = 0; i < first.AppliedSymbols.Length; i++)
        {
            Assert.Same(first.AppliedSymbols[i].DeclarationSyntax, second.AppliedSymbols[i].DeclarationSyntax);
        }

        foreach (var apply in applies)
        {
            Assert.Empty(model.GetSemanticDiagnostics(apply));
            Assert.All(model.GetAkcssApplyItemReferences(apply), reference =>
            {
                Assert.NotNull(reference.Symbol);
                Assert.Equal(reference.Text, component.Text.ToString(reference.SourceSpan));
            });
        }
    }

    [Fact]
    public void MutatingLookupSymbols_DoesNotAffectLaterLookupsOrCanonicalDeclarations()
    {
        var component = ParseComponent(
            """
            @akcss {
                @using Avalonia.Controls;
                Border.seed { Width: 12; }
                @utilities { Border.enabled { Height: 24; } }
                Border.first { @apply seed enabled; }
                Border.second { @apply seed enabled; }
            }
            """);
        var model = CreateModel(component);
        var applies = GetApplies(component);
        var first = BindApply(model, applies[0]);
        Assert.Equal(2, first.AppliedSymbols.Length);

        foreach (var lookupSymbol in first.AppliedSymbols)
        {
            Assert.Empty(lookupSymbol.Operations);
            var declared = Assert.IsAssignableFrom<IAkcssSymbol>(
                model.GetDeclaredSymbol(lookupSymbol.DeclarationSyntax!));
            Assert.NotSame(lookupSymbol, declared);
            var operation = Assert.Single(declared.Operations);
            Assert.Same(declared, operation.ContainingAkcssSymbol);
            Assert.False(operation.HasErrors);
            Assert.Empty(lookupSymbol.Operations);
            if (lookupSymbol is AkcssStyleSymbol style)
            {
                style.SetOperations(declared.Operations);
                style.SetInterceptType(declared.TargetType);
            }
            else
            {
                var utility = Assert.IsType<TailwindUtilitySymbol>(lookupSymbol);
                utility.SetOperations(declared.Operations);
                utility.SetInterceptType(declared.TargetType);
            }

            Assert.Single(lookupSymbol.Operations);
            Assert.True(lookupSymbol.IsIntercepted);
            Assert.False(declared.IsIntercepted);
            Assert.Same(declared, Assert.Single(declared.Operations).ContainingAkcssSymbol);
        }

        var second = BindApply(model, applies[1]);
        for (var i = 0; i < first.AppliedSymbols.Length; i++)
        {
            Assert.NotSame(first.AppliedSymbols[i], second.AppliedSymbols[i]);
            Assert.Same(first.AppliedSymbols[i].DeclarationSyntax, second.AppliedSymbols[i].DeclarationSyntax);
            Assert.Empty(second.AppliedSymbols[i].Operations);
            Assert.False(second.AppliedSymbols[i].IsIntercepted);
        }
    }

    [Fact]
    public void RepeatedInvalidApply_PreservesDiagnosticMultiplicitySeverityAndLocations()
    {
        var component = ParseComponent(
            """
            @akcss {
                .duplicate { }
                .duplicate { }
                .first { @apply duplicate missing; }
                .second { @apply duplicate missing; }
            }
            """);
        var model = CreateModel(component);
        var applies = GetApplies(component);
        var diagnostics = new List<AkburaSemanticDiagnostic>();

        foreach (var apply in applies)
        {
            var operation = Assert.IsAssignableFrom<IAkcssApplyOperation>(model.GetOperation(apply));
            Assert.True(operation.HasErrors);
            Assert.Empty(operation.AppliedSymbols);
            var beforeReferences = model.GetSemanticDiagnostics(apply);
            Assert.Collection(beforeReferences,
                diagnostic => Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemAmbiguous, diagnostic.Code),
                diagnostic => Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemNotFound, diagnostic.Code));
            Assert.All(beforeReferences, diagnostic =>
            {
                Assert.Same(apply, diagnostic.Syntax);
                Assert.Equal(apply.Span, diagnostic.Span);
                Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
            });

            var references = model.GetAkcssApplyItemReferences(apply);
            Assert.Equal(["duplicate", "missing"], references.Select(static reference => reference.Text));
            Assert.All(references, reference =>
            {
                Assert.Null(reference.Symbol);
                Assert.Equal(reference.Text, component.Text.ToString(reference.SourceSpan));
            });
            Assert.Equal(beforeReferences, model.GetSemanticDiagnostics(apply));
            diagnostics.AddRange(beforeReferences);
        }

        Assert.Equal(4, diagnostics.Count);
        Assert.Equal(2, diagnostics.Select(static diagnostic => diagnostic.Span).Distinct().Count());
    }

    [Fact]
    public void InlineLayers_WithSameUtilityName_KeepTheirOwnUsingDirectivesAndSymbols()
    {
        var component = ParseComponent(
            """
            @akcss {
                @using First;
                @utilities { Widget.select-(Value value) { } }
                Widget.first { @apply select-1; }
                Widget.second { @apply select-2; }
            }
            @akcss {
                @using Second;
                @utilities { Widget.select-(Value value) { } }
                Widget.first { @apply select-1; }
                Widget.second { @apply select-2; }
            }
            """);
        var csharp = CreateCSharpCompilation(
            """
            namespace First {
                public class Widget : Avalonia.Controls.Control { }
                public class Value { }
            }
            namespace Second {
                public class Widget : Avalonia.Controls.Control { }
                public class Value { }
            }
            """);
        var model = new AkburaCompilation(csharp, [component]).GetSemanticModel(component);
        var applies = GetApplies(component);
        var utilities = applies.Select(apply => Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            Assert.Single(BindApply(model, apply).AppliedSymbols))).ToArray();

        Assert.Equal(4, utilities.Length);
        Assert.Same(utilities[0].DeclarationSyntax, utilities[1].DeclarationSyntax);
        Assert.Same(utilities[2].DeclarationSyntax, utilities[3].DeclarationSyntax);
        Assert.NotSame(utilities[0].DeclarationSyntax, utilities[2].DeclarationSyntax);
        for (var i = 0; i < utilities.Length; i++)
        {
            var expectedNamespace = i < 2 ? "First" : "Second";
            Assert.Equal(expectedNamespace + ".Widget", utilities[i].TargetType.Symbol!.ToDisplayString());
            var parameter = Assert.Single(utilities[i].Parameters);
            Assert.Equal(expectedNamespace + ".Value", parameter.CSharpParameter!.Type.ToDisplayString());
            Assert.Empty(model.GetSemanticDiagnostics(applies[i]));
        }
    }

    [Fact]
    public void SharedSyntaxAcrossCompilations_DoesNotReusePreviousCSharpParameterSymbols()
    {
        var component = ParseComponent(
            """
            @akcss {
                @using Demo;
                @utilities { Widget.select-(Value value) { } }
                Widget.first { @apply select-1; }
                Widget.second { @apply select-2; }
            }
            """);
        var beforeCSharp = CreateCSharpCompilation(
            "namespace Demo { public class Widget : Avalonia.Controls.Control { } public class Value { public int Before; } }");
        var afterCSharp = CreateCSharpCompilation(
            "namespace Demo { public class Widget : Avalonia.Controls.Control { } public class Value { public string After = string.Empty; } }");
        var before = new AkburaCompilation(beforeCSharp, [component]).GetSemanticModel(component);
        var after = new AkburaCompilation(afterCSharp, [component]).GetSemanticModel(component);
        var applies = GetApplies(component);
        var first = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            Assert.Single(BindApply(before, applies[0]).AppliedSymbols));
        var second = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            Assert.Single(BindApply(after, applies[0]).AppliedSymbols));
        var firstType = Assert.Single(first.Parameters).CSharpParameter!.Type;
        var secondType = Assert.Single(second.Parameters).CSharpParameter!.Type;

        Assert.NotSame(first, second);
        Assert.NotSame(firstType, secondType);
        Assert.Single(firstType.GetMembers("Before"));
        Assert.Empty(firstType.GetMembers("After"));
        Assert.Empty(secondType.GetMembers("Before"));
        Assert.Single(secondType.GetMembers("After"));
        var firstAgain = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            Assert.Single(BindApply(before, applies[1]).AppliedSymbols));
        var secondAgain = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            Assert.Single(BindApply(after, applies[1]).AppliedSymbols));
        Assert.Same(firstType, Assert.Single(firstAgain.Parameters).CSharpParameter!.Type);
        Assert.Same(secondType, Assert.Single(secondAgain.Parameters).CSharpParameter!.Type);
    }

    [Fact]
    public void RepeatedGeneration_AfterApplyReferenceQueries_PreservesOutputAndCompiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), nameof(AkcssLookupReuseTests));
        var styles = AkcssSyntaxTree.ParseText(SourceText.From(
            """
            @using Avalonia.Controls;
            Border.seed { Width: 12; }
            @utilities {
                Border.enabled { Height: 24; }
                Border.scale-(double value) { Opacity: value; }
            }
            Border.first { @apply seed enabled scale-1; }
            Border.second { @apply seed enabled scale-2; }
            """), Path.Combine(directory, "Styles.akcss"), "Demo.Styles.akcss");
        ImmutableArray<AkburaSyntaxTree> trees = [styles];
        var csharp = CreateCSharpCompilation();
        var index = AkburaProjectIndex.Create(csharp, trees, "Demo", directory);
        var descriptor = Assert.Single(index.ExternalAkcssDescriptors);
        Assert.True(index.TryResolveAkcss(descriptor, out var input));
        var first = AkcssDocumentWriter.Generate(input, index.AkcssSourceMap, index.RootNamespace);

        foreach (var apply in styles.GetRoot().DescendantNodes().OfType<AkcssApplyDirectiveSyntax>())
        {
            Assert.Equal(3, input.SemanticModel.GetAkcssApplyItemReferences(apply).Length);
            Assert.Empty(input.SemanticModel.GetSemanticDiagnostics(apply));
        }

        var second = AkcssDocumentWriter.Generate(input, index.AkcssSourceMap, index.RootNamespace);
        Assert.Equal(first.ToString(), second.ToString());
        var generatedTree = CSharpSyntaxTree.ParseText(first,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = csharp.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
        Assert.Empty(diagnostics);
    }

#if STATS
    [Fact]
    public void ParameterlessUtilities_DoNotCreateCSharpParameterProbes()
    {
        var component = ParseComponent(
            """
            @akcss {
                @utilities {
                    .enabled { }
                    .self-start { }
                }
            }
            """);
        var model = CreateModel(component);
        var utilities = component.GetRoot().DescendantNodes().OfType<AkcssUtilityDeclarationSyntax>().ToArray();
        Assert.Equal(2, utilities.Length);
        using var measurement = GenerationStatistics.BeginMeasurement(trackOperations: true);

        for (var i = 0; i < 3; i++)
        {
            foreach (var utility in utilities)
            {
                Assert.Empty(model.CreateTailwindUtilityParameters(utility));
            }
        }

        var snapshot = measurement.GetSnapshot();
        Assert.Equal(0, snapshot.GetStageInvocationCount(GenerationStatisticStage.CSharpUtilityParameters));
        Assert.DoesNotContain(snapshot.OperationMeasurements,
            static row => row.Operation == GenerationStatisticOperation.UtilityParameterBinding);
    }

    [Fact]
    public void RepeatedApplyAndReferenceQueries_ConstructEachLookupLayerOncePerModel()
    {
        var component = ParseComponent(
            """
            @akcss {
                @using Shared.Styles.akcss;
                .local { }
                .first { @apply local imported enabled scale-1; }
                .second { @apply local imported enabled scale-2; }
                .third { @apply local imported enabled scale-3; }
            }
            """);
        var styles = ParseStyles(
            """
            .imported { }
            @utilities {
                .enabled { }
                .scale-(double value) { }
            }
            """, "Styles");
        var model = CreateModel(component, styles);
        using var measurement = GenerationStatistics.BeginMeasurement(trackOperations: true);
        foreach (var apply in GetApplies(component))
        {
            Assert.Equal(4, BindApply(model, apply).AppliedSymbols.Length);
            Assert.Equal(4, model.GetAkcssApplyItemReferences(apply).Length);
            Assert.Empty(model.GetSemanticDiagnostics(apply));
        }

        var snapshot = measurement.GetSnapshot();
        var rows = snapshot.OperationMeasurements
            .Where(static row => row.Operation == GenerationStatisticOperation.AkcssLookupSymbols).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, static row => Assert.Equal(1, row.InvocationCount));
        Assert.Single(rows.Select(static row => row.OwnerId).Distinct());
        Assert.True(snapshot.AkcssLookupRequestCount > rows.Length);
        Assert.Equal(snapshot.AkcssLookupRequestCount - rows.Length, snapshot.AkcssLookupReusedCount);
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.CSharpUtilityParameters));
    }
#endif

    private static ComponentSyntaxTree ParseComponent(string source)
    {
        var tree = ComponentSyntaxTree.ParseText(SourceText.From(source), "Lookup.akbura");
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        return tree;
    }

    private static AkcssSyntaxTree ParseStyles(string source, string name)
    {
        var tree = AkcssSyntaxTree.ParseText(source, name + ".akcss", "Shared." + name + ".akcss");
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        return tree;
    }

    private static AkburaSemanticModel CreateModel(ComponentSyntaxTree component, params AkcssSyntaxTree[] styles)
    {
        return new AkburaCompilation(CreateCSharpCompilation(), [component], styles).GetSemanticModel(component);
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(nameof(AkcssLookupReuseTests),
            sources.Select(source => CSharpSyntaxTree.ParseText(source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))),
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static AkcssApplyDirectiveSyntax[] GetApplies(ComponentSyntaxTree tree)
    {
        return tree.GetRoot().DescendantNodes().OfType<AkcssApplyDirectiveSyntax>().ToArray();
    }

    private static IAkcssApplyOperation BindApply(AkburaSemanticModel model, AkcssApplyDirectiveSyntax apply)
    {
        var operation = Assert.IsAssignableFrom<IAkcssApplyOperation>(model.GetOperation(apply));
        Assert.False(operation.HasErrors);
        return operation;
    }
}

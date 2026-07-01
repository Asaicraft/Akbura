using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using BinderType = Akbura.Language.Binder.Binder;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.UnitTests;

public sealed class SemanticArchitectureTests
{
    [Fact]
    public void DeclarationTable_CollectsComponentAndAkcssDeclarations()
    {
        const string code =
            "using System;\n" +
            "namespace Demo.Pages;\n" +
            "\n" +
            "@akcss {\n" +
            "    .card { Background: White; }\n" +
            "    @utilities { .w-(double value) { Width: value; } }\n" +
            "}\n" +
            "\n" +
            "inject ILogger<Dashboard> logger;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(UserId) { logger.LogInformation(\"load\"); }\n" +
            "<TextBlock Text=\"Hello\" />\n";
        const string akcss =
            "@using Demo.Theme;\n" +
            ".shared { Padding: 4; }\n";

        var tree = AkburaSyntaxTree.ParseText(code, "Pages/Dashboard.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(akcss, "Pages/Dashboard.akcss", "Demo.Pages.Dashboard.akcss");
        var compilation = CreateCompilation(tree, [akcssTree]);

        var table = compilation.DeclarationTable;

        var component = Assert.Single(table.Components);
        Assert.Equal(AkburaDeclarationKind.Component, component.Kind);
        Assert.Equal("Dashboard", component.Name);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.Using);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.Namespace);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssModule);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.InjectedService);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.Parameter);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.State);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.Command);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.UseEffect);
        Assert.Contains(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.MarkupRoot);

        var inlineAkcss = Assert.Single(component.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssModule);
        Assert.Contains(inlineAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssStyle);
        Assert.Contains(inlineAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssUtility);

        var externalAkcss = Assert.Single(table.AkcssModules);
        Assert.Equal("Demo.Pages.Dashboard.akcss", externalAkcss.Name);
        Assert.Contains(externalAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssUsing);
        Assert.Contains(externalAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssStyle);
    }

    [Fact]
    public void DeclarationCollector_UsesPooledVisitorAndResetsState()
    {
        var poolField = typeof(AkburaDeclarationCollector).GetField(
            "s_pool",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(poolField);
        Assert.Same(
            typeof(ObjectPool<AkburaDeclarationCollector>),
            poolField.FieldType);

        const string firstCode =
            "@akcss { .first { Background: White; } }\n" +
            "state int first = 0;";
        const string secondCode =
            "@akcss { .second { Background: White; } }\n" +
            "state int second = 0;";

        var first = AkburaDeclarationCollector.Collect(
            AkburaSyntaxTree.ParseText(firstCode, "First.akbura"));
        var second = AkburaDeclarationCollector.Collect(
            AkburaSyntaxTree.ParseText(secondCode, "Second.akbura"));

        Assert.Equal("First", first.Name);
        Assert.Equal("Second", second.Name);
        Assert.Contains(first.Children, declaration => declaration.Kind == AkburaDeclarationKind.State && declaration.Name == "first");
        Assert.DoesNotContain(first.Children, declaration => declaration.Name == "second");
        Assert.Contains(second.Children, declaration => declaration.Kind == AkburaDeclarationKind.State && declaration.Name == "second");
        Assert.DoesNotContain(second.Children, declaration => declaration.Name == "first");

        var firstInlineAkcss = Assert.Single(first.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssModule);
        var secondInlineAkcss = Assert.Single(second.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssModule);
        Assert.Contains(firstInlineAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssStyle && declaration.Name == ".first");
        Assert.DoesNotContain(firstInlineAkcss.Children, declaration => declaration.Name == ".second");
        Assert.Contains(secondInlineAkcss.Children, declaration => declaration.Kind == AkburaDeclarationKind.AkcssStyle && declaration.Name == ".second");
        Assert.DoesNotContain(secondInlineAkcss.Children, declaration => declaration.Name == ".first");
    }

    [Fact]
    public void SyntaxAndDeclarationManager_LazilyOwnsTreesOrdinalsAndDeclarationTable()
    {
        var component = AkburaSyntaxTree.ParseText("state int count = 0;", "Pages/Counter.akbura");
        var akcss = AkcssSyntaxTree.ParseText(".card { Padding: 4; }", "Pages/Counter.akcss", "Demo.Pages.Counter.akcss");
        var compilation = CreateCompilation(component, [akcss]);

        var state = compilation.SyntaxAndDeclarations.GetLazyState();
        var secondState = compilation.SyntaxAndDeclarations.GetLazyState();

        Assert.Same(state, secondState);
        Assert.Same(component, Assert.Single(state.SyntaxTrees));
        Assert.Same(akcss, Assert.Single(state.AkcssSyntaxTrees));
        Assert.Equal(0, state.SyntaxOrdinalMap[component]);
        Assert.Equal(0, state.AkcssOrdinalMap[akcss]);
        Assert.Same(compilation.DeclarationTable, state.DeclarationTable);
        Assert.Equal("Counter", Assert.Single(state.DeclarationTable.Components).Name);
        Assert.Equal("Demo.Pages.Counter.akcss", Assert.Single(state.DeclarationTable.AkcssModules).Name);
    }

    [Fact]
    public void DeclarationTable_UsesPooledDeclarationStackForTraversal()
    {
        var stackField = typeof(AkburaDeclarationTable).GetField(
            "s_declarationStack",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(stackField);
        Assert.Same(
            typeof(ObjectPool<Stack<AkburaDeclaration>>),
            stackField.FieldType);

        const string code =
            """
            if(true)
            {
                <StackPanel>
                    <Border>
                        <TextBlock Text="Hello" />
                    </Border>
                </StackPanel>
            }
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Nested.akbura");
        var table = CreateCompilation(tree).DeclarationTable;
        var root = tree.GetRoot();
        var statement = Assert.IsType<CSharpStatementSyntax>(Assert.Single(root.Members));
        var markupRoot = Assert.IsType<MarkupRootSyntax>(Assert.Single(statement.Body!.Tokens));
        var border = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(markupRoot.Element.Body)).Element;
        var textBlock = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(border.Body)).Element;

        Assert.True(table.TryGetDeclaration(textBlock, out var declaration));
        Assert.Equal(AkburaDeclarationKind.MarkupElement, declaration.Kind);
        Assert.Equal("TextBlock", declaration.Name);
    }

    [Fact]
    public void SyntaxAndDeclarationManager_ReplaceSyntaxTreeReusesUnchangedDeclarations()
    {
        var first = AkburaSyntaxTree.ParseText("state int first = 0;", "First.akbura");
        var oldSecond = AkburaSyntaxTree.ParseText("state int second = 0;", "Second.akbura");
        var newSecond = AkburaSyntaxTree.ParseText("state int second = 1;", "Second.akbura");
        var oldCompilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [first, oldSecond]);
        var oldTable = oldCompilation.DeclarationTable;

        var newCompilation = oldCompilation.ReplaceSyntaxTree(oldSecond, newSecond);
        var newTable = newCompilation.DeclarationTable;

        Assert.NotSame(oldCompilation, newCompilation);
        Assert.Same(first, newCompilation.SyntaxTrees[0]);
        Assert.Same(newSecond, newCompilation.SyntaxTrees[1]);
        Assert.Same(oldTable.Components[0], newTable.Components[0]);
        Assert.NotSame(oldTable.Components[1], newTable.Components[1]);
        Assert.Equal(0, newCompilation.SyntaxAndDeclarations.GetLazyState().SyntaxOrdinalMap[first]);
        Assert.Equal(1, newCompilation.SyntaxAndDeclarations.GetLazyState().SyntaxOrdinalMap[newSecond]);
    }

    [Fact]
    public void SyntaxAndDeclarationManager_AddRemoveAkcssTreesUpdatesDeclarationTable()
    {
        var component = AkburaSyntaxTree.ParseText("state int count = 0;", "Counter.akbura");
        var firstAkcss = AkcssSyntaxTree.ParseText(".first { Padding: 4; }", "First.akcss", "First.akcss");
        var secondAkcss = AkcssSyntaxTree.ParseText(".second { Padding: 8; }", "Second.akcss", "Second.akcss");
        var compilation = CreateCompilation(component, [firstAkcss]);
        var oldTable = compilation.DeclarationTable;

        var added = compilation.AddAkcssSyntaxTrees([secondAkcss]);
        var removed = added.RemoveAkcssSyntaxTrees([firstAkcss]);

        Assert.Equal(2, added.DeclarationTable.AkcssModules.Length);
        Assert.Same(oldTable.AkcssModules[0], added.DeclarationTable.AkcssModules[0]);
        var remaining = Assert.Single(removed.DeclarationTable.AkcssModules);
        Assert.Equal("Second.akcss", remaining.Name);
        Assert.Same(secondAkcss, Assert.Single(removed.AkcssSyntaxTrees));
    }

    [Fact]
    public void SyntaxAndDeclarationManager_UpdateDoesNotForceColdPreviousState()
    {
        var first = AkburaSyntaxTree.ParseText("state int first = 0;", "First.akbura");
        var second = AkburaSyntaxTree.ParseText("state int second = 0;", "Second.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [first]);

        var added = compilation.AddSyntaxTrees([second]);

        Assert.False(compilation.SyntaxAndDeclarations.HasLazyState);
        Assert.False(added.SyntaxAndDeclarations.HasLazyState);

        var table = added.DeclarationTable;

        Assert.False(compilation.SyntaxAndDeclarations.HasLazyState);
        Assert.True(added.SyntaxAndDeclarations.HasLazyState);
        Assert.Equal(2, table.Components.Length);
        Assert.Equal("First", table.Components[0].Name);
        Assert.Equal("Second", table.Components[1].Name);
    }

    [Fact]
    public void AkburaCompilation_LazilyCachesSemanticModelsConcurrently()
    {
        var tree = AkburaSyntaxTree.ParseText("state int count = 0;", "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var cacheField = typeof(AkburaCompilation).GetField(
            "_semanticModels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var models = new AkburaSemanticModel[32];

        Parallel.For(0, models.Length, index =>
        {
            models[index] = compilation.GetSemanticModel(tree);
        });

        Assert.NotNull(cacheField);
        Assert.Same(
            typeof(System.Collections.Concurrent.ConcurrentDictionary<AkburaSyntaxTree, AkburaSemanticModel>),
            cacheField.FieldType);
        Assert.All(models, model => Assert.Same(models[0], model));
    }

    [Fact]
    public void SmallDictionary_AddUpdateLookupAndEnumerate()
    {
        var dictionary = new SmallDictionary<string, int>();

        dictionary.Add("first", 1);
        dictionary.Add("second", 2);
        dictionary["second"] = 22;

        Assert.True(dictionary.ContainsKey("first"));
        Assert.True(dictionary.TryGetValue("second", out var second));
        Assert.Equal(22, second);
        Assert.False(dictionary.TryGetValue("missing", out _));
        Assert.Equal(1, dictionary["first"]);

        var keys = new HashSet<string>();
        foreach (var key in dictionary.Keys)
        {
            keys.Add(key);
        }

        var values = new HashSet<int>();
        foreach (var value in dictionary.Values)
        {
            values.Add(value);
        }

        Assert.Equal(2, keys.Count);
        Assert.Contains("first", keys);
        Assert.Contains("second", keys);
        Assert.Equal(2, values.Count);
        Assert.Contains(1, values);
        Assert.Contains(22, values);
    }

    [Fact]
    public void SmallDictionary_HandlesHashCollisions()
    {
        var dictionary = new SmallDictionary<string, int>(new ConstantHashComparer());

        dictionary.Add("first", 1);
        dictionary.Add("second", 2);
        dictionary.Add("third", 3);
        dictionary["second"] = 20;

        Assert.Equal(1, dictionary["first"]);
        Assert.Equal(20, dictionary["second"]);
        Assert.Equal(3, dictionary["third"]);
        Assert.Throws<InvalidOperationException>(() => dictionary.Add("third", 30));

        var pairs = new Dictionary<string, int>();
        foreach (var pair in dictionary)
        {
            pairs.Add(pair.Key, pair.Value);
        }

        Assert.Equal(3, pairs.Count);
        Assert.Equal(20, pairs["second"]);
    }

    [Fact]
    public void SyntaxTree_WithChangedText_NoChangeReturnsSameTree()
    {
        const string code = "state int count = 0;\n<TextBlock Text=\"Hello\" />";
        const string akcss = ".card { Padding: 4; }";

        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(akcss, "Counter.akcss");

        Assert.Same(tree, tree.WithChangedText(SourceText.From(code), []));
        Assert.Same(akcssTree, akcssTree.WithChangedText(SourceText.From(akcss), []));
    }

    [Fact]
    public void SyntaxTree_WithChangedText_EditRoundTrips()
    {
        const string oldCode = "state int count = 0;\n<TextBlock Text=\"Hello\" />";
        const string newCode = "state int count = 1;\n<TextBlock Text=\"Hello\" />";
        var changeStart = oldCode.IndexOf("0", StringComparison.Ordinal);
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var tree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");

        var newTree = tree.WithChangedText(SourceText.From(newCode), [change]);

        Assert.NotSame(tree, newTree);
        Assert.Equal(newCode, newTree.GetRoot().ToFullString());
        Assert.Equal(newCode.Length, newTree.GetRoot().FullWidth);
    }

    [Fact]
    public void SemanticBindingCache_NoChangeCompilationReturnsSameSnapshotCachedSymbol()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var oldSymbol = Assert.IsAssignableFrom<IStateSymbol>(model.GetSymbolInfo(state).Symbol);

        var newTree = tree.WithChangedText(SourceText.From(code), []);
        var newCompilation = compilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newState = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[0]);
        var newSymbol = Assert.IsAssignableFrom<IStateSymbol>(newModel.GetSymbolInfo(newState).Symbol);

        Assert.Same(tree, newTree);
        Assert.Same(compilation, newCompilation);
        Assert.Same(model, newModel);
        Assert.Same(oldSymbol, newSymbol);
    }

    [Fact]
    public void SemanticBindingCache_StateInitializerEdit_RebindsUnchangedTopLevelSymbolsInNewSnapshot()
    {
        const string oldCode =
            "state int first = 0;\n" +
            "state int changed = 0;\n" +
            "state int last = 2;";
        const string newCode =
            "state int first = 0;\n" +
            "state int changed = 1;\n" +
            "state int last = 2;";
        var changeStart = oldCode.IndexOf("changed = 0", StringComparison.Ordinal) + "changed = ".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldFirst = Assert.IsType<StateDeclarationSyntax>(oldTree.GetRoot().Members[0]);
        var oldChanged = Assert.IsType<StateDeclarationSyntax>(oldTree.GetRoot().Members[1]);
        var oldLast = Assert.IsType<StateDeclarationSyntax>(oldTree.GetRoot().Members[2]);
        var oldFirstSymbol = oldModel.GetSymbolInfo(oldFirst).Symbol;
        var oldChangedSymbol = oldModel.GetSymbolInfo(oldChanged).Symbol;
        var oldLastSymbol = oldModel.GetSymbolInfo(oldLast).Symbol;

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newFirst = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[0]);
        var newChanged = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[1]);
        var newLast = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[2]);
        var newFirstSymbol = newModel.GetSymbolInfo(newFirst).Symbol;
        var newChangedSymbol = newModel.GetSymbolInfo(newChanged).Symbol;
        var newLastSymbol = newModel.GetSymbolInfo(newLast).Symbol;

        Assert.Same(oldFirst.Green, newFirst.Green);
        Assert.Same(oldLast.Green, newLast.Green);
        Assert.NotSame(oldFirstSymbol, newFirstSymbol);
        Assert.NotSame(oldChangedSymbol, newChangedSymbol);
        Assert.NotSame(oldLastSymbol, newLastSymbol);
        Assert.Same(newFirstSymbol, newModel.GetSymbolInfo(newFirst).Symbol);
        Assert.Same(newLastSymbol, newModel.GetSymbolInfo(newLast).Symbol);
    }

    [Fact]
    public void SemanticBindingCache_ChangedComponentScopeDoesNotReuseUnchangedDeclaration()
    {
        const string oldCode =
            "using Demo.A;\n" +
            "\n" +
            "state Widget value = new Widget();";
        const string newCode =
            "using Demo.B;\n" +
            "\n" +
            "state Widget value = new Widget();";
        const string csharpCode =
            """
            namespace Demo.A
            {
                public sealed class Widget { }
            }

            namespace Demo.B
            {
                public sealed class Widget { }
            }
            """;
        var changeStart = oldCode.IndexOf("Demo.A", StringComparison.Ordinal) + "Demo.".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var csharpCompilation = CreateCSharpCompilation()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(csharpCode));
        var oldCompilation = new AkburaCompilation(
            csharpCompilation,
            [oldTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldState = Assert.IsType<StateDeclarationSyntax>(oldTree.GetRoot().Members[1]);
        var oldStateSymbol = Assert.IsAssignableFrom<IStateSymbol>(
            oldModel.GetSymbolInfo(oldState).Symbol);
        var oldBoundNode = oldModel.BindingSession.BindSemanticSyntax(oldState);

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newState = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[1]);
        var newStateSymbol = Assert.IsAssignableFrom<IStateSymbol>(
            newModel.GetSymbolInfo(newState).Symbol);
        var newBoundNode = newModel.BindingSession.BindSemanticSyntax(newState);

        Assert.Same(oldState.Green, newState.Green);
        Assert.Equal("global::Demo.A.Widget", oldStateSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal("global::Demo.B.Widget", newStateSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.NotSame(oldStateSymbol, newStateSymbol);
        Assert.NotSame(oldBoundNode, newBoundNode);
    }

    [Fact]
    public void SemanticBindingCache_ChangedComponentScopeDoesNotReuseUnchangedMarkupOperation()
    {
        const string oldCode =
            "using Avalonia.Controls;\n" +
            "using Demo.A;\n" +
            "\n" +
            "<TextBlock Text={Widget.Value.ToString()} />";
        const string newCode =
            "using Avalonia.Controls;\n" +
            "using Demo.B;\n" +
            "\n" +
            "<TextBlock Text={Widget.Value.ToString()} />";
        const string csharpCode =
            """
            namespace Demo.A
            {
                public static class Widget
                {
                    public static int Value => 1;
                }
            }

            namespace Demo.B
            {
                public static class Widget
                {
                    public static int Value => 2;
                }
            }
            """;
        var changeStart = oldCode.IndexOf("Demo.A", StringComparison.Ordinal) + "Demo.".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var csharpCompilation = CreateCSharpCompilation()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(csharpCode));
        var oldCompilation = new AkburaCompilation(
            csharpCompilation,
            [oldTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldMarkup = Assert.IsType<MarkupRootSyntax>(oldTree.GetRoot().Members[2]);
        var oldAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            oldMarkup.Element.StartTag!.Attributes[0]);
        var oldOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            oldModel.GetOperation(oldAttribute));

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newMarkup = Assert.IsType<MarkupRootSyntax>(newTree.GetRoot().Members[2]);
        var newAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            newMarkup.Element.StartTag!.Attributes[0]);
        var newOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            newModel.GetOperation(newAttribute));

        Assert.Same(oldAttribute.Green, newAttribute.Green);
        Assert.NotSame(oldOperation, newOperation);
    }

    [Fact]
    public void SemanticBindingCache_StateInitializerEdit_RebindsMarkupBoundOperationAndCachesWithinSnapshot()
    {
        const string oldCode =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<TextBlock Text={missing + 1} />";
        const string newCode =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 1;\n" +
            "\n" +
            "<TextBlock Text={missing + 1} />";
        var changeStart = oldCode.IndexOf("count = 0", StringComparison.Ordinal) + "count = ".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldMarkup = Assert.IsType<MarkupRootSyntax>(oldTree.GetRoot().Members[2]);
        var oldAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            oldMarkup.Element.StartTag!.Attributes[0]);
        var oldBoundNode = oldModel.BindingSession.BindOperationSyntax(oldAttribute);
        var oldOperation = oldModel.GetOperation(oldAttribute);
        var oldDiagnostics = oldModel.GetSemanticDiagnostics(oldAttribute);

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newMarkup = Assert.IsType<MarkupRootSyntax>(newTree.GetRoot().Members[2]);
        var newAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            newMarkup.Element.StartTag!.Attributes[0]);
        var newBoundNode = newModel.BindingSession.BindOperationSyntax(newAttribute);
        var newOperation = newModel.GetOperation(newAttribute);
        var newDiagnostics = newModel.GetSemanticDiagnostics(newAttribute);

        Assert.Same(oldAttribute.Green, newAttribute.Green);
        Assert.NotSame(oldBoundNode, newBoundNode);
        Assert.NotSame(oldOperation, newOperation);
        Assert.Same(newBoundNode, newModel.BindingSession.BindOperationSyntax(newAttribute));
        Assert.Same(newOperation, newModel.GetOperation(newAttribute));
        Assert.Equal(oldDiagnostics.Select(diagnostic => diagnostic.Code), newDiagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Contains(
            newDiagnostics,
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);
    }

    [Fact]
    public void SemanticBindingCache_ChangedExternalComponentDeclarationDoesNotReuseMarkupOperation()
    {
        const string dashboardCode =
            """
            using Demo.Components;

            param string Title = "Hello";

            <TaskCard Title={Title} />
            """;
        const string oldTaskCardCode =
            """
            namespace Demo.Components;

            param string Title = "";
            """;
        const string newTaskCardCode =
            """
            namespace Demo.Components;

            param int Title = 0;
            """;
        var dashboardTree = AkburaSyntaxTree.ParseText(
            dashboardCode,
            "Pages/Dashboard.akbura");
        var oldTaskCardTree = AkburaSyntaxTree.ParseText(
            oldTaskCardCode,
            "Components/TaskCard.akbura");
        var csharpCompilation = CreateCSharpCompilation();
        var oldCompilation = new AkburaCompilation(
            csharpCompilation,
            [dashboardTree, oldTaskCardTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var oldModel = oldCompilation.GetSemanticModel(dashboardTree);
        var oldMarkup = Assert.IsType<MarkupRootSyntax>(dashboardTree.GetRoot().Members[2]);
        var oldAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            oldMarkup.Element.StartTag!.Attributes[0]);
        var oldOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            oldModel.GetOperation(oldAttribute));

        var newTaskCardTree = oldTaskCardTree.WithChangedText(SourceText.From(newTaskCardCode), [
            new TextChangeRange(
                new TextSpan(
                    oldTaskCardCode.IndexOf("string", StringComparison.Ordinal),
                    "string".Length),
                "int".Length)
        ]);
        var newCompilation = oldCompilation.WithSyntaxTrees([dashboardTree, newTaskCardTree]);
        var newModel = newCompilation.GetSemanticModel(dashboardTree);
        var newMarkup = Assert.IsType<MarkupRootSyntax>(dashboardTree.GetRoot().Members[2]);
        var newAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            newMarkup.Element.StartTag!.Attributes[0]);
        var newOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            newModel.GetOperation(newAttribute));

        Assert.Same(oldAttribute.Green, newAttribute.Green);
        Assert.Equal("String", oldOperation.Property?.Parameter?.Type.Name);
        Assert.Equal("Int32", newOperation.Property?.Parameter?.Type.Name);
        Assert.NotSame(oldOperation, newOperation);
    }

    [Fact]
    public void MemberSemanticModel_CachesByComponentScope()
    {
        const string code =
            "state int count = 0;\n" +
            "param string Text = \"Hello\";";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);

        var rootModel = model.GetMemberSemanticModel(root);
        var stateModel = model.GetMemberSemanticModel(state);
        var paramModel = model.GetMemberSemanticModel(param);

        Assert.Same(rootModel, stateModel);
        Assert.Same(rootModel, paramModel);
        Assert.Same(root, rootModel.Scope);
        Assert.IsType<AkburaSemanticModel.ComponentMemberSemanticModel>(rootModel);
    }

    [Fact]
    public void MemberSemanticModel_DifferentComponentsUseDifferentModels()
    {
        var firstTree = AkburaSyntaxTree.ParseText("state int first = 0;", "First.akbura");
        var secondTree = AkburaSyntaxTree.ParseText("state int second = 0;", "Second.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [firstTree, secondTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var firstModel = compilation.GetSemanticModel(firstTree);
        var secondModel = compilation.GetSemanticModel(secondTree);

        Assert.NotSame(
            firstModel.GetMemberSemanticModel(firstTree.GetRoot()),
            secondModel.GetMemberSemanticModel(secondTree.GetRoot()));
    }

    [Fact]
    public void MemberSemanticModel_DeclarationSymbolInfoRoutesThroughMemberModel()
    {
        const string code =
            "state int count = 0;\n" +
            "command int Refresh(int userId);";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[0]);
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[1]);
        var memberModel = model.GetMemberSemanticModel(state);

        var stateFromMemberModel = Assert.IsAssignableFrom<IStateSymbol>(
            memberModel.GetSymbolInfo(state).Symbol);
        var stateFromFacade = Assert.IsAssignableFrom<IStateSymbol>(
            model.GetSymbolInfo(state).Symbol);
        var commandFromMemberModel = Assert.IsAssignableFrom<ICommandSymbol>(
            memberModel.GetSymbolInfo(command).Symbol);
        var commandFromFacade = Assert.IsAssignableFrom<ICommandSymbol>(
            model.GetSymbolInfo(command).Symbol);

        Assert.Same(stateFromMemberModel, stateFromFacade);
        Assert.Same(commandFromMemberModel, commandFromFacade);
        Assert.Equal("Int32", stateFromFacade.Type.Name);
        Assert.Equal("Refresh", commandFromFacade.Name);
        Assert.Equal("Int32", commandFromFacade.ResultType.Name);
        Assert.Single(commandFromFacade.Parameters);
    }

    [Fact]
    public void MemberSemanticModel_ComponentSymbolExposesDeclarationMembers()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "namespace Demo.Pages;\n" +
            "\n" +
            "@akcss { .card { Background: White; } }\n" +
            "inject object service;\n" +
            "param bind string Text = \"Hello\";\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(Text, count, service, Refresh.IsExecuting) { }\n" +
            "<TextBlock Text={Text} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Pages/Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            model.GetMemberSemanticModel(root).GetSymbolInfo(root).Symbol);
        var state = Assert.Single(component.States);
        var parameter = Assert.Single(component.Parameters);
        var injectedService = Assert.Single(component.InjectedServices);
        var command = Assert.Single(component.Commands);
        var useEffect = Assert.Single(component.UseEffects);

        Assert.Same(component, model.GetSymbolInfo(root).Symbol);
        Assert.Equal("Counter", component.Name);
        Assert.Equal("Demo.Pages", component.NamespaceName);
        Assert.Equal("count", state.Name);
        Assert.Equal(StateBindingKind.None, state.BindingKind);
        Assert.Equal("Text", parameter.Name);
        Assert.Equal(ParamBindingKind.Bind, parameter.BindingKind);
        Assert.Equal("service", injectedService.Name);
        Assert.Equal("Refresh", command.Name);
        Assert.Equal("Int32", command.ResultType.Name);
        Assert.Equal(4, useEffect.Dependencies.Length);
        Assert.Single(component.MarkupRoots);
        Assert.Single(component.AkcssModules);
    }

    [Fact]
    public void MemberSemanticModel_MarkupOnlyEditRebindsDeclarationSymbolsInNewSnapshot()
    {
        const string oldCode =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "param string Text = \"Hello\";\n" +
            "<TextBlock Text=\"Old\" />";
        const string newCode =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "param string Text = \"Hello\";\n" +
            "<TextBlock Text=\"New\" />";
        var changeStart = oldCode.IndexOf("Old", StringComparison.Ordinal);
        var change = new TextChangeRange(new TextSpan(changeStart, 3), newLength: 3);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldRoot = oldTree.GetRoot();
        var oldState = Assert.IsType<StateDeclarationSyntax>(oldRoot.Members[1]);
        var oldParam = Assert.IsType<ParamDeclarationSyntax>(oldRoot.Members[2]);
        var oldStateSymbol = oldModel.GetSymbolInfo(oldState).Symbol;
        var oldParamSymbol = oldModel.GetSymbolInfo(oldParam).Symbol;

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newRoot = newTree.GetRoot();
        var newState = Assert.IsType<StateDeclarationSyntax>(newRoot.Members[1]);
        var newParam = Assert.IsType<ParamDeclarationSyntax>(newRoot.Members[2]);
        var newStateSymbol = newModel.GetSymbolInfo(newState).Symbol;
        var newParamSymbol = newModel.GetSymbolInfo(newParam).Symbol;

        Assert.Same(oldState.Green, newState.Green);
        Assert.Same(oldParam.Green, newParam.Green);
        Assert.NotSame(oldStateSymbol, newStateSymbol);
        Assert.NotSame(oldParamSymbol, newParamSymbol);
        Assert.Same(newStateSymbol, newModel.GetSymbolInfo(newState).Symbol);
        Assert.Same(newParamSymbol, newModel.GetSymbolInfo(newParam).Symbol);
        Assert.Equal(newCode.Length, newRoot.FullWidth);
    }

    [Fact]
    public void SemanticBindingCache_CachesBoundNodesBySyntax()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);

        var first = model.BindingSession.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1"));
        var second = model.BindingSession.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("2"));

        var expression = Assert.IsType<BoundLiteralExpression>(first);
        Assert.Equal("Int32", expression.Type?.Name);
        Assert.Equal(1, expression.ConstantValue);
        Assert.Same(first, second);
    }

    [Fact]
    public void SemanticBindingCache_UnchangedGreenNodeRebindsBoundNodeInNewSnapshot()
    {
        const string oldCode =
            "state int first = 0;\n" +
            "state int changed = 0;";
        const string newCode =
            "state int first = 0;\n" +
            "state int changed = 1;";
        var changeStart = oldCode.IndexOf("changed = 0", StringComparison.Ordinal) + "changed = ".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldFirst = Assert.IsType<StateDeclarationSyntax>(oldTree.GetRoot().Members[0]);
        var oldBoundNode = oldModel.BindingSession.BindExpression(
            oldFirst,
            CSharpSyntaxFactory.ParseExpression("1"));

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newFirst = Assert.IsType<StateDeclarationSyntax>(newTree.GetRoot().Members[0]);
        var newBoundNode = newModel.BindingSession.BindExpression(
            newFirst,
            CSharpSyntaxFactory.ParseExpression("2"));
        var cachedNewBoundNode = newModel.BindingSession.BindExpression(
            newFirst,
            CSharpSyntaxFactory.ParseExpression("3"));

        Assert.Same(oldFirst.Green, newFirst.Green);
        Assert.NotSame(oldBoundNode, newBoundNode);
        Assert.Same(newBoundNode, cachedNewBoundNode);
    }

    [Fact]
    public void BindingSession_TargetTypedExpressionBindingDoesNotPolluteUntypedCache()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var doubleType = model.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Double);

        var untyped = model.BindingSession.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1"));
        var converted = model.BindingSession.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1"),
            doubleType);
        var cachedUntyped = model.BindingSession.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("2"));

        Assert.IsType<BoundLiteralExpression>(untyped);
        var conversion = Assert.IsType<BoundConversionExpression>(converted);
        Assert.Equal(AkburaConversionKind.Implicit, conversion.Conversion.Kind);
        Assert.Same(untyped, cachedUntyped);
        Assert.NotSame(untyped, converted);
    }

    [Fact]
    public void OperationWalker_VisitsAkcssOperationTree()
    {
        const string code =
            "@akcss {\n" +
            "    Button.card {\n" +
            "        @if(true) {\n" +
            "            Background: White;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var block = Assert.IsType<InlineAkcssBlockSyntax>(tree.GetRoot().Members[0]);
        var rule = Assert.IsType<AkcssStyleRuleSyntax>(block.Members[0]);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(rule.Members[0]);

        var operation = Assert.IsAssignableFrom<IAkcssIfOperation>(model.GetOperation(ifDirective));
        var walker = new RecordingOperationWalker();

        walker.Visit(operation);

        Assert.Contains(AkburaOperationKind.AkcssIf, walker.Kinds);
        Assert.Contains(AkburaOperationKind.CSharpExpression, walker.Kinds);
        Assert.Contains(AkburaOperationKind.AkcssAssignment, walker.Kinds);
    }

    [Fact]
    public void BoundTree_DoesNotExposeAkburaOperations()
    {
        var operationType = typeof(AkburaOperation);
        var boundTypes = typeof(BoundNode)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(BoundNode).Namespace)
            .ToArray();

        Assert.DoesNotContain(typeof(BoundNode).GetProperties(), property =>
            property.Name == "Operation");

        foreach (var type in boundTypes)
        {
            Assert.DoesNotContain(type.GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic),
                field => operationType.IsAssignableFrom(field.FieldType));
            Assert.DoesNotContain(type.GetProperties(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic),
                property => operationType.IsAssignableFrom(property.PropertyType));
            Assert.DoesNotContain(type.GetConstructors(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()),
                parameter => operationType.IsAssignableFrom(parameter.ParameterType));
        }
    }

    [Fact]
    public void CSharpProbeBinder_DoesNotExposeAkburaCSharpOperationTree()
    {
        var csharpOperationType = typeof(ICSharpOperation);
        var binderType = typeof(CSharpProbeBinder);
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "CSharpProbeBinder.cs");

        Assert.DoesNotContain(binderType.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic),
            method =>
                csharpOperationType.IsAssignableFrom(method.ReturnType) ||
                method.GetParameters().Any(parameter =>
                    csharpOperationType.IsAssignableFrom(parameter.ParameterType)));
        Assert.DoesNotContain(nameof(CSharpOperationTreeBuilder), source);
    }

    [Fact]
    public void OperationFactory_DoesNotReferenceSemanticModelFacade()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Operations",
            "AkburaOperationFactory.cs");

        Assert.DoesNotContain(nameof(AkburaSemanticModel), source);
        Assert.DoesNotContain("_semanticModel", source);
        Assert.DoesNotContain(".GetSymbolInfo(", source);
        Assert.DoesNotContain(".GetOperation(", source);
        Assert.DoesNotContain(".GetSemanticDiagnostics(", source);
        Assert.DoesNotContain(nameof(BindingSession), source);
        Assert.Contains("CSharpOperationTreeBuilder.Create", source);
    }

    [Fact]
    public void BindingSession_DelegatesOperationBindingToBinderChain()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "BindingSession.cs");
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "AkburaSemanticModel.cs");
        var markupSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "AkburaSemanticModel.MarkupOperations.cs");

        Assert.Contains("GetOperationBinder(syntax).BindOperationSyntax(syntax)", source);
        Assert.DoesNotContain("CreateBoundMarkupAttribute", source);
        Assert.DoesNotContain("CreateBoundAkcss", source);
        Assert.DoesNotContain("BindMarkupAttributeOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("BindAkcssPropertySetterOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("ResolveMarkupAttributeOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("ResolveAkcssPropertySetterOperation", semanticModelSource + markupSemanticModelSource);
    }

    [Fact]
    public void OperationBearingBinders_OwnMarkupAndAkcssOperationEntrypoints()
    {
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "AkburaSemanticModel.cs");
        var markupSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "AkburaSemanticModel.MarkupOperations.cs");
        var markupBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "MarkupBinder.cs");
        var tailwindBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "MarkupBinder.Tailwind.cs");
        var akcssStyleBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "AkcssStyleBinder.cs");

        Assert.Contains("CreateBoundTailwindUtilityAttribute", markupBinderSource + tailwindBinderSource);
        Assert.DoesNotContain("CreateBoundTailwindUtilityAttribute", markupSemanticModelSource);

        Assert.Contains("BindAkcssPropertySetter", akcssStyleBinderSource);
        Assert.Contains("BindAkcssIf", akcssStyleBinderSource);
        Assert.Contains("BindAkcssApply", akcssStyleBinderSource);
        Assert.Contains("BindAkcssIntercept", akcssStyleBinderSource);
        Assert.DoesNotContain("CreateBoundAkcssPropertySetter(AkcssAssignmentSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssIf(AkcssIfDirectiveSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssApply(AkcssApplyDirectiveSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssIntercept(AkcssInterceptDirectiveSyntax", semanticModelSource);
    }

    [Fact]
    public void OperationFactory_CreatesOperationsFromBoundOperationNodes()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                Button.surface {
                    Width: 10;
                }

                Button.card {
                    @apply surface;
                    @if(true) {
                        Height: 20;
                    }
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <TextBlock Text="Hello" w-30 />
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Dashboard.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[1]);
        var surface = Assert.IsType<AkcssStyleRuleSyntax>(akcss.Members[0]);
        var card = Assert.IsType<AkcssStyleRuleSyntax>(akcss.Members[1]);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(surface.Members[0]);
        var apply = Assert.IsType<AkcssApplyDirectiveSyntax>(card.Members[0]);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(card.Members[1]);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            markup.Element.StartTag!.Attributes[0]);
        var tailwindAttribute = Assert.IsType<TailwindFullAttributeSyntax>(
            markup.Element.StartTag!.Attributes[1]);

        Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(textAttribute));
        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(textAttribute));

        Assert.IsType<BoundTailwindUtilityAttribute>(
            model.BindingSession.BindOperationSyntax(tailwindAttribute));
        Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            model.GetOperation(tailwindAttribute));

        Assert.IsType<BoundAkcssPropertySetter>(
            model.BindingSession.BindOperationSyntax(assignment));
        Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            model.GetOperation(assignment));

        Assert.IsType<BoundAkcssApply>(
            model.BindingSession.BindOperationSyntax(apply));
        Assert.IsAssignableFrom<IAkcssApplyOperation>(
            model.GetOperation(apply));

        Assert.IsType<BoundAkcssIf>(
            model.BindingSession.BindOperationSyntax(ifDirective));
        Assert.IsAssignableFrom<IAkcssIfOperation>(
            model.GetOperation(ifDirective));
    }

    [Fact]
    public void MarkupBinder_BindsPropertyAndEventAttributesToBoundNodes()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "<StackPanel><TextBlock Text={count.ToString()} /><Button Click={() => count++} /></StackPanel>";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var textBlockContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[0]);
        var buttonContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[1]);
        var textBlock = textBlockContent.Element;
        var button = buttonContent.Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            textBlock.StartTag!.Attributes[0]);
        var clickAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            button.StartTag!.Attributes[0]);

        var propertyBoundNode = Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(textAttribute));
        var eventBoundNode = Assert.IsType<BoundMarkupRoutedEventBinding>(
            model.BindingSession.BindOperationSyntax(clickAttribute));

        Assert.IsType<MarkupBinder>(propertyBoundNode.Binder);
        Assert.IsType<MarkupBinder>(eventBoundNode.Binder);
        Assert.Equal("Text", propertyBoundNode.Property?.Name);
        Assert.Equal("Click", eventBoundNode.RoutedEvent.Name);
    }

    [Fact]
    public void SymbolVisitor_DispatchesConcreteAkburaSymbols()
    {
        const string code =
            "inject int service;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[3]);
        var visitor = new RecordingSymbolVisitor();

        visitor.Visit(model.GetSymbolInfo(inject).Symbol);
        visitor.Visit(model.GetSymbolInfo(param).Symbol);
        visitor.Visit(model.GetSymbolInfo(state).Symbol);
        visitor.Visit(model.GetSymbolInfo(command).Symbol);

        Assert.Equal(
            ["inject", "param", "state", "command"],
            visitor.Visited);
    }

    [Fact]
    public void BinderChain_RootAndNestedBindersExposeNext()
    {
        const string code =
            "state int count = 0;\n" +
            "<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[1]);

        var rootBinder = model.BindingSession.RootBinder;
        Assert.Null(rootBinder.Next);
        Assert.Throws<InvalidOperationException>(() => rootBinder.NextRequired);

        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        Assert.True(markupBinder.Flags.HasFlag(AkburaBinderFlags.InMarkup));
        Assert.True(markupBinder.Flags.HasFlag(AkburaBinderFlags.InComponent));
        Assert.Same(markup, markupBinder.ScopeDesignator);
        Assert.IsAssignableFrom<ComponentBinder>(markupBinder.NextRequired);
        Assert.IsType<CompilationBinder>(markupBinder.NextRequired.NextRequired);
    }

    [Fact]
    public void BinderLookup_DelegatesThroughNextAndAllowsLocalShadowing()
    {
        const string code =
            "state double value = 0;\n" +
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) { Width: value; }\n" +
            "    }\n" +
            "}\n" +
            "<TextBlock Text={value.ToString()} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[1]);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(inlineAkcss.Members[0]);
        var utility = Assert.Single(utilities.Utilities);
        var diagnostics = new BindingDiagnosticBag();

        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        var delegatedSymbol = markupBinder.LookupSymbol(
            "value",
            BinderLookupOptions.None,
            markup,
            diagnostics).Symbol;

        Assert.IsAssignableFrom<IStateSymbol>(delegatedSymbol);

        var utilityBinder = Assert.IsType<AkcssStyleBinder>(model.GetBinder(utility, BinderUsage.Akcss));
        var shadowingSymbol = utilityBinder.LookupSymbol(
            "value",
            BinderLookupOptions.None,
            utility,
            diagnostics).Symbol;

        Assert.IsAssignableFrom<ITailwindUtilityParameterSymbol>(shadowingSymbol);
        Assert.Equal(AkburaSymbolKind.TailwindUtilityParameter, shadowingSymbol!.Kind);
    }

    [Fact]
    public void BinderLookup_InternalLookupUsesLookupResult()
    {
        const string code =
            "state int count = 0;\n" +
            "<TextBlock Text={count.ToString()} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var markup = Assert.IsType<MarkupRootSyntax>(tree.GetRoot().Members[1]);
        var binder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        var result = LookupResult.GetInstance();

        try
        {
            binder.LookupSymbolsInternal(
                result,
                "count",
                arity: 0,
                BinderLookupOptions.None,
                originalBinder: binder,
                markup,
                new BindingDiagnosticBag());

            Assert.True(result.IsGood);
            var symbolInfo = result.ToSymbolInfo();
            Assert.IsAssignableFrom<IStateSymbol>(symbolInfo.Symbol);
        }
        finally
        {
            result.Free();
        }
    }

    [Fact]
    public void BinderLookup_MissingSymbolLeavesLookupResultClearUntilChainEnds()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var binder = model.GetBinder(tree.GetRoot(), BinderUsage.Expression);
        var result = LookupResult.GetInstance();

        binder.LookupSymbolsInternal(
            result,
            "missing",
            arity: 0,
            BinderLookupOptions.None,
            originalBinder: binder,
            tree.GetRoot(),
            new BindingDiagnosticBag());

        var symbolInfo = result.ToSymbolInfoAndFree();
        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
    }

    [Fact]
    public void LookupResult_PooledInstancesResetBeforeReuse()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var stateSymbol = Assert.IsAssignableFrom<IStateSymbol>(model.GetSymbolInfo(state).Symbol);
        var result = LookupResult.GetInstance();
        result.SetSymbol(stateSymbol);
        result.Free();

        var reused = LookupResult.GetInstance();
        try
        {
            Assert.True(reused.IsClear);
            Assert.Null(reused.Symbol);
            Assert.Equal(AkburaCandidateReason.NotFound, reused.CandidateReason);
        }
        finally
        {
            reused.Free();
        }
    }

    [Fact]
    public void BlockBinder_ResolvesLocalVariablesAndDelegatesOuterSymbols()
    {
        const string code =
            "state int total = 0;\n" +
            "\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    Console.WriteLine(total + count);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(root.Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        Assert.IsType<CSharpStatementSyntax>(block.Tokens[0]);
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[1]);
        var binder = Assert.IsType<BlockBinder>(model.GetBinder(writeLine, BinderUsage.Statement));
        var diagnostics = new BindingDiagnosticBag();

        Assert.Same(block, binder.ScopeDesignator);
        Assert.True(binder.Flags.HasFlag(AkburaBinderFlags.InCSharpBlock));
        Assert.True(binder.Flags.HasFlag(AkburaBinderFlags.InComponent));

        var local = Assert.IsType<CSharpLocalSymbol>(binder.LookupSymbol(
            "count",
            BinderLookupOptions.None,
            writeLine,
            diagnostics).Symbol);
        Assert.Equal("count", local.Name);
        Assert.IsAssignableFrom<ILocalSymbol>(local.CSharpDefinition.Symbol);

        var delegatedState = Assert.IsAssignableFrom<IStateSymbol>(binder.LookupSymbol(
            "total",
            BinderLookupOptions.None,
            writeLine,
            diagnostics).Symbol);
        Assert.Equal("total", delegatedState.Name);
    }

    [Fact]
    public void BlockBinder_LocalVariableShadowsComponentState()
    {
        const string code =
            "state int count = 0;\n" +
            "\n" +
            "if(count > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    Console.WriteLine(count);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(tree.GetRoot().Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[1]);
        var binder = Assert.IsType<BlockBinder>(model.GetBinder(writeLine, BinderUsage.Statement));

        var symbol = binder.LookupSymbol(
            "count",
            BinderLookupOptions.None,
            writeLine,
            new BindingDiagnosticBag()).Symbol;

        Assert.IsType<CSharpLocalSymbol>(symbol);
    }

    [Fact]
    public void MarkupInsideCSharpBlock_DelegatesThroughBlockBinder()
    {
        const string code =
            "state int total = 0;\n" +
            "\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    <TextBlock Text={count.ToString()} />\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(tree.GetRoot().Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var markup = Assert.IsType<MarkupRootSyntax>(block.Tokens[1]);

        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        var blockBinder = Assert.IsType<BlockBinder>(markupBinder.NextRequired);
        var symbol = markupBinder.LookupSymbol(
            "count",
            BinderLookupOptions.None,
            markup,
            new BindingDiagnosticBag()).Symbol;

        Assert.Same(block, blockBinder.ScopeDesignator);
        Assert.IsType<CSharpLocalSymbol>(symbol);
    }

    [Fact]
    public void BinderScopeOwnership_ReturnsDeclaredSymbolsForOwnedScopes()
    {
        const string code =
            "inject int service;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(count) { }\n" +
            "@akcss {\n" +
            "    .card { Background: White; }\n" +
            "    @utilities { .w-(double value) { Width: value; } }\n" +
            "}\n" +
            "<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[5]);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(inlineAkcss.Members[1]);
        var utility = Assert.Single(utilities.Utilities);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[6]);

        var componentBinder = Assert.IsType<ComponentBinder>(model.GetBinder(root));
        var componentSymbols = componentBinder.GetDeclaredSymbolsForScope(root);

        Assert.Contains(componentSymbols, symbol => symbol is IInjectSymbol { Name: "service" });
        Assert.Contains(componentSymbols, symbol => symbol is IParamSymbol { Name: "UserId" });
        Assert.Contains(componentSymbols, symbol => symbol is IStateSymbol { Name: "count" });
        Assert.Contains(componentSymbols, symbol => symbol is ICommandSymbol { Name: "Refresh" });
        Assert.Contains(componentSymbols, symbol => symbol is IUseEffectSymbol);

        var akcssModuleBinder = Assert.IsType<AkcssModuleBinder>(model.GetBinder(inlineAkcss, BinderUsage.Akcss));
        var akcssSymbols = akcssModuleBinder.GetDeclaredSymbolsForScope(inlineAkcss);
        Assert.Contains(akcssSymbols, symbol => symbol is IAkcssSymbol { Name: "card" });
        Assert.Contains(akcssSymbols, symbol => symbol is ITailwindUtilitySymbol { Name: "w" });

        var utilityBinder = Assert.IsType<AkcssStyleBinder>(model.GetBinder(utility, BinderUsage.Akcss));
        var parameterSymbols = utilityBinder.GetDeclaredSymbolsForScope(utility);
        Assert.Single(parameterSymbols);
        Assert.IsAssignableFrom<ITailwindUtilityParameterSymbol>(parameterSymbols[0]);
        Assert.Equal(AkburaSymbolKind.TailwindUtilityParameter, parameterSymbols[0].Kind);

        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        Assert.Empty(markupBinder.GetDeclaredSymbolsForScope(markup));
    }

    [Fact]
    public void DeclarationSymbolTable_CachesSymbolsBySyntaxIdentity()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var symbolInfosField = typeof(AkburaDeclarationSymbolTable).GetField("_symbolInfos", flags);
        var declaredSymbolsField = typeof(AkburaDeclarationSymbolTable).GetField("_declaredSymbols", flags);

        Assert.NotNull(symbolInfosField);
        Assert.NotNull(declaredSymbolsField);
        Assert.Equal(typeof(Dictionary<AkburaSyntax, AkburaSymbolInfo>), symbolInfosField!.FieldType);

        var declaredSymbolsArguments = declaredSymbolsField!.FieldType.GetGenericArguments();
        Assert.Equal(typeof(ImmutableArray<AkburaSymbol>), declaredSymbolsArguments[1]);

        var keyType = declaredSymbolsArguments[0];
        Assert.Null(keyType.GetProperty("Declaration", BindingFlags.Instance | BindingFlags.Public));
        var syntaxProperty = keyType.GetProperty("Syntax", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(syntaxProperty);
        Assert.Equal(typeof(AkburaSyntax), syntaxProperty!.PropertyType);
    }

    [Fact]
    public void DeclarationSymbolTable_OwnsComponentDeclaredSymbols()
    {
        const string code =
            "inject int service;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(count) { }";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var declaration = Assert.Single(compilation.DeclarationTable.Components);

        var declaredSymbols = model.DeclarationSymbols.GetDeclaredSymbols(
            declaration,
            AkburaDeclarationKind.State,
            AkburaDeclarationKind.Parameter,
            AkburaDeclarationKind.InjectedService,
            AkburaDeclarationKind.Command,
            AkburaDeclarationKind.UseEffect);
        var binder = Assert.IsType<ComponentBinder>(model.GetBinder(root));

        Assert.Contains(declaredSymbols, symbol => symbol is IInjectSymbol { Name: "service" });
        Assert.Contains(declaredSymbols, symbol => symbol is IParamSymbol { Name: "UserId" });
        Assert.Contains(declaredSymbols, symbol => symbol is IStateSymbol { Name: "count" });
        Assert.Contains(declaredSymbols, symbol => symbol is ICommandSymbol { Name: "Refresh" });
        Assert.Contains(declaredSymbols, symbol => symbol is IUseEffectSymbol);
        var binderSymbols = binder.GetDeclaredSymbolsForScope(root);
        Assert.Equal(
            declaredSymbols.Select(symbol => (symbol.Kind, symbol.Name)),
            binderSymbols.Select(symbol => (symbol.Kind, symbol.Name)));
        Assert.Null(typeof(BinderType).GetMethod(
            "CreateSymbolsForDeclarations",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void SemanticModel_GetDeclaredSymbol_UsesDeclarationSymbolTable()
    {
        const string code =
            "inject int service;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(count) { }\n" +
            "@akcss {\n" +
            "    .card { Background: White; }\n" +
            "    @utilities { .w-(double value) { Width: value; } }\n" +
            "}\n" +
            "<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[3]);
        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[4]);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[5]);
        var style = Assert.IsType<AkcssStyleRuleSyntax>(inlineAkcss.Members[0]);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(inlineAkcss.Members[1]);
        var utility = Assert.Single(utilities.Utilities);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[6]);

        Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetDeclaredSymbol(root));
        Assert.IsAssignableFrom<IInjectSymbol>(model.GetDeclaredSymbol(inject));
        Assert.IsAssignableFrom<IParamSymbol>(model.GetDeclaredSymbol(param));
        Assert.IsAssignableFrom<IStateSymbol>(model.GetDeclaredSymbol(state));
        Assert.IsAssignableFrom<ICommandSymbol>(model.GetDeclaredSymbol(command));
        Assert.IsAssignableFrom<IUseEffectSymbol>(model.GetDeclaredSymbol(useEffect));
        Assert.IsAssignableFrom<IAkcssModuleSymbol>(model.GetDeclaredSymbol(inlineAkcss));
        Assert.IsAssignableFrom<IAkcssSymbol>(model.GetDeclaredSymbol(style));
        Assert.IsAssignableFrom<ITailwindUtilitySymbol>(model.GetDeclaredSymbol(utility));
        Assert.Same(model.GetSymbolInfo(state).Symbol, model.GetDeclaredSymbol(state));
        Assert.Null(model.GetDeclaredSymbol(markup.Element));
    }

    [Fact]
    public void SemanticModel_GetDeclaredSymbol_UsesDeclarationSymbolTableForExternalAkcss()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using Demo.Styles.Shared.akcss;\n" +
            "\n" +
            "<TextBlock class=\"surface\" w-30 />";
        const string akcss =
            "@using Avalonia.Controls;\n" +
            ".surface { Background: White; }\n" +
            "@utilities { .w-(double value) { Width: value; } }";
        var tree = AkburaSyntaxTree.ParseText(code, "Pages/Dashboard.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(
            akcss,
            "Styles/Shared.akcss",
            "Demo.Styles.Shared.akcss");
        var compilation = CreateCompilation(tree, [akcssTree]);
        var model = compilation.GetSemanticModel(tree);
        var document = akcssTree.GetRoot();
        var style = Assert.IsType<AkcssStyleRuleSyntax>(document.Members[1]);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(document.Members[2]);
        var utility = Assert.Single(utilities.Utilities);

        var module = Assert.IsAssignableFrom<IAkcssModuleSymbol>(model.GetDeclaredSymbol(document));
        var declaredSymbols = model.DeclarationSymbols.GetDeclaredSymbols(
            Assert.Single(compilation.DeclarationTable.AkcssModules),
            AkburaDeclarationKind.AkcssStyle,
            AkburaDeclarationKind.AkcssUtility);

        Assert.False(module.IsInlined);
        Assert.Null(module.ContainingSymbol);
        Assert.Equal("Demo.Styles.Shared.akcss", module.Path);
        Assert.Same(document, module.DeclaringSyntax);
        Assert.Contains(module.AkcssSymbols, symbol => symbol is IAkcssSymbol { Name: "surface" });
        Assert.Contains(module.AkcssSymbols, symbol => symbol is ITailwindUtilitySymbol { Name: "w" });
        Assert.Contains(declaredSymbols, symbol => symbol is IAkcssSymbol { Name: "surface" });
        Assert.Contains(declaredSymbols, symbol => symbol is ITailwindUtilitySymbol { Name: "w" });
        Assert.Same(module, model.GetDeclaredSymbol(document));
        Assert.IsAssignableFrom<IAkcssSymbol>(model.GetDeclaredSymbol(style));
        Assert.IsAssignableFrom<ITailwindUtilitySymbol>(model.GetDeclaredSymbol(utility));
    }

    [Fact]
    public void SemanticModel_GetDeclaredSymbol_UsesDeclarationSymbolTableForOtherComponentTree()
    {
        const string dashboardCode =
            "using Demo.Components;\n" +
            "\n" +
            "<TaskCard Title=\"Hello\" />";
        const string taskCardCode =
            "namespace Demo.Components;\n" +
            "\n" +
            "param string Title = \"\";\n" +
            "state int count = 0;";
        var dashboardTree = AkburaSyntaxTree.ParseText(
            dashboardCode,
            "Pages/DashboardPage.akbura");
        var taskCardTree = AkburaSyntaxTree.ParseText(
            taskCardCode,
            "Components/TaskCard.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [dashboardTree, taskCardTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var model = compilation.GetSemanticModel(dashboardTree);
        var taskCardRoot = taskCardTree.GetRoot();
        var param = Assert.IsType<ParamDeclarationSyntax>(taskCardRoot.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(taskCardRoot.Members[2]);

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            model.GetDeclaredSymbol(taskCardRoot));

        Assert.Equal("TaskCard", component.Name);
        Assert.Equal("Demo.Components", component.NamespaceName);
        Assert.Contains(component.Parameters, parameter => parameter.Name == "Title");
        Assert.Contains(component.States, stateSymbol => stateSymbol.Name == "count");
        Assert.IsAssignableFrom<IParamSymbol>(model.GetDeclaredSymbol(param));
        Assert.IsAssignableFrom<IStateSymbol>(model.GetDeclaredSymbol(state));
    }

    [Fact]
    public void SemanticModel_GetDeclaredSymbol_RejectsForeignExternalAkcssSyntax()
    {
        const string code = "<TextBlock />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var foreignRoot = AkcssSyntaxTree.ParseText(
            ".other { Width: 1; }",
            "Other.akcss",
            "Other.akcss").GetRoot();

        Assert.Throws<ArgumentException>(() => model.GetDeclaredSymbol(foreignRoot));
    }

    [Fact]
    public void BoundWrapping_WithDeclaredSymbolsCreatesBoundBlock()
    {
        const string code =
            "state int count = 0;\n" +
            "<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[1]);
        var componentBinder = Assert.IsType<ComponentBinder>(model.GetBinder(root));
        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        var body = new BoundDeclaration(
            root,
            componentBinder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        var wrapped = componentBinder.WrapWithDeclaredSymbolsIfAny(root, body);
        var unwrapped = markupBinder.WrapWithDeclaredSymbolsIfAny(markup, body);

        var block = Assert.IsType<BoundBlock>(wrapped);
        Assert.IsAssignableFrom<BoundStatement>(block);
        Assert.Contains(block.DeclaredSymbols, symbol => symbol is IStateSymbol { Name: "count" });
        Assert.Same(body, Assert.Single(block.Statements));
        Assert.False(block.HasErrors);
        Assert.Same(body, unwrapped);
    }

    [Fact]
    public void BoundErrorNodes_AreFailSoftAndPropagateHasErrors()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.GetBinder(state, BinderUsage.Expression);
        var diagnostic = new AkburaSemanticDiagnostic(
            state,
            ErrorCodes.ERR_SyntaxError,
            ["bad"]);
        var errorExpression = new BoundErrorExpression(
            state,
            binder,
            ImmutableArray.Create(diagnostic));
        var badExpression = new BoundBadExpression(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty,
            ImmutableArray.Create<BoundNode>(errorExpression));
        var badStatement = new BoundBadStatement(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty,
            ImmutableArray.Create<BoundNode>(badExpression));

        Assert.True(errorExpression.IsError);
        Assert.True(errorExpression.HasErrors);
        Assert.True(badExpression.IsError);
        Assert.True(badExpression.HasErrors);
        Assert.IsAssignableFrom<BoundExpression>(badExpression);
        Assert.Same(errorExpression, Assert.Single(badExpression.Children));
        Assert.IsAssignableFrom<BoundStatement>(badStatement);
        Assert.True(badStatement.IsError);
        Assert.True(badStatement.HasErrors);
        Assert.Same(badExpression, Assert.Single(badStatement.Children));
    }

    [Fact]
    public void BoundTreeVisitor_DispatchesConcreteBoundNodes()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.GetBinder(state, BinderUsage.Expression);
        var expression = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            expression,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));
        var badExpression = new BoundBadExpression(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var block = new BoundBlock(
            state,
            binder,
            ImmutableArray<AkburaSymbol>.Empty,
            ImmutableArray.Create<BoundNode>(conversion));
        var visitor = new RecordingBoundTreeVisitor();

        visitor.Visit(block);
        visitor.Visit(conversion);
        visitor.Visit(badExpression);
        visitor.Visit(expression);

        Assert.Equal(
            ["block", "conversion", "bad", "expression"],
            visitor.Visited);
    }

    [Fact]
    public void BoundNodes_ExposeBoundKind()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);
        var expression = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var csharpExpression = new BoundCSharpExpression(
            state,
            binder,
            CSharpBindingResult.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            expression,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));
        var literal = Assert.IsType<BoundLiteralExpression>(binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1")));
        var binary = Assert.IsType<BoundBinaryExpression>(binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1 + 2")));
        var call = Assert.IsType<BoundCallExpression>(binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("System.Math.Abs(1)")));
        var badExpression = new BoundBadExpression(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var errorExpression = new BoundErrorExpression(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var localDeclaration = Assert.IsType<BoundLocalDeclarationStatement>(binder.BindStatement(
            state,
            CSharpSyntaxFactory.ParseStatement("int value = 1;")));
        var declaration = new BoundDeclaration(
            state,
            binder,
            model.GetSymbolInfo(state),
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var block = new BoundBlock(
            state,
            binder,
            ImmutableArray<AkburaSymbol>.Empty,
            ImmutableArray.Create<BoundNode>(expression));
        var badStatement = new BoundBadStatement(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        const string markupCode =
            "using Avalonia.Controls;\n" +
            "<TextBlock Text=\"Hello\" />";
        var markupTree = AkburaSyntaxTree.ParseText(markupCode, "Markup.akbura");
        var markupModel = CreateCompilation(markupTree).GetSemanticModel(markupTree);
        var markupRoot = Assert.IsType<MarkupRootSyntax>(markupTree.GetRoot().Members[1]);
        var markupAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            markupRoot.Element.StartTag!.Attributes[0]);
        var boundMarkupRoot = markupModel.BindingSession.BindSemanticSyntax(markupRoot);
        var boundMarkupComponent = markupModel.BindingSession.BindSemanticSyntax(markupRoot.Element);
        var markupSetter = markupModel.BindingSession.BindOperationSyntax(markupAttribute);

        const string akcssCode = "@akcss { Button.card { Background: White; } }";
        var akcssTree = AkburaSyntaxTree.ParseText(akcssCode, "Akcss.akbura");
        var akcssModel = CreateCompilation(akcssTree).GetSemanticModel(akcssTree);
        var akcssBlock = Assert.IsType<InlineAkcssBlockSyntax>(akcssTree.GetRoot().Members[0]);
        var akcssRule = Assert.IsType<AkcssStyleRuleSyntax>(akcssBlock.Members[0]);
        var akcssAssignment = Assert.IsType<AkcssAssignmentSyntax>(akcssRule.Members[0]);
        var boundAkcssModule = akcssModel.BindingSession.BindSemanticSyntax(akcssBlock);
        var boundAkcssStyle = akcssModel.BindingSession.BindSemanticSyntax(akcssRule);
        var akcssSetter = akcssModel.BindingSession.BindOperationSyntax(akcssAssignment);

        Assert.Equal(BoundKind.Expression, expression.Kind);
        Assert.Equal(BoundKind.CSharpExpression, csharpExpression.Kind);
        Assert.Equal(BoundKind.ConversionExpression, conversion.Kind);
        Assert.Equal(BoundKind.LiteralExpression, literal.Kind);
        Assert.Equal(BoundKind.BinaryExpression, binary.Kind);
        Assert.Equal(BoundKind.CallExpression, call.Kind);
        Assert.Equal(BoundKind.BadExpression, badExpression.Kind);
        Assert.Equal(BoundKind.ErrorExpression, errorExpression.Kind);
        Assert.Equal(BoundKind.LocalDeclarationStatement, localDeclaration.Kind);
        Assert.Equal(BoundKind.Declaration, declaration.Kind);
        Assert.Equal(BoundKind.MarkupRoot, boundMarkupRoot.Kind);
        Assert.Equal(BoundKind.MarkupComponent, boundMarkupComponent.Kind);
        Assert.Equal(BoundKind.MarkupPropertySetter, markupSetter.Kind);
        Assert.Equal(BoundKind.AkcssModule, boundAkcssModule.Kind);
        Assert.Equal(BoundKind.AkcssStyle, boundAkcssStyle.Kind);
        Assert.Equal(BoundKind.AkcssPropertySetter, akcssSetter.Kind);
        Assert.Equal(BoundKind.Block, block.Kind);
        Assert.Equal(BoundKind.BadStatement, badStatement.Kind);
    }

    [Fact]
    public void BindingSession_BindsSemanticSyntaxCoverageTree()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;
            param string Title = "Dashboard";

            useEffect(count) {
                <TextBlock Text={Title}/>
            }

            @akcss {
                Button.card {
                    Background: White;
                    @if(true) {
                        Opacity: 1;
                    }
                    @apply card;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <StackPanel>
                <TextBlock Text="Hello"/>
            </StackPanel>
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Dashboard.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();

        var component = Assert.IsType<BoundComponentDeclaration>(
            model.BindingSession.BindSemanticSyntax(root));
        Assert.Contains(component.Children, child => child.Kind == BoundKind.StateDeclaration);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.ParamDeclaration);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.UseEffectDeclaration);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.AkcssModule);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.MarkupRoot);

        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[1]);
        var boundState = Assert.IsType<BoundStateDeclaration>(
            model.BindingSession.BindSemanticSyntax(state));
        Assert.Single(boundState.Children);
        Assert.IsType<BoundStateInitializer>(boundState.Children[0]);

        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[2]);
        var boundParam = Assert.IsType<BoundParamDeclaration>(
            model.BindingSession.BindSemanticSyntax(param));
        Assert.Single(boundParam.Children);
        Assert.IsType<BoundParamDefaultValue>(boundParam.Children[0]);

        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[3]);
        var boundUseEffect = Assert.IsType<BoundUseEffectDeclaration>(
            model.BindingSession.BindSemanticSyntax(useEffect));
        Assert.Contains(boundUseEffect.Children, child => child.Kind == BoundKind.UseEffectDependency);
        var body = Assert.IsType<BoundUseEffectBody>(
            Assert.Single(boundUseEffect.Children, child => child.Kind == BoundKind.UseEffectBody));
        Assert.Contains(body.Children, child => child.Kind == BoundKind.MarkupRoot);

        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[4]);
        var boundModule = Assert.IsType<BoundAkcssModule>(
            model.BindingSession.BindSemanticSyntax(akcss));
        Assert.Contains(boundModule.Children, child => child.Kind == BoundKind.AkcssStyle);
        Assert.Contains(boundModule.Children, child => child.Kind == BoundKind.AkcssUtility);

        var style = Assert.IsType<BoundAkcssStyle>(
            Assert.Single(boundModule.Children, child => child.Kind == BoundKind.AkcssStyle));
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssPropertySetter);
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssIf);
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssApply);

        var utility = Assert.IsType<BoundAkcssUtility>(
            Assert.Single(boundModule.Children, child => child.Kind == BoundKind.AkcssUtility));
        Assert.Contains(utility.Children, child => child.Kind == BoundKind.AkcssPropertySetter);

        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[5]);
        var boundMarkupRoot = Assert.IsType<BoundMarkupRoot>(
            model.BindingSession.BindSemanticSyntax(markup));
        var boundMarkupComponent = Assert.IsType<BoundMarkupComponent>(
            Assert.Single(boundMarkupRoot.Children));
        Assert.Contains(boundMarkupComponent.Children, child => child.Kind == BoundKind.MarkupContent);
        var nestedContent = Assert.IsType<BoundMarkupContent>(
            Assert.Single(boundMarkupComponent.Children, child => child.Kind == BoundKind.MarkupContent));
        Assert.Contains(nestedContent.Children, child => child.Kind == BoundKind.MarkupComponent);
    }

    [Fact]
    public void SemanticApiAudit_CoversSymbolBoundAndOperationSyntax()
    {
        const string dashboardCode =
            """
            using Avalonia.Controls;
            using Demo.Components;

            namespace Demo.Pages;

            @akcss {
                Button.card {
                    Background: White;
                    @if(true) {
                        Opacity: 1;
                    }
                    @apply card;
                }

                Button.managed {
                    @intercept global::Demo.Styles.DashboardStyle;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }

                    .hidden {
                        IsVisible: false;
                    }
                }
            }

            inject int service;
            param string Title = "Dashboard";
            state int count = 0;
            command int Refresh(int id);

            useEffect(count, Refresh.IsExecuting) {
                <TextBlock Text={Title}/>
            }

            <StackPanel>
                <TextBlock Text={Title} w-30 {count > 0}:hidden/>
                <Button Click={(sender, args) => { count++; }} Content="Run"/>
                <TaskCard Toggle={id => id + count}/>
            </StackPanel>
            """;
        const string taskCardCode =
            """
            namespace Demo.Components;

            command int Toggle(int id);

            <TextBlock Text="Task"/>
            """;
        const string csharpCode =
            """
            namespace Akbura.Akcss
            {
                public abstract class AkcssStyle { }

                public abstract class AkcssClass : AkcssStyle
                {
                    public abstract void Update(object control);
                }
            }

            namespace Demo.Styles
            {
                public sealed class DashboardStyle : Akbura.Akcss.AkcssClass
                {
                    public override void Update(object control) { }
                }
            }
            """;

        var dashboardTree = AkburaSyntaxTree.ParseText(dashboardCode, "Pages/Dashboard.akbura");
        var taskCardTree = AkburaSyntaxTree.ParseText(taskCardCode, "Components/TaskCard.akbura");
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(csharpCode));
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [dashboardTree, taskCardTree],
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var model = compilation.GetSemanticModel(dashboardTree);
        var root = dashboardTree.GetRoot();
        var inlineAkcss = root.Members.OfType<InlineAkcssBlockSyntax>().Single();
        var cardStyle = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().First();
        var managedStyle = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().Skip(1).Single();
        var utilities = inlineAkcss.Members.OfType<AkcssUtilitiesSectionSyntax>().Single();
        var markup = root.Members.OfType<MarkupRootSyntax>().Single();
        var stackPanel = markup.Element;
        var children = ChildElements(stackPanel).ToArray();
        var textBlock = children.Single(element => ElementName(element) == "TextBlock");
        var button = children.Single(element => ElementName(element) == "Button");
        var taskCard = children.Single(element => ElementName(element) == "TaskCard");

        AssertSymbol<IAkburaComponentSymbol>(root);
        AssertSemanticBound<BoundComponentDeclaration>(root);
        AssertSymbol<IInjectSymbol>(root.Members.OfType<InjectDeclarationSyntax>().Single());
        AssertSemanticBound<BoundInjectDeclaration>(root.Members.OfType<InjectDeclarationSyntax>().Single());
        AssertSymbol<IParamSymbol>(root.Members.OfType<ParamDeclarationSyntax>().Single());
        AssertSemanticBound<BoundParamDeclaration>(root.Members.OfType<ParamDeclarationSyntax>().Single());
        AssertSymbol<IStateSymbol>(root.Members.OfType<StateDeclarationSyntax>().Single());
        AssertSemanticBound<BoundStateDeclaration>(root.Members.OfType<StateDeclarationSyntax>().Single());
        AssertSymbol<ICommandSymbol>(root.Members.OfType<CommandDeclarationSyntax>().Single());
        AssertSemanticBound<BoundCommandDeclaration>(root.Members.OfType<CommandDeclarationSyntax>().Single());
        AssertSymbol<IUseEffectSymbol>(root.Members.OfType<UseEffectDeclarationSyntax>().Single());
        AssertSemanticBound<BoundUseEffectDeclaration>(root.Members.OfType<UseEffectDeclarationSyntax>().Single());

        AssertSymbol<IAkcssModuleSymbol>(inlineAkcss);
        AssertSemanticBound<BoundAkcssModule>(inlineAkcss);
        AssertSymbol<IAkcssSymbol>(cardStyle);
        AssertSemanticBound<BoundAkcssStyle>(cardStyle);
        AssertSymbol<ITailwindUtilitySymbol>(utilities.Utilities[0]);
        AssertSemanticBound<BoundAkcssUtility>(utilities.Utilities[0]);

        AssertSymbol<IMarkupComponentSymbol>(stackPanel);
        AssertSemanticBound<BoundMarkupRoot>(markup);
        AssertSemanticBound<BoundMarkupComponent>(stackPanel);
        AssertSymbol<IMarkupComponentSymbol>(textBlock);
        AssertSymbol<IMarkupComponentSymbol>(button);
        AssertSymbol<IMarkupComponentSymbol>(taskCard);

        var textAttribute = Attribute(textBlock, "Text");
        var widthAttribute = Attribute(textBlock, "w");
        var hiddenAttribute = Attribute(textBlock, "hidden");
        var clickAttribute = Attribute(button, "Click");
        var contentAttribute = Attribute(button, "Content");
        var toggleAttribute = Attribute(taskCard, "Toggle");
        var backgroundAssignment = cardStyle.Members.OfType<AkcssAssignmentSyntax>().Single();
        var ifDirective = cardStyle.Members.OfType<AkcssIfDirectiveSyntax>().Single();
        var applyDirective = cardStyle.Members.OfType<AkcssApplyDirectiveSyntax>().Single();
        var interceptDirective = managedStyle.Members.OfType<AkcssInterceptDirectiveSyntax>().Single();

        AssertSymbol<AkburaPropertySymbol>(textAttribute);
        AssertOperation<BoundMarkupPropertySetter, IMarkupPropertySetterOperation>(textAttribute);
        AssertOperation<BoundTailwindUtilityAttribute, ITailwindUtilityAttributeOperation>(widthAttribute);
        AssertOperation<BoundTailwindUtilityAttribute, ITailwindUtilityAttributeOperation>(hiddenAttribute);
        AssertSymbol<IRoutedEventSymbol>(clickAttribute);
        AssertOperation<BoundMarkupRoutedEventBinding, IMarkupRoutedEventBindingOperation>(clickAttribute);
        AssertSymbol<AkburaPropertySymbol>(contentAttribute);
        AssertOperation<BoundMarkupPropertySetter, IMarkupPropertySetterOperation>(contentAttribute);
        var toggleProperty = AssertSymbol<AkburaPropertySymbol>(toggleAttribute);
        Assert.NotNull(toggleProperty.Command);
        AssertOperation<BoundMarkupCommandBinding, IMarkupCommandBindingOperation>(toggleAttribute);

        AssertOperation<BoundAkcssPropertySetter, IAkcssPropertySetterOperation>(backgroundAssignment);
        AssertOperation<BoundAkcssIf, IAkcssIfOperation>(ifDirective);
        AssertOperation<BoundAkcssApply, IAkcssApplyOperation>(applyDirective);
        AssertOperation<BoundAkcssIntercept, IAkcssInterceptOperation>(interceptDirective);

        TSymbol AssertSymbol<TSymbol>(AkburaSyntax syntax)
            where TSymbol : class, AkburaSymbol
        {
            var symbol = Assert.IsAssignableFrom<TSymbol>(model.GetSymbolInfo(syntax).Symbol);
            Assert.Same(symbol, model.GetSymbolInfo(syntax).Symbol);
            return symbol;
        }

        void AssertSemanticBound<TBound>(AkburaSyntax syntax)
            where TBound : BoundNode
        {
            Assert.IsType<TBound>(model.BindingSession.BindSemanticSyntax(syntax));
            Assert.Same(
                model.BindingSession.BindSemanticSyntax(syntax),
                model.BindingSession.BindSemanticSyntax(syntax));
        }

        void AssertOperation<TBound, TOperation>(AkburaSyntax syntax)
            where TBound : BoundNode
            where TOperation : class, AkburaOperation
        {
            Assert.IsType<TBound>(model.BindingSession.BindOperationSyntax(syntax));
            var operation = Assert.IsAssignableFrom<TOperation>(model.GetOperation(syntax));
            Assert.Same(operation, model.GetOperation(syntax));
            Assert.Same(syntax, operation.Syntax);
        }

        static IEnumerable<MarkupElementSyntax> ChildElements(MarkupElementSyntax element)
        {
            foreach (var content in element.Body)
            {
                if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
                {
                    yield return ((MarkupElementContentSyntax)content).Element;
                }
            }
        }

        static string ElementName(MarkupElementSyntax element)
        {
            return element.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
        }

        static MarkupAttributeSyntax Attribute(MarkupElementSyntax element, string name)
        {
            return element.StartTag!.Attributes.Single(attribute => AttributeName(attribute) == name);
        }

        static string AttributeName(MarkupAttributeSyntax attribute)
        {
            return attribute.Kind switch
            {
                AkburaSyntaxKind.MarkupPlainAttributeSyntax =>
                    ((MarkupPlainAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax =>
                    ((MarkupPrefixedAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.TailwindFlagAttributeSyntax =>
                    ((TailwindFlagAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                    ((TailwindFullAttributeSyntax)attribute).Name.Identifier.ValueText,
                _ => string.Empty,
            };
        }
    }

    [Fact]
    public void BoundTreeWalker_WalksChildrenWithStackGuard()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.GetBinder(state, BinderUsage.Expression);
        var expression = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            expression,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));
        var block = new BoundBlock(
            state,
            binder,
            ImmutableArray<AkburaSymbol>.Empty,
            ImmutableArray.Create<BoundNode>(conversion));
        var walker = new RecordingBoundTreeWalker();

        walker.Visit(block);

        Assert.Equal(
            [nameof(BoundBlock), nameof(BoundConversionExpression), nameof(BoundExpression)],
            walker.Visited);
    }

    [Fact]
    public void BoundTreeRewriter_RewritesKnownChildrenAndPreservesNoOpIdentity()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.GetBinder(state, BinderUsage.Expression);
        var oldOperand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var newOperand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            oldOperand,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));
        var badExpression = new BoundBadExpression(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty,
            ImmutableArray.Create<BoundNode>(oldOperand));

        Assert.Same(conversion, new BoundTreeRewriter().Visit(conversion));
        Assert.Same(badExpression, new BoundTreeRewriter().Visit(badExpression));

        var rewritten = Assert.IsType<BoundConversionExpression>(
            new ReplacingBoundTreeRewriter(oldOperand, newOperand).Visit(conversion));
        var rewrittenBadExpression = Assert.IsType<BoundBadExpression>(
            new ReplacingBoundTreeRewriter(oldOperand, newOperand).Visit(badExpression));

        Assert.NotSame(conversion, rewritten);
        Assert.Same(newOperand, rewritten.Operand);
        Assert.Equal(conversion.Conversion.Kind, rewritten.Conversion.Kind);
        Assert.NotSame(badExpression, rewrittenBadExpression);
        Assert.Same(newOperand, Assert.Single(rewrittenBadExpression.Children));
    }

    [Fact]
    public void BoundNodeUpdate_PreservesNoOpIdentityAndCreatesChangedNode()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.GetBinder(state, BinderUsage.Expression);
        var oldOperand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var newOperand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            oldOperand,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));
        var block = new BoundBlock(
            state,
            binder,
            ImmutableArray<AkburaSymbol>.Empty,
            ImmutableArray.Create<BoundNode>(conversion));

        Assert.Same(conversion, conversion.Update(conversion.Operand, conversion.Conversion));
        Assert.Same(block, block.Update(block.DeclaredSymbols, block.Statements));

        var changedConversion = Assert.IsType<BoundConversionExpression>(
            conversion.Update(newOperand, conversion.Conversion));
        var changedBlock = block.Update(
            block.DeclaredSymbols,
            ImmutableArray.Create<BoundNode>(changedConversion));

        Assert.NotSame(conversion, changedConversion);
        Assert.Same(newOperand, changedConversion.Operand);
        Assert.NotSame(block, changedBlock);
        Assert.Same(changedConversion, Assert.Single(changedBlock.Statements));
    }

    [Fact]
    public void BoundTreeRewriter_VisitsSymbols()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var stateSymbol = Assert.IsAssignableFrom<IStateSymbol>(model.GetSymbolInfo(state).Symbol);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);
        var call = Assert.IsType<BoundCallExpression>(binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("System.Math.Abs(1)")));
        var localDeclaration = Assert.IsType<BoundLocalDeclarationStatement>(binder.BindStatement(
            state,
            CSharpSyntaxFactory.ParseStatement("int value = 1;")));
        var block = new BoundBlock(
            state,
            binder,
            ImmutableArray.Create<AkburaSymbol>(stateSymbol),
            ImmutableArray.Create<BoundNode>(call, localDeclaration));
        var rewriter = new RecordingSymbolBoundTreeRewriter();

        var rewritten = rewriter.Visit(block);

        Assert.Same(block, rewritten);
        Assert.Equal(1, rewriter.StateSymbolCount);
        Assert.True(rewriter.MethodSymbolCount >= 1);
        Assert.Equal(1, rewriter.LocalSymbolCount);
    }

    [Fact]
    public void BoundTreeRewriter_VisitsOperationAndBindingFacts()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                .card { Width: 1; }
            }

            state int count = 0;

            <StackPanel>
                <TextBlock Text={count.ToString()} />
                <Button Click={() => count++} />
            </StackPanel>
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[1]);
        var akcssRule = Assert.IsType<AkcssStyleRuleSyntax>(akcss.Members[0]);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(akcssRule.Members[0]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[3]);
        var textBlockContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[0]);
        var buttonContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[1]);
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            textBlockContent.Element.StartTag!.Attributes[0]);
        var clickAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            buttonContent.Element.StartTag!.Attributes[0]);
        var stateBoundNode = Assert.IsType<BoundStateDeclaration>(
            model.BindingSession.BindSemanticSyntax(state));
        var stateInitializer = Assert.IsType<BoundStateInitializer>(
            Assert.Single(stateBoundNode.Children));
        var textSetter = Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(textAttribute));
        var clickBinding = Assert.IsType<BoundMarkupRoutedEventBinding>(
            model.BindingSession.BindOperationSyntax(clickAttribute));
        var akcssSetter = Assert.IsType<BoundAkcssPropertySetter>(
            model.BindingSession.BindOperationSyntax(assignment));
        var rewriter = new RecordingSymbolBoundTreeRewriter();

        Assert.Same(stateInitializer, rewriter.Visit(stateInitializer));
        Assert.Same(textSetter, rewriter.Visit(textSetter));
        Assert.Same(clickBinding, rewriter.Visit(clickBinding));
        Assert.Same(akcssSetter, rewriter.Visit(akcssSetter));

        Assert.True(rewriter.PropertySymbolCount >= 2);
        Assert.True(rewriter.RoutedEventSymbolCount >= 1);
        Assert.True(rewriter.CSharpSymbolDefinitionCount >= 2);
        Assert.True(rewriter.CSharpOperationDefinitionCount >= 4);
    }

    [Fact]
    public void BindingSession_CachesBinderChainsBySyntaxAndUsage()
    {
        const string code = "state int count = 0;\n<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);

        var first = model.GetBinder(state, BinderUsage.Expression);
        var second = model.GetBinder(state, BinderUsage.Expression);
        var differentUsage = model.GetBinder(state, BinderUsage.Type);

        Assert.Same(first, second);
        Assert.NotSame(first, differentUsage);
        Assert.True(model.BindingSession.CachedBinderCount >= 2);

        var fields = typeof(BindingSession).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.DoesNotContain(fields, field =>
            field.FieldType.FullName?.Contains(nameof(BoundNode), StringComparison.Ordinal) == true ||
            field.FieldType.FullName?.Contains(nameof(AkburaOperation), StringComparison.Ordinal) == true);
    }

    [Fact]
    public void BindingSession_ConcurrentCacheIsBestEffortForParallelBinderRequests()
    {
        const string code = "state int count = 0;\n<TextBlock Text=\"Hello\" />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binders = new BinderType[64];

        Parallel.For(0, binders.Length, index =>
        {
            binders[index] = model.GetBinder(state, BinderUsage.Expression);
        });

        Assert.All(binders, binder => Assert.IsType<ComponentBinder>(binder));

        var cached = model.GetBinder(state, BinderUsage.Expression);
        Assert.Same(cached, model.GetBinder(state, BinderUsage.Expression));
    }

    [Fact]
    public void BinderFactory_UsesSemanticModelBindingSessionCache()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var factory = new BinderFactory(model);

        var fromModel = model.GetBinder(state);
        var firstFromFactory = factory.GetBinder(state);
        var secondFromFactory = factory.GetBinder(state);

        Assert.Same(fromModel, firstFromFactory);
        Assert.Same(firstFromFactory, secondFromFactory);
        Assert.True(model.BindingSession.CachedBinderCount >= 1);
    }

    [Fact]
    public void BinderFactory_UsesPooledVisitor()
    {
        var poolField = typeof(BinderFactory).GetField(
            "s_binderFactoryVisitorPool",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(poolField);
        Assert.Same(
            typeof(ObjectPool<BinderFactory.BinderFactoryVisitor>),
            poolField.FieldType);
    }

    [Fact]
    public void BinderFactory_UsesFixedSizeConcurrentCache()
    {
        var cacheField = typeof(BinderFactory).GetField(
            "_binderCache",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(cacheField);
        Assert.Same(
            typeof(ConcurrentCache<BinderCacheKey, BinderType>),
            cacheField.FieldType);
    }

    [Fact]
    public void ExecutableCodeBinder_UsesLazySmallDictionaryBinderMap()
    {
        var cacheField = typeof(BindingSession).GetField(
            "_executableBinderCache",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        Assert.Same(
            typeof(ConcurrentCache<BinderCacheKey, ExecutableCodeBinder>),
            cacheField.FieldType);

        var mapField = typeof(ExecutableCodeBinder).GetField(
            "_lazyBinderMap",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapField);
        Assert.Same(
            typeof(SmallDictionary<AkburaSyntax, BinderType>),
            mapField.FieldType);
    }

    [Fact]
    public void ExecutableCodeBinder_ReturnsCachedNestedBinderFromMap()
    {
        const string code =
            "state int total = 0;\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    Console.WriteLine(count);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var rootDeclaration = Assert.Single(model.Compilation.DeclarationTable.Components);
        var root = tree.GetRoot();
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(root.Members[1]);
        var block = ifStatement.Body!;
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[1]);
        var statementDeclaration = Assert.Single(
            rootDeclaration.Children,
            declaration => ReferenceEquals(declaration.Syntax.Green, ifStatement.Green));
        var executableRootPath = ImmutableArray.Create(rootDeclaration, statementDeclaration);
        var next = model.GetBinder(root);
        var executableBinder = new ExecutableCodeBinder(
            model.BindingSession,
            executableRootPath,
            next,
            BinderUsage.Statement);

        var first = executableBinder.GetBinder(writeLine);
        var second = executableBinder.GetBinder(writeLine);

        var blockBinder = Assert.IsType<BlockBinder>(first);
        Assert.Same(first, second);
        Assert.Same(block, blockBinder.ScopeDesignator);

        var mapField = typeof(ExecutableCodeBinder).GetField(
            "_lazyBinderMap",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapField);
        Assert.NotNull(mapField.GetValue(executableBinder));
    }

    [Fact]
    public void BinderFactoryVisitor_BuildsNestedMarkupBinderChain()
    {
        const string code = "<StackPanel><Button /></StackPanel>";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members[0]);
        var childContent = Assert.IsType<MarkupElementContentSyntax>(markupRoot.Element.Body[0]);
        var childElement = childContent.Element;

        var childBinder = Assert.IsType<MarkupBinder>(model.GetBinder(childElement, BinderUsage.Markup));

        Assert.Same(childElement, childBinder.ScopeDesignator);
        Assert.True(childBinder.Flags.HasFlag(AkburaBinderFlags.InMarkup));

        var parentMarkupBinder = Assert.IsType<MarkupBinder>(childBinder.NextRequired);
        Assert.Same(markupRoot, parentMarkupBinder.ScopeDesignator);

        Assert.IsType<ComponentBinder>(parentMarkupBinder.NextRequired);
        Assert.IsType<CompilationBinder>(parentMarkupBinder.NextRequired.NextRequired);
        Assert.True(model.BindingSession.CachedBinderCount >= 1);
    }

    [Fact]
    public void BinderFactory_GetBinderByPosition_BuildsNestedMarkupBinderChain()
    {
        const string code = "<StackPanel><Button /></StackPanel>";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members[0]);
        var childContent = Assert.IsType<MarkupElementContentSyntax>(markupRoot.Element.Body[0]);
        var childElement = childContent.Element;
        var factory = new BinderFactory(model);

        var childBinder = Assert.IsType<MarkupBinder>(factory.GetBinder(
            root,
            childElement.Position,
            BinderUsage.Markup));
        var cachedBinder = factory.GetBinder(
            root,
            childElement.Position,
            BinderUsage.Markup);

        Assert.Same(childBinder, cachedBinder);
        Assert.Same(childElement, childBinder.ScopeDesignator);
        Assert.True(childBinder.Flags.HasFlag(AkburaBinderFlags.InMarkup));

        var parentMarkupBinder = Assert.IsType<MarkupBinder>(childBinder.NextRequired);
        Assert.Same(markupRoot, parentMarkupBinder.ScopeDesignator);
        Assert.IsType<ComponentBinder>(parentMarkupBinder.NextRequired);
        Assert.IsType<CompilationBinder>(parentMarkupBinder.NextRequired.NextRequired);
        Assert.True(model.BindingSession.CachedBinderCount >= 1);
    }

    [Fact]
    public void BinderFactoryVisitor_BuildsCSharpBlockBinderChain()
    {
        const string code =
            "state int total = 0;\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    Console.WriteLine(count);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(tree.GetRoot().Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[1]);

        var binder = Assert.IsType<BlockBinder>(model.GetBinder(writeLine, BinderUsage.Statement));

        Assert.Same(block, binder.ScopeDesignator);
        Assert.True(binder.Flags.HasFlag(AkburaBinderFlags.InCSharpBlock));
        Assert.IsType<ComponentBinder>(binder.NextRequired);
        Assert.IsType<CompilationBinder>(binder.NextRequired.NextRequired);
        Assert.True(model.BindingSession.CachedBinderCount >= 1);
    }

    [Fact]
    public void BinderFactory_GetBinderByPosition_BuildsCSharpBlockBinderChain()
    {
        const string code =
            "state int total = 0;\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    Console.WriteLine(count);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(root.Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[1]);

        var binder = Assert.IsType<BlockBinder>(model.GetBinder(
            root,
            writeLine.Position,
            BinderUsage.Statement));
        var cachedBinder = model.GetBinder(
            root,
            writeLine.Position,
            BinderUsage.Statement);

        Assert.Same(binder, cachedBinder);
        Assert.Same(block, binder.ScopeDesignator);
        Assert.True(binder.Flags.HasFlag(AkburaBinderFlags.InCSharpBlock));
        Assert.IsType<ComponentBinder>(binder.NextRequired);
        Assert.IsType<CompilationBinder>(binder.NextRequired.NextRequired);
        Assert.True(model.BindingSession.CachedBinderCount >= 1);
    }

    [Fact]
    public void BinderFactory_GetBinderByPosition_RejectsPositionOutsideSyntax()
    {
        const string code = "<Button />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();

        Assert.Throws<ArgumentOutOfRangeException>(() => model.GetBinder(
            root,
            root.EndPosition + 1,
            BinderUsage.Markup));
    }

    [Fact]
    public void ComponentBinder_DeclaredSymbolsAreLazyAndStable()
    {
        const string code =
            "inject ILogger<Counter> logger;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int userId);";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var binder = Assert.IsType<ComponentBinder>(model.GetBinder(root));
        var lazyField = typeof(ComponentBinder).GetField(
            "_lazyDeclaredSymbols",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(lazyField);
        var before = Assert.IsType<ImmutableArray<AkburaSymbol>>(lazyField.GetValue(binder));
        Assert.True(before.IsDefault);

        var first = binder.GetDeclaredSymbolsForScope(root);
        var after = Assert.IsType<ImmutableArray<AkburaSymbol>>(lazyField.GetValue(binder));
        var second = binder.GetDeclaredSymbolsForScope(root);

        Assert.False(after.IsDefault);
        Assert.Equal(4, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void BlockBinder_DeclaredSymbolsAreLazyAndStable()
    {
        const string code =
            "state int total = 0;\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    int other = 2;\n" +
            "    Console.WriteLine(count + other);\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(tree.GetRoot().Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var writeLine = Assert.IsType<CSharpStatementSyntax>(block.Tokens[2]);
        var binder = Assert.IsType<BlockBinder>(model.GetBinder(writeLine, BinderUsage.Statement));
        var lazyField = typeof(BlockBinder).GetField(
            "_lazyDeclaredSymbols",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(lazyField);
        var before = Assert.IsType<ImmutableArray<AkburaSymbol>>(lazyField.GetValue(binder));
        Assert.True(before.IsDefault);

        var first = binder.GetDeclaredSymbolsForScope(block);
        var after = Assert.IsType<ImmutableArray<AkburaSymbol>>(lazyField.GetValue(binder));
        var second = binder.GetDeclaredSymbolsForScope(block);

        Assert.False(after.IsDefault);
        Assert.Equal(["count", "other"], first.Select(symbol => symbol.Name));
        Assert.Equal(first, second);
    }

    [Fact]
    public void CSharpProbeBinder_BindsProbeExpressionsAndDiagnostics()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);

        var validExpression = CSharpSyntaxFactory.ParseExpression("1 + 2");
        var validBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(validExpression),
            isBindingPath: true);

        Assert.Equal("Int32", validBinding.TypeSymbol?.Name);
        Assert.False(validBinding.OperationDefinition.IsDefault);
        Assert.Empty(validBinding.Diagnostics);

        var invalidExpression = CSharpSyntaxFactory.ParseExpression("NotExisting.Value");
        var invalidBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(invalidExpression),
            isBindingPath: true);

        Assert.Null(invalidBinding.TypeSymbol);
        Assert.NotEmpty(invalidBinding.Diagnostics);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionUsesComponentAndBlockScope()
    {
        const string code =
            "state int total = 0;\n" +
            "\n" +
            "if(total > 0)\n" +
            "{\n" +
            "    int count = 1;\n" +
            "    <TextBlock Text={count + total} />\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(tree.GetRoot().Members[1]);
        Assert.NotNull(ifStatement.Body);
        var block = ifStatement.Body!;
        var markup = Assert.IsType<MarkupRootSyntax>(block.Tokens[1]);

        var bound = model.BindingSession.BindExpression(
            markup,
            CSharpSyntaxFactory.ParseExpression("count + total"),
            usage: BinderUsage.Markup);

        var binary = Assert.IsType<BoundBinaryExpression>(bound);
        Assert.Equal("Int32", binary.Type?.Name);
        Assert.False(binary.HasErrors);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionCreatesCommandFacadeFromScope()
    {
        const string code =
            "command int CustomClick(int value);\n" +
            "\n" +
            "<TextBlock Tag={CustomClick.IsExecuting} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var markup = Assert.IsType<MarkupRootSyntax>(tree.GetRoot().Members[1]);

        var bound = model.BindingSession.BindExpression(
            markup,
            CSharpSyntaxFactory.ParseExpression("CustomClick.IsExecuting"),
            usage: BinderUsage.Markup);

        Assert.Equal("IObservable", bound.Type?.Name);
        Assert.False(bound.HasErrors);
    }

    [Fact]
    public void Binder_ConversionsAreLazyAndStable()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var field = typeof(BinderType).GetField(
            "_lazyConversions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Null(field!.GetValue(binder));

        var first = binder.Conversions;
        var second = binder.Conversions;

        Assert.Same(first, second);
        Assert.Same(first, field.GetValue(binder));
    }

    [Fact]
    public void Binder_OverloadResolutionIsLazyAndStable()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var field = typeof(BinderType).GetField(
            "_lazyOverloadResolution",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Null(field!.GetValue(binder));

        var first = binder.OverloadResolution;
        var second = binder.OverloadResolution;

        Assert.Same(first, second);
        Assert.Same(first, field.GetValue(binder));
    }

    [Fact]
    public void OverloadResolver_SelectsBestMethodUsingImplicitConversions()
    {
        const string code = "state int count = 0;";
        const string csharpCode =
            """
            namespace Demo;

            public sealed class OverloadTarget
            {
                public void Pick(int value) { }
                public void Pick(double value) { }
                public void Widen(double value) { }
                public void Widen(string value) { }
                public void Ambiguous(long value) { }
                public void Ambiguous(double value) { }
                public void None(int value) { }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(syntaxTree);
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = new AkburaCompilation(
                csharpCompilation,
                [tree],
                rootNamespace: "Demo",
                projectDirectory: Environment.CurrentDirectory)
            .GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var target = Assert.IsAssignableFrom<INamedTypeSymbol>(
            csharpCompilation.GetTypeByMetadataName("Demo.OverloadTarget"));
        var intType = csharpCompilation.GetSpecialType(SpecialType.System_Int32);
        var stringType = csharpCompilation.GetSpecialType(SpecialType.System_String);

        var exact = binder.OverloadResolution.ResolveMethodGroup(
            target.GetMembers("Pick").OfType<IMethodSymbol>().ToImmutableArray(),
            ImmutableArray.Create<ITypeSymbol?>(intType));
        var implicitOnly = binder.OverloadResolution.ResolveMethodGroup(
            target.GetMembers("Widen").OfType<IMethodSymbol>().ToImmutableArray(),
            ImmutableArray.Create<ITypeSymbol?>(intType));
        var ambiguous = binder.OverloadResolution.ResolveMethodGroup(
            target.GetMembers("Ambiguous").OfType<IMethodSymbol>().ToImmutableArray(),
            ImmutableArray.Create<ITypeSymbol?>(intType));
        var notFound = binder.OverloadResolution.ResolveMethodGroup(
            target.GetMembers("None").OfType<IMethodSymbol>().ToImmutableArray(),
            ImmutableArray.Create<ITypeSymbol?>(stringType));

        Assert.True(exact.IsSuccessful);
        Assert.Equal("Int32", exact.SelectedMethod?.Parameters[0].Type.Name);
        Assert.True(implicitOnly.IsSuccessful);
        Assert.Equal("Double", implicitOnly.SelectedMethod?.Parameters[0].Type.Name);
        Assert.False(ambiguous.IsSuccessful);
        Assert.Equal(AkburaCandidateReason.Ambiguous, ambiguous.CandidateReason);
        Assert.Equal(2, ambiguous.CandidateMethods.Length);
        Assert.False(notFound.IsSuccessful);
        Assert.Equal(AkburaCandidateReason.NotFound, notFound.CandidateReason);
    }

    [Fact]
    public void CSharpProbeBinder_ClassifiesConversionsThroughRoslyn()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var intBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(CSharpSyntaxFactory.ParseExpression("1")),
            isBindingPath: true);
        var doubleBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(CSharpSyntaxFactory.ParseExpression("1.0")),
            isBindingPath: true);
        var stringBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(CSharpSyntaxFactory.ParseExpression("\"text\"")),
            isBindingPath: true);
        var intType = intBinding.TypeSymbol;
        var doubleType = doubleBinding.TypeSymbol;
        var stringType = stringBinding.TypeSymbol;

        Assert.NotNull(intType);
        Assert.NotNull(doubleType);
        Assert.NotNull(stringType);

        var identity = binder.ClassifyConversion(intType, intType);
        var implicitNumeric = binder.ClassifyConversion(intType, doubleType);
        var explicitNumeric = binder.ClassifyConversion(doubleType, intType);
        var none = binder.ClassifyConversion(stringType, intType);

        Assert.Equal(AkburaConversionKind.Identity, identity.Kind);
        Assert.True(identity.IsImplicit);
        Assert.Equal(AkburaConversionKind.Implicit, implicitNumeric.Kind);
        Assert.True(implicitNumeric.IsImplicit);
        Assert.Equal(AkburaConversionKind.Explicit, explicitNumeric.Kind);
        Assert.True(explicitNumeric.IsExplicit);
        Assert.Equal(AkburaConversionKind.None, none.Kind);
        Assert.False(none.Exists);
    }

    [Fact]
    public void AkburaConversions_PreserveRoslynConversionDetails()
    {
        const string code = "state int count = 0;";
        const string csharpCode =
            """
            namespace Demo;

            public enum Status
            {
                None,
                Active
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(syntaxTree);
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = new AkburaCompilation(
                csharpCompilation,
                [tree],
                rootNamespace: "Demo",
                projectDirectory: Environment.CurrentDirectory)
            .GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var intType = csharpCompilation.GetSpecialType(SpecialType.System_Int32);
        var nullableIntType = csharpCompilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(intType);
        var enumType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            csharpCompilation.GetTypeByMetadataName("Demo.Status"));

        var nullableConversion = binder.Conversions.ClassifyConversion(intType, nullableIntType);
        var enumConversion = binder.Conversions.ClassifyConversion(intType, enumType);

        Assert.Equal(AkburaConversionKind.Implicit, nullableConversion.Kind);
        Assert.True(nullableConversion.CSharpConversion.IsImplicit);
        Assert.Same(nullableIntType, nullableConversion.TargetType);
        Assert.Equal(AkburaConversionKind.Explicit, enumConversion.Kind);
        Assert.True(enumConversion.CSharpConversion.IsExplicit);
        Assert.Same(enumType, enumConversion.TargetType);
    }

    [Fact]
    public void AkburaConversions_ExposeRoslynStyleFlags()
    {
        const string code = "state int count = 0;";
        const string csharpCode =
            """
            namespace Demo;

            public enum Status
            {
                None,
                Active
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(syntaxTree);
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = new AkburaCompilation(
                csharpCompilation,
                [tree],
                rootNamespace: "Demo",
                projectDirectory: Environment.CurrentDirectory)
            .GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var intType = csharpCompilation.GetSpecialType(SpecialType.System_Int32);
        var doubleType = csharpCompilation.GetSpecialType(SpecialType.System_Double);
        var nullableIntType = csharpCompilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(intType);
        var enumType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            csharpCompilation.GetTypeByMetadataName("Demo.Status"));

        var numericConversion = binder.Conversions.ClassifyConversion(intType, doubleType);
        var sameNumericConversion = binder.Conversions.ClassifyConversion(intType, doubleType);
        var nullableConversion = binder.Conversions.ClassifyConversion(intType, nullableIntType);
        var enumConversion = binder.Conversions.ClassifyConversion(intType, enumType);

        Assert.Equal(numericConversion, sameNumericConversion);
        Assert.True(numericConversion.Exists);
        Assert.True(numericConversion.IsImplicit);
        Assert.True(numericConversion.IsNumeric);
        Assert.False(numericConversion.IsUserDefined);
        Assert.Null(numericConversion.MethodSymbol);

        Assert.True(nullableConversion.IsNullable);
        Assert.True(nullableConversion.IsImplicit);

        Assert.True(enumConversion.IsEnumeration);
        Assert.True(enumConversion.IsExplicit);
    }

    [Fact]
    public void AkburaConversions_PreserveUserDefinedConversionMethod()
    {
        const string code = "state int count = 0;";
        const string csharpCode =
            """
            namespace Demo;

            public readonly struct Meters
            {
                public Meters(double value)
                {
                    Value = value;
                }

                public double Value { get; }

                public static implicit operator Meters(double value)
                {
                    return new Meters(value);
                }

                public static explicit operator double(Meters value)
                {
                    return value.Value;
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(syntaxTree);
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = new AkburaCompilation(
                csharpCompilation,
                [tree],
                rootNamespace: "Demo",
                projectDirectory: Environment.CurrentDirectory)
            .GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot(), BinderUsage.Expression);
        var doubleType = csharpCompilation.GetSpecialType(SpecialType.System_Double);
        var metersType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            csharpCompilation.GetTypeByMetadataName("Demo.Meters"));

        var implicitConversion = binder.Conversions.ClassifyConversion(doubleType, metersType);
        var explicitConversion = binder.Conversions.ClassifyConversion(metersType, doubleType);

        Assert.Equal(AkburaConversionKind.Implicit, implicitConversion.Kind);
        Assert.True(implicitConversion.IsUserDefined);
        Assert.True(implicitConversion.IsImplicit);
        Assert.NotNull(implicitConversion.MethodSymbol);
        Assert.Equal("op_Implicit", implicitConversion.MethodSymbol.MetadataName);
        Assert.Same(implicitConversion.MethodSymbol, implicitConversion.CSharpConversion.MethodSymbol);

        Assert.Equal(AkburaConversionKind.Explicit, explicitConversion.Kind);
        Assert.True(explicitConversion.IsUserDefined);
        Assert.True(explicitConversion.IsExplicit);
        Assert.NotNull(explicitConversion.MethodSymbol);
        Assert.Equal("op_Explicit", explicitConversion.MethodSymbol.MetadataName);
        Assert.Same(explicitConversion.MethodSymbol, explicitConversion.CSharpConversion.MethodSymbol);
    }

    [Fact]
    public void BoundConversionExpression_StoresConversionAndTargetType()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);
        var intBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(CSharpSyntaxFactory.ParseExpression("1")),
            isBindingPath: true);
        var doubleBinding = binder.BindReturnExpression(
            CreateReturnExpressionProbe(CSharpSyntaxFactory.ParseExpression("1.0")),
            isBindingPath: true);
        var intType = intBinding.TypeSymbol;
        var doubleType = doubleBinding.TypeSymbol;

        Assert.NotNull(intType);
        Assert.NotNull(doubleType);

        var operand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.None),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = binder.ClassifyConversion(intType, doubleType);

        var boundConversion = new BoundConversionExpression(
            state,
            binder,
            operand,
            conversion);

        Assert.Same(operand, boundConversion.Operand);
        Assert.Equal(AkburaConversionKind.Implicit, boundConversion.Conversion.Kind);
        Assert.Equal("Double", boundConversion.Type?.Name);
        Assert.False(boundConversion.HasErrors);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionReturnsBoundExpressionAndAppliesTargetConversion()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);
        var doubleType = binder.CSharpCompilation.GetSpecialType(SpecialType.System_Double);

        var bound = binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1"),
            doubleType);

        var conversion = Assert.IsType<BoundConversionExpression>(bound);
        var operand = Assert.IsType<BoundLiteralExpression>(conversion.Operand);
        Assert.Equal("Int32", operand.Type?.Name);
        Assert.Equal(1, operand.ConstantValue);
        Assert.Equal(AkburaConversionKind.Implicit, conversion.Conversion.Kind);
        Assert.Equal("Double", conversion.Type?.Name);
        Assert.False(conversion.HasErrors);
    }

    [Fact]
    public void MarkupPropertySetter_BindsDynamicValueWithExpectedType()
    {
        const string code =
            """
            using Avalonia.Controls;

            <TextBlock Width={1} />
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var markup = Assert.IsType<MarkupRootSyntax>(tree.GetRoot().Members[1]);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(markup.Element.StartTag!.Attributes));

        var bound = Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(attribute));
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(attribute));

        Assert.Equal("Width", bound.Property?.Name);
        Assert.Equal("Double", bound.ValueType.Name);
        Assert.Equal("Double", operation.ValueType.Name);
        Assert.True(model.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void StateAndParamInitializers_BindWithExplicitExpectedType()
    {
        const string code =
            """
            param double Size = 1;
            state double width = 1;
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[0]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[1]);

        var paramSymbol = Assert.IsAssignableFrom<IParamSymbol>(
            model.GetSymbolInfo(param).Symbol);
        var stateSymbol = Assert.IsAssignableFrom<IStateSymbol>(
            model.GetSymbolInfo(state).Symbol);
        var boundParam = Assert.IsType<BoundParamDeclaration>(
            model.BindingSession.BindSemanticSyntax(param));
        var boundState = Assert.IsType<BoundStateDeclaration>(
            model.BindingSession.BindSemanticSyntax(state));
        var defaultValue = Assert.IsType<BoundParamDefaultValue>(
            Assert.Single(boundParam.Children));
        var initializer = Assert.IsType<BoundStateInitializer>(
            Assert.Single(boundState.Children));

        Assert.Equal("Double", paramSymbol.Type.Name);
        Assert.Equal("Double", paramSymbol.DefaultValueType.Name);
        Assert.Equal("Double", stateSymbol.Type.Name);
        Assert.Equal("Double", stateSymbol.InitializerType.Name);
        Assert.Equal(AkburaConversionKind.Implicit, defaultValue.BindingResult.Conversion.Kind);
        Assert.Equal(AkburaConversionKind.Implicit, initializer.BindingResult.Conversion.Kind);
        Assert.True(model.GetSemanticDiagnostics(param).IsEmpty);
        Assert.True(model.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void StateAndParamInitializers_ReportInvalidExpectedTypeConversion()
    {
        const string code =
            """
            param int Count = "bad";
            state int width = "bad";
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[0]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[1]);

        _ = model.GetSymbolInfo(param);
        _ = model.GetSymbolInfo(state);

        Assert.Contains(
            model.GetSemanticDiagnostics(param),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);
        Assert.Contains(
            model.GetSemanticDiagnostics(state),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);
    }

    [Fact]
    public void AkcssPropertySetter_BindsCSharpValueWithExpectedType()
    {
        const string code =
            """
            @akcss {
                .panel {
                    Width: 1;
                }
            }
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(tree.GetRoot().Members[0]);
        var rule = Assert.IsType<AkcssStyleRuleSyntax>(Assert.Single(akcss.Members));
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));

        var bound = Assert.IsType<BoundAkcssPropertySetter>(
            model.BindingSession.BindOperationSyntax(assignment));
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            model.GetOperation(assignment));

        Assert.Equal("Width", bound.Property?.Name);
        Assert.Equal("Double", bound.ValueType.Name);
        Assert.Equal("Double", operation.ValueType.Name);
        Assert.True(model.GetSemanticDiagnostics(assignment).IsEmpty);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionBuildsLiteralAndBinaryBoundNodes()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);

        var bound = binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("1 + 2"));

        var binary = Assert.IsType<BoundBinaryExpression>(bound);
        Assert.Equal(CSharpSyntaxKind.AddExpression, binary.OperatorKind);
        Assert.Equal("Int32", binary.Type?.Name);

        var left = Assert.IsType<BoundLiteralExpression>(binary.Left);
        var right = Assert.IsType<BoundLiteralExpression>(binary.Right);
        Assert.Equal(1, left.ConstantValue);
        Assert.Equal(2, right.ConstantValue);
        Assert.Same(left, binary.Children[0]);
        Assert.Same(right, binary.Children[1]);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionBuildsCallBoundNode()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);

        var bound = binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("System.Math.Abs(1)"));

        var call = Assert.IsType<BoundCallExpression>(bound);
        Assert.Equal("Abs", call.TargetMethod?.Name);
        Assert.Equal("Int32", call.Type?.Name);
        Assert.NotNull(call.Receiver);
        Assert.Single(call.Arguments);
        Assert.Same(call.Receiver, call.Children[0]);
        Assert.Same(call.Arguments[0], call.Children[1]);

        var argument = Assert.IsType<BoundLiteralExpression>(call.Arguments[0]);
        Assert.Equal(1, argument.ConstantValue);

        var replacement = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var rewritten = Assert.IsType<BoundCallExpression>(
            new ReplacingBoundTreeRewriter(argument, replacement).Visit(call));

        Assert.NotSame(call, rewritten);
        Assert.Same(replacement, rewritten.Arguments[0]);
        Assert.Same(rewritten.Receiver, rewritten.Children[0]);
        Assert.Same(rewritten.Arguments[0], rewritten.Children[1]);
    }

    [Fact]
    public void CSharpProbeBinder_BindStatementBuildsLocalDeclarationBoundNode()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);

        var bound = binder.BindStatement(
            state,
            CSharpSyntaxFactory.ParseStatement("int value = 1 + 2;"));

        var localDeclaration = Assert.IsType<BoundLocalDeclarationStatement>(bound);
        var local = Assert.Single(localDeclaration.Locals);
        Assert.Equal("value", local.Name);
        Assert.Equal("Int32", local.Type.Name);

        var initializer = Assert.IsType<BoundBinaryExpression>(
            Assert.Single(localDeclaration.Initializers));
        Assert.Equal(CSharpSyntaxKind.AddExpression, initializer.OperatorKind);
        Assert.Same(initializer, Assert.Single(localDeclaration.Children));

        var left = Assert.IsType<BoundLiteralExpression>(initializer.Left);
        var right = Assert.IsType<BoundLiteralExpression>(initializer.Right);
        Assert.Equal(1, left.ConstantValue);
        Assert.Equal(2, right.ConstantValue);

        var replacement = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var rewritten = Assert.IsType<BoundLocalDeclarationStatement>(
            new ReplacingBoundTreeRewriter(left, replacement).Visit(localDeclaration));
        var rewrittenInitializer = Assert.IsType<BoundBinaryExpression>(
            Assert.Single(rewritten.Initializers));

        Assert.NotSame(localDeclaration, rewritten);
        Assert.Same(replacement, rewrittenInitializer.Left);
    }

    [Fact]
    public void CSharpProbeBinder_BindExpressionMarksInvalidTargetConversionAsError()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var binder = model.BindingSession.GetCSharpProbeBinder(state, BinderUsage.Expression);
        var intType = binder.CSharpCompilation.GetSpecialType(SpecialType.System_Int32);

        var bound = binder.BindExpression(
            state,
            CSharpSyntaxFactory.ParseExpression("\"text\""),
            intType);

        var conversion = Assert.IsType<BoundConversionExpression>(bound);
        var operand = Assert.IsType<BoundLiteralExpression>(conversion.Operand);
        Assert.Equal("String", operand.Type?.Name);
        Assert.Equal("text", operand.ConstantValue);
        Assert.Equal(AkburaConversionKind.None, conversion.Conversion.Kind);
        Assert.True(conversion.HasErrors);
    }

    [Fact]
    public void BindingDiagnosticBag_DeduplicatesSemanticAndCSharpDiagnostics()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var semanticDiagnostic = new AkburaSemanticDiagnostic(
            state,
            ErrorCodes.ERR_SyntaxError,
            ["same"]);
        var descriptor = new DiagnosticDescriptor(
            "AKBURA_TEST",
            "Title",
            "Message",
            "Test",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        var csharpDiagnostic = Diagnostic.Create(
            descriptor,
            Location.None);
        var bag = new BindingDiagnosticBag();

        bag.Add(semanticDiagnostic);
        bag.Add(semanticDiagnostic);
        bag.AddCSharp(csharpDiagnostic);
        bag.AddCSharp(csharpDiagnostic);

        Assert.Single(bag.ToSemanticDiagnostics());
        Assert.Single(bag.ToCSharpDiagnostics());
    }

    [Fact]
    public void CSharpProbeDiagnostics_StillSurfaceThroughSemanticDiagnostics()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "<TextBlock Text={NotExisting.Constant} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var markup = Assert.IsType<MarkupRootSyntax>(tree.GetRoot().Members[1]);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(markup.Element.StartTag!.Attributes[0]);

        var diagnostics = model.GetSemanticDiagnostics(attribute);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);
    }

    [Fact]
    public void SemanticBindingCache_AkcssAssignmentEdit_RebindsUnchangedStyleOperationsInNewSnapshot()
    {
        const string oldCode =
            "@akcss {\n" +
            "    Button.unchanged {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "\n" +
            "    Button.changed {\n" +
            "        Padding: 4;\n" +
            "    }\n" +
            "}";
        const string newCode =
            "@akcss {\n" +
            "    Button.unchanged {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "\n" +
            "    Button.changed {\n" +
            "        Padding: 8;\n" +
            "    }\n" +
            "}";
        var changeStart = oldCode.IndexOf("Padding: 4", StringComparison.Ordinal) + "Padding: ".Length;
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldBlock = Assert.IsType<InlineAkcssBlockSyntax>(oldTree.GetRoot().Members[0]);
        var oldUnchangedRule = Assert.IsType<AkcssStyleRuleSyntax>(oldBlock.Members[0]);
        var oldChangedRule = Assert.IsType<AkcssStyleRuleSyntax>(oldBlock.Members[1]);
        var oldBackground = Assert.IsType<AkcssAssignmentSyntax>(oldUnchangedRule.Members[0]);
        var oldPadding = Assert.IsType<AkcssAssignmentSyntax>(oldChangedRule.Members[0]);
        var oldBackgroundOperation = oldModel.GetOperation(oldBackground);
        var oldPaddingOperation = oldModel.GetOperation(oldPadding);

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newBlock = Assert.IsType<InlineAkcssBlockSyntax>(newTree.GetRoot().Members[0]);
        var newUnchangedRule = Assert.IsType<AkcssStyleRuleSyntax>(newBlock.Members[0]);
        var newChangedRule = Assert.IsType<AkcssStyleRuleSyntax>(newBlock.Members[1]);
        var newBackground = Assert.IsType<AkcssAssignmentSyntax>(newUnchangedRule.Members[0]);
        var newPadding = Assert.IsType<AkcssAssignmentSyntax>(newChangedRule.Members[0]);
        var newBackgroundOperation = newModel.GetOperation(newBackground);
        var newPaddingOperation = newModel.GetOperation(newPadding);

        Assert.Same(oldBackground.Green, newBackground.Green);
        Assert.NotSame(oldBackgroundOperation, newBackgroundOperation);
        Assert.NotSame(oldPaddingOperation, newPaddingOperation);
        Assert.Same(newBackgroundOperation, newModel.GetOperation(newBackground));
        Assert.Same(newPaddingOperation, newModel.GetOperation(newPadding));
    }

    private static AkburaCompilation CreateCompilation(
        AkburaSyntaxTree tree,
        ImmutableArray<AkcssSyntaxTree> akcssTrees = default)
    {
        return new AkburaCompilation(
            CreateCSharpCompilation(),
            [tree],
            akcssTrees.IsDefault ? ImmutableArray<AkcssSyntaxTree>.Empty : akcssTrees,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.Controls.Button).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.Media.Color).Assembly.Location),
        };

        return CSharpCompilation.Create(
            "SemanticArchitectureTests",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var parts = new string[pathParts.Length + 5];
        parts[0] = AppContext.BaseDirectory;
        parts[1] = "..";
        parts[2] = "..";
        parts[3] = "..";
        parts[4] = "..";
        Array.Copy(pathParts, 0, parts, 5, pathParts.Length);

        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts)));
    }

    private static Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax CreateReturnExpressionProbe(
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expression);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));
        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>(method));

        return CSharpSyntaxFactory.CompilationUnit()
            .WithMembers(CSharpSyntaxFactory.SingletonList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>(probeClass));
    }

    private sealed class RecordingBoundTreeVisitor : BoundTreeVisitor
    {
        public List<string> Visited { get; } = [];

        public override void VisitBlock(BoundBlock node)
        {
            Visited.Add("block");
        }

        public override void VisitConversionExpression(BoundConversionExpression node)
        {
            Visited.Add("conversion");
        }

        public override void VisitBadExpression(BoundBadExpression node)
        {
            Visited.Add("bad");
        }

        public override void VisitExpression(BoundExpression node)
        {
            Visited.Add("expression");
        }
    }

    private sealed class RecordingBoundTreeWalker : BoundTreeWalker
    {
        public List<string> Visited { get; } = [];

        public override void DefaultVisit(BoundNode node)
        {
            Visited.Add(node.GetType().Name);
            base.DefaultVisit(node);
        }
    }

    private sealed class ReplacingBoundTreeRewriter : BoundTreeRewriter
    {
        private readonly BoundExpression _oldOperand;
        private readonly BoundExpression _newOperand;

        public ReplacingBoundTreeRewriter(
            BoundExpression oldOperand,
            BoundExpression newOperand)
        {
            _oldOperand = oldOperand;
            _newOperand = newOperand;
        }

        public override BoundNode? VisitExpression(BoundExpression node)
        {
            return ReferenceEquals(node, _oldOperand)
                ? _newOperand
                : base.VisitExpression(node);
        }

        public override BoundNode? VisitLiteralExpression(BoundLiteralExpression node)
        {
            return ReferenceEquals(node, _oldOperand)
                ? _newOperand
                : base.VisitLiteralExpression(node);
        }

        public override BoundNode? VisitBinaryExpression(BoundBinaryExpression node)
        {
            return ReferenceEquals(node, _oldOperand)
                ? _newOperand
                : base.VisitBinaryExpression(node);
        }
    }

    private sealed class RecordingSymbolBoundTreeRewriter : BoundTreeRewriter
    {
        public int StateSymbolCount { get; private set; }

        public int MethodSymbolCount { get; private set; }

        public int LocalSymbolCount { get; private set; }

        public int PropertySymbolCount { get; private set; }

        public int RoutedEventSymbolCount { get; private set; }

        public int CSharpSymbolDefinitionCount { get; private set; }

        public int CSharpOperationDefinitionCount { get; private set; }

        protected override AkburaSymbol VisitStateSymbol(IStateSymbol symbol)
        {
            StateSymbolCount++;
            return base.VisitStateSymbol(symbol);
        }

        protected override AkburaSymbol VisitPropertySymbol(AkburaPropertySymbol symbol)
        {
            PropertySymbolCount++;
            return base.VisitPropertySymbol(symbol);
        }

        protected override AkburaSymbol VisitRoutedEventSymbol(IRoutedEventSymbol symbol)
        {
            RoutedEventSymbolCount++;
            return base.VisitRoutedEventSymbol(symbol);
        }

        protected override Microsoft.CodeAnalysis.ISymbol VisitMethodSymbol(IMethodSymbol symbol)
        {
            MethodSymbolCount++;
            return base.VisitMethodSymbol(symbol);
        }

        protected override Microsoft.CodeAnalysis.ISymbol VisitLocalSymbol(ILocalSymbol symbol)
        {
            LocalSymbolCount++;
            return base.VisitLocalSymbol(symbol);
        }

        protected override CSharpSymbolDefinition VisitCSharpSymbolDefinition(CSharpSymbolDefinition definition)
        {
            if (!definition.IsDefault)
            {
                CSharpSymbolDefinitionCount++;
            }

            return base.VisitCSharpSymbolDefinition(definition);
        }

        protected override CSharpOperationDefinition VisitCSharpOperationDefinition(CSharpOperationDefinition definition)
        {
            if (!definition.IsDefault)
            {
                CSharpOperationDefinitionCount++;
            }

            return base.VisitCSharpOperationDefinition(definition);
        }
    }

    private sealed class RecordingOperationWalker : OperationWalker
    {
        public List<AkburaOperationKind> Kinds { get; } = [];

        public override void DefaultVisit(AkburaOperation operation)
        {
            Kinds.Add(operation.Kind);
            base.DefaultVisit(operation);
        }
    }

    private sealed class RecordingSymbolVisitor : AkburaSymbolVisitor
    {
        public List<string> Visited { get; } = [];

        public override void VisitInject(IInjectSymbol symbol)
        {
            Visited.Add("inject");
        }

        public override void VisitParameter(IParamSymbol symbol)
        {
            Visited.Add("param");
        }

        public override void VisitState(IStateSymbol symbol)
        {
            Visited.Add("state");
        }

        public override void VisitCommand(ICommandSymbol symbol)
        {
            Visited.Add("command");
        }
    }

    private sealed class ConstantHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            return string.Equals(x, y, StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            return 1;
        }
    }
}

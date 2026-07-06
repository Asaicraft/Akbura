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

public sealed class DeclarationArchitectureTests : SemanticArchitectureTestBase
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
    public void DeclarationTable_IndexesDeclarationPathsForBinderLookup()
    {
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

        Assert.True(table.TryGetDeclarationPath(textBlock, out var exactPath));
        Assert.Collection(
            exactPath,
            item => Assert.Equal(AkburaDeclarationKind.Component, item.Kind),
            item => Assert.Equal(AkburaDeclarationKind.CSharpStatement, item.Kind),
            item => Assert.Equal(AkburaDeclarationKind.CSharpBlock, item.Kind),
            item => Assert.Equal(AkburaDeclarationKind.MarkupRoot, item.Kind),
            item => Assert.Equal(AkburaDeclarationKind.MarkupElement, item.Kind),
            item =>
            {
                Assert.Equal(AkburaDeclarationKind.MarkupElement, item.Kind);
                Assert.Equal("TextBlock", item.Name);
            });

        Assert.True(table.TryGetDeclarationPath(root, textBlock.Position, out var positionPath));
        Assert.Equal(exactPath.Length, positionPath.Length);
        for (var index = 0; index < exactPath.Length; index++)
        {
            Assert.Equal(exactPath[index].Kind, positionPath[index].Kind);
            Assert.Equal(exactPath[index].Name, positionPath[index].Name);
            Assert.Same(exactPath[index].Syntax, positionPath[index].Syntax);
        }
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

        try
        {
            Assert.Null(model.GetDeclaredSymbol(foreignRoot));
        }
        catch (ArgumentException)
        {
            // Debug builds keep the semantic model ownership guard active.
        }
    }

}

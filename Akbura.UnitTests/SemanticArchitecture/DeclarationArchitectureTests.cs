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
using System.Runtime.CompilerServices;
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
        Assert.Same(component, Assert.Single(state.RootNamespaces).Key);
        Assert.Same(akcss, Assert.Single(state.AkcssRootNamespaces).Key);
        Assert.Same(compilation.DeclarationTable, state.DeclarationTable);
        Assert.Equal("Counter", Assert.Single(state.DeclarationTable.Components).Name);
        Assert.Equal("Demo.Pages.Counter.akcss", Assert.Single(state.DeclarationTable.AkcssModules).Name);
        Assert.Collection(
            state.RootDeclarationTable.RootDeclarations,
            declaration =>
            {
                Assert.Equal(string.Empty, declaration.Name);
                Assert.Equal("Counter", Assert.Single(declaration.Children).Name);
            },
            declaration =>
            {
                Assert.Equal(string.Empty, declaration.Name);
                Assert.Equal("Demo.Pages.Counter.akcss", Assert.Single(declaration.Children).Name);
            });
        Assert.Equal(1, state.LastComputedMemberNames[component].Count);
        Assert.Equal(1, state.LastComputedAkcssMemberNames[akcss].Count);
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
    public void SyntaxAndDeclarationManager_ReplaceSyntaxTreeCarriesLastComputedMemberNameBoxes()
    {
        var oldTree = AkburaSyntaxTree.ParseText(
            """
            state int count = 0;

            @akcss {
                .card {
                    Width: 1;
                }
            }
            """,
            "Counter.akbura");
        var newTree = AkburaSyntaxTree.ParseText(
            """
            state int count = 1;

            @akcss {
                .card {
                    Width: 2;
                }
            }
            """,
            "Counter.akbura");
        var oldCompilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [oldTree]);
        var oldState = oldCompilation.SyntaxAndDeclarations.GetLazyState();
        var oldRoot = oldState.RootNamespaces[oldTree].Value;
        var oldComponent = Assert.IsType<SingleTypeDeclaration>(Assert.Single(oldRoot.Children));
        var oldInlineAkcss = Assert.IsType<SingleTypeDeclaration>(Assert.Single(oldComponent.Children));
        Assert.True(oldState.LastComputedMemberNames[oldTree][0].TryGetTarget(out var oldComponentNames));
        Assert.True(oldState.LastComputedMemberNames[oldTree][1].TryGetTarget(out var oldInlineAkcssNames));
        Assert.Same(oldComponent.MemberNames, oldComponentNames);
        Assert.Same(oldInlineAkcss.MemberNames, oldInlineAkcssNames);

        var newCompilation = oldCompilation.ReplaceSyntaxTree(oldTree, newTree);
        var newState = newCompilation.SyntaxAndDeclarations.GetLazyState();
        var newRoot = newState.RootNamespaces[newTree].Value;
        var newComponent = Assert.IsType<SingleTypeDeclaration>(Assert.Single(newRoot.Children));
        var newInlineAkcss = Assert.IsType<SingleTypeDeclaration>(Assert.Single(newComponent.Children));

        Assert.Equal(2, newState.LastComputedMemberNames[newTree].Count);
        Assert.True(newState.LastComputedMemberNames[newTree][0].TryGetTarget(out var newComponentNames));
        Assert.True(newState.LastComputedMemberNames[newTree][1].TryGetTarget(out var newInlineAkcssNames));
        Assert.Same(newComponent.MemberNames, newComponentNames);
        Assert.Same(newInlineAkcss.MemberNames, newInlineAkcssNames);
        Assert.Contains("count", newComponentNames.Value);
        Assert.Contains("@akcss", newComponentNames.Value);
        Assert.Contains(".card", newInlineAkcssNames.Value);
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
    public void SyntaxAndDeclarationManager_AddRemoveSyntaxTreesUpdatesHotStateIncrementally()
    {
        var first = AkburaSyntaxTree.ParseText("state int first = 0;", "First.akbura");
        var second = AkburaSyntaxTree.ParseText("state int second = 0;", "Second.akbura");
        var third = AkburaSyntaxTree.ParseText("state int third = 0;", "Third.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [first, second]);
        var oldTable = compilation.DeclarationTable;

        var added = compilation.AddSyntaxTrees([third]);
        var addedTable = added.DeclarationTable;
        var removed = added.RemoveSyntaxTrees([second]);
        var removedTable = removed.DeclarationTable;

        Assert.Equal(3, addedTable.Components.Length);
        Assert.Same(oldTable.Components[0], addedTable.Components[0]);
        Assert.Same(oldTable.Components[1], addedTable.Components[1]);
        Assert.Equal("Third", addedTable.Components[2].Name);
        Assert.Same(oldTable.Components[0], removedTable.Components[0]);
        Assert.Same(addedTable.Components[2], removedTable.Components[1]);
        Assert.Equal(0, removed.SyntaxAndDeclarations.GetLazyState().SyntaxOrdinalMap[first]);
        Assert.Equal(1, removed.SyntaxAndDeclarations.GetLazyState().SyntaxOrdinalMap[third]);
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


    [Fact]
    public void DeclarationTable_BuilderAddsAndRemovesLatestRootDeclaration()
    {
        var oldRoot = CreateLazyRoot("Old", "Nested");
        var latestRoot = CreateLazyRoot("Latest");
        var builder = DeclarationTable.Empty.ToBuilder();
        builder.AddRootDeclaration(oldRoot);
        var oldTable = builder.ToDeclarationTableAndFree();

        builder = oldTable.ToBuilder();
        builder.AddRootDeclaration(latestRoot);
        var tableWithLatest = builder.ToDeclarationTableAndFree();

        Assert.Collection(
            tableWithLatest.RootDeclarations,
            declaration => Assert.Same(oldRoot.Value, declaration),
            declaration => Assert.Same(latestRoot.Value, declaration));
        Assert.Collection(
            tableWithLatest.MergedRoot.Declarations,
            declaration => Assert.Same(oldRoot.Value, declaration),
            declaration => Assert.Same(latestRoot.Value, declaration));
        Assert.Collection(
            tableWithLatest.MergedRoot.Children,
            declaration => Assert.Equal("Old", declaration.Name),
            declaration => Assert.Equal("Latest", declaration.Name));
        Assert.Contains("Old", tableWithLatest.DeclarationNames);
        Assert.Contains("Nested", tableWithLatest.DeclarationNames);
        Assert.Contains("Latest", tableWithLatest.DeclarationNames);

        builder = tableWithLatest.ToBuilder();
        builder.RemoveRootDeclaration(latestRoot);
        var tableWithoutLatest = builder.ToDeclarationTableAndFree();

        Assert.Collection(
            tableWithoutLatest.RootDeclarations,
            declaration => Assert.Same(oldRoot.Value, declaration));
        Assert.Collection(
            tableWithoutLatest.MergedRoot.Declarations,
            declaration => Assert.Same(oldRoot.Value, declaration));
        Assert.Contains("Old", tableWithoutLatest.DeclarationNames);
        Assert.Contains("Nested", tableWithoutLatest.DeclarationNames);
        Assert.DoesNotContain("Latest", tableWithoutLatest.DeclarationNames);
    }

    [Fact]
    public void DeclarationTable_OlderRootsPreserveInsertionOrder()
    {
        var first = CreateLazyRoot("First");
        var second = CreateLazyRoot("Second");
        var third = CreateLazyRoot("Third");
        var builder = DeclarationTable.Empty.ToBuilder();
        builder.AddRootDeclaration(first);
        var table = builder.ToDeclarationTableAndFree();

        builder = table.ToBuilder();
        builder.AddRootDeclaration(second);
        table = builder.ToDeclarationTableAndFree();

        builder = table.ToBuilder();
        builder.AddRootDeclaration(third);
        table = builder.ToDeclarationTableAndFree();

        Assert.Collection(
            table.RootDeclarations,
            declaration => Assert.Same(first.Value, declaration),
            declaration => Assert.Same(second.Value, declaration),
            declaration => Assert.Same(third.Value, declaration));
        Assert.Collection(
            table.MergedRoot.Declarations,
            declaration => Assert.Same(first.Value, declaration),
            declaration => Assert.Same(second.Value, declaration),
            declaration => Assert.Same(third.Value, declaration));
        Assert.Collection(
            table.MergedRoot.Children,
            declaration => Assert.Equal("First", declaration.Name),
            declaration => Assert.Equal("Second", declaration.Name),
            declaration => Assert.Equal("Third", declaration.Name));
    }

    [Fact]
    public void DeclarationKind_ConvertsFromSyntaxKind()
    {
        Assert.Equal(DeclarationKind.Component, AkburaSyntaxKind.AkburaDocumentSyntax.ToDeclarationKind());
        Assert.Equal(DeclarationKind.Namespace, AkburaSyntaxKind.NamespaceDeclarationSyntax.ToDeclarationKind());
        Assert.Equal(DeclarationKind.AkcssModule, AkburaSyntaxKind.AkcssDocumentSyntax.ToDeclarationKind());
        Assert.Equal(DeclarationKind.AkcssModule, AkburaSyntaxKind.InlineAkcssBlockSyntax.ToDeclarationKind());
        Assert.Equal(DeclarationKind.AkcssStyle, AkburaSyntaxKind.AkcssStyleRuleSyntax.ToDeclarationKind());
        Assert.Equal(DeclarationKind.AkcssUtility, AkburaSyntaxKind.AkcssUtilityDeclarationSyntax.ToDeclarationKind());
        Assert.ThrowsAny<Exception>(() => AkburaSyntaxKind.StateDeclarationSyntax.ToDeclarationKind());
    }

    [Fact]
    public void SingleNamespaceDeclarations_PreserveFlagsLocationsAndChildren()
    {
        var root = AkburaSyntaxTree.ParseText("namespace Demo;\n<TextBlock />").GetRoot();
        var nameLocation = new SourceLocation(root);
        var child = SingleNamespaceDeclaration.Create(
            "Child",
            hasUsings: false,
            hasExternAliases: false,
            root,
            nameLocation,
            ImmutableArray<SingleNamespaceOrTypeDeclaration>.Empty,
            ImmutableArray<AkburaDiagnostic>.Empty);
        var namespaceWithImports = SingleNamespaceDeclaration.Create(
            "Demo",
            hasUsings: true,
            hasExternAliases: true,
            root,
            nameLocation,
            ImmutableArray.Create<SingleNamespaceOrTypeDeclaration>(child),
            ImmutableArray<AkburaDiagnostic>.Empty);
        var rootDeclaration = new RootSingleNamespaceDeclaration(
            hasGlobalUsings: true,
            hasUsings: true,
            hasExternAliases: true,
            root,
            ImmutableArray.Create<SingleNamespaceOrTypeDeclaration>(namespaceWithImports),
            ImmutableArray.Create(new ReferenceDirective("demo.dll")),
            hasAssemblyAttributes: true,
            ImmutableArray<AkburaDiagnostic>.Empty,
            QuickAttributes.None);

        Assert.Equal(DeclarationKind.Namespace, namespaceWithImports.Kind);
        Assert.True(namespaceWithImports.HasUsings);
        Assert.True(namespaceWithImports.HasExternAliases);
        Assert.Single(namespaceWithImports.Children);
        Assert.Single(((Declaration)namespaceWithImports).Children);
        Assert.Same(root, namespaceWithImports.Syntax);
        Assert.Same(root, namespaceWithImports.Location.Syntax);
        Assert.Same(root, namespaceWithImports.NameLocation.Syntax);

        Assert.True(rootDeclaration.HasGlobalUsings);
        Assert.True(rootDeclaration.HasUsings);
        Assert.True(rootDeclaration.HasExternAliases);
        Assert.True(rootDeclaration.HasAssemblyAttributes);
        Assert.Equal("demo.dll", Assert.Single(rootDeclaration.ReferenceDirectives).File);
        Assert.Same(namespaceWithImports, Assert.Single(rootDeclaration.Children));
    }

    [Fact]
    public void SourceLocation_PreservesKindSyntaxSpanAndEquality()
    {
        var root = AkburaSyntaxTree.ParseText("namespace Demo;\n<TextBlock />").GetRoot();
        var location = new SourceLocation(root);
        var sameLocation = new SourceLocation(root, root.Span);
        var differentLocation = new SourceLocation(root, new TextSpan(root.Span.Start, 0));

        Assert.Equal(Akbura.Language.LocationKind.SourceFile, location.Kind);
        Assert.Same(root, location.SourceSyntax);
        Assert.Same(root, location.Syntax);
        Assert.Equal(root.Span, location.SourceSpan);
        Assert.Equal(root.Span, location.Span);
        Assert.Equal(location, sameLocation);
        Assert.NotEqual(location, differentLocation);
        Assert.Equal(Akbura.Language.LocationKind.None, Akbura.Language.Location.None.Kind);
        Assert.Same(Akbura.Language.Location.None, NoLocation.Singleton);
    }

    [Fact]
    public void DeclarationTreeBuilder_BuildsAkburaRootDeclaration()
    {
        const string code =
            """
            global using System;
            using Avalonia.Controls;
            namespace Demo.Pages;

            state int count = 0;
            param string Title = "";

            @akcss {
                .card {
                    Width: 1;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <TextBlock />
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Pages/Dashboard.akbura");

        var declaration = DeclarationTreeBuilder.ForTree(tree);

        Assert.True(declaration.HasGlobalUsings);
        Assert.True(declaration.HasUsings);
        var demoNamespace = Assert.IsType<SingleNamespaceDeclaration>(Assert.Single(declaration.Children));
        Assert.Equal("Demo", demoNamespace.Name);
        var pagesNamespace = Assert.IsType<SingleNamespaceDeclaration>(Assert.Single(demoNamespace.Children));
        Assert.Equal("Pages", pagesNamespace.Name);
        var component = Assert.IsType<SingleTypeDeclaration>(Assert.Single(pagesNamespace.Children));
        Assert.Equal(DeclarationKind.Component, component.Kind);
        Assert.Equal("Dashboard", component.Name);
        Assert.Contains("count", component.MemberNames.Value);
        Assert.Contains("Title", component.MemberNames.Value);
        Assert.Contains("@akcss", component.MemberNames.Value);

        var inlineAkcss = Assert.IsType<SingleTypeDeclaration>(Assert.Single(component.Children));
        Assert.Equal(DeclarationKind.AkcssModule, inlineAkcss.Kind);
        Assert.Equal("@akcss", inlineAkcss.Name);
        Assert.Contains(".card", inlineAkcss.MemberNames.Value);
        Assert.Contains(".w-(double value)", inlineAkcss.MemberNames.Value);
        Assert.Collection(
            inlineAkcss.Children,
            child => Assert.Equal(DeclarationKind.AkcssStyle, child.Kind),
            child => Assert.Equal(DeclarationKind.AkcssUtility, child.Kind));
    }

    [Fact]
    public void DeclarationTreeBuilder_CachesContainerMemberNames()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var cacheField = typeof(DeclarationTreeBuilder).GetField("s_nodeToMemberNames", flags);
        Assert.NotNull(cacheField);
        Assert.Contains(
            "ConditionalWeakTable",
            cacheField!.FieldType.FullName);
        const string code =
            """
            state int count = 0;
            state int count = 1;

            @akcss {
                .card {
                    Width: 1;
                }

                .card {
                    Width: 2;
                }
            }
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");

        var declaration = DeclarationTreeBuilder.ForTree(tree);

        var component = Assert.IsType<SingleTypeDeclaration>(Assert.Single(declaration.Children));
        var inlineAkcss = Assert.IsType<SingleTypeDeclaration>(Assert.Single(component.Children));
        Assert.Equal(2, inlineAkcss.Children.Length);
        var style = Assert.IsType<SingleTypeDeclaration>(inlineAkcss.Children[0]);
        Assert.True(DeclarationTreeBuilder.CachesComputedMemberNames(component));
        Assert.True(DeclarationTreeBuilder.CachesComputedMemberNames(inlineAkcss));
        Assert.False(DeclarationTreeBuilder.CachesComputedMemberNames(style));
        Assert.Equal(2, component.MemberNames.Value.Count);
        Assert.Contains("count", component.MemberNames.Value);
        Assert.Contains("@akcss", component.MemberNames.Value);
        Assert.Single(inlineAkcss.MemberNames.Value);
        Assert.Contains(".card", inlineAkcss.MemberNames.Value);
    }

    [Fact]
    public void DeclarationTreeBuilder_BuildsAkcssRootDeclaration()
    {
        const string code =
            """
            @using Demo.Shared;

            .card {
                Width: 1;
            }

            @utilities {
                .w {
                    Width: 1;
                }
            }
            """;
        var tree = AkcssSyntaxTree.ParseText(
            code,
            "Styles.akcss",
            "Demo.Styles.akcss");

        var declaration = DeclarationTreeBuilder.ForTree(tree);

        Assert.True(declaration.HasUsings);
        var module = Assert.IsType<SingleTypeDeclaration>(Assert.Single(declaration.Children));
        Assert.Equal(DeclarationKind.AkcssModule, module.Kind);
        Assert.Equal("Demo.Styles.akcss", module.Name);
        Assert.Contains(".card", module.MemberNames.Value);
        Assert.Contains(".w", module.MemberNames.Value);
        Assert.Collection(
            module.Children,
            child => Assert.Equal(DeclarationKind.AkcssStyle, child.Kind),
            child => Assert.Equal(DeclarationKind.AkcssUtility, child.Kind));
    }

    private static Lazy<RootSingleNamespaceDeclaration> CreateLazyRoot(
        string childName,
        string? nestedChildName = null)
    {
        return new Lazy<RootSingleNamespaceDeclaration>(() =>
        {
            var root = AkburaSyntaxTree.ParseText("<TextBlock />").GetRoot();
            var children = nestedChildName == null
                ? ImmutableArray<SingleTypeDeclaration>.Empty
                : ImmutableArray.Create(CreateTypeDeclaration(root, nestedChildName));
            var child = CreateTypeDeclaration(root, childName, children);

            return new RootSingleNamespaceDeclaration(
                hasGlobalUsings: false,
                hasUsings: false,
                hasExternAliases: false,
                root,
                ImmutableArray.Create<SingleNamespaceOrTypeDeclaration>(child),
                ImmutableArray<ReferenceDirective>.Empty,
                hasAssemblyAttributes: false,
                ImmutableArray<AkburaDiagnostic>.Empty,
                QuickAttributes.None);
        });
    }

    private static SingleTypeDeclaration CreateTypeDeclaration(
        AkburaSyntax syntax,
        string name,
        ImmutableArray<SingleTypeDeclaration> children = default)
    {
        return new SingleTypeDeclaration(
            DeclarationKind.Component,
            name,
            arity: 0,
            modifiers: DeclarationModifiers.None,
            declFlags: SingleTypeDeclaration.TypeDeclarationFlags.None,
            syntax,
            new SourceLocation(syntax),
            new StrongBox<ImmutableSegmentedHashSet<string>>(ImmutableSegmentedHashSet<string>.Empty),
            children,
            ImmutableArray<AkburaDiagnostic>.Empty,
            QuickAttributes.None);
    }


}

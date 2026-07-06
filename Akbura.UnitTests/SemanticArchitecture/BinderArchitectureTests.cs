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

public sealed class BinderArchitectureTests : SemanticArchitectureTestBase
{
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
            declaration => SemanticSyntaxIdentity.Equals(declaration.Syntax, ifStatement));
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

}

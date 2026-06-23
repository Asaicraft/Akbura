using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Threading.Tasks;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;
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
    public void SemanticBindingCache_NoChangeCompilationReusesCachedSymbol()
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
        Assert.Same(oldSymbol, newSymbol);
    }

    [Fact]
    public void SemanticBindingCache_StateInitializerEdit_ReusesUnchangedTopLevelSymbols()
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

        Assert.Same(oldFirstSymbol, newModel.GetSymbolInfo(newFirst).Symbol);
        Assert.NotSame(oldChangedSymbol, newModel.GetSymbolInfo(newChanged).Symbol);
        Assert.Same(oldLastSymbol, newModel.GetSymbolInfo(newLast).Symbol);
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
    public void SemanticBindingCache_UnchangedGreenNodeReusesPreviousBoundNode()
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

        Assert.Same(oldBoundNode, newBoundNode);
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
        Assert.Contains(AkburaOperationKind.AkcssAssignment, walker.Kinds);
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

        var markupBinder = Assert.IsType<MarkupBinder>(model.GetBinder(markup, BinderUsage.Markup));
        Assert.Empty(markupBinder.GetDeclaredSymbolsForScope(markup));
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
            operation: null,
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
        var badStatement = new BoundBadStatement(
            state,
            binder,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty,
            ImmutableArray.Create<BoundNode>(errorExpression));

        Assert.True(errorExpression.IsError);
        Assert.True(errorExpression.HasErrors);
        Assert.IsAssignableFrom<BoundStatement>(badStatement);
        Assert.True(badStatement.IsError);
        Assert.True(badStatement.HasErrors);
        Assert.Same(errorExpression, Assert.Single(badStatement.Children));
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
            operation: null,
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
        var visitor = new RecordingBoundTreeVisitor();

        visitor.Visit(block);
        visitor.Visit(conversion);
        visitor.Visit(expression);

        Assert.Equal(
            ["block", "conversion", "expression"],
            visitor.Visited);
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
            operation: null,
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
            operation: null,
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var newOperand = new BoundExpression(
            state,
            binder,
            AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            operation: null,
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var conversion = new BoundConversionExpression(
            state,
            binder,
            oldOperand,
            new AkburaConversion(AkburaConversionKind.Identity, null, null));

        Assert.Same(conversion, new BoundTreeRewriter().Visit(conversion));

        var rewritten = Assert.IsType<BoundConversionExpression>(
            new ReplacingBoundTreeRewriter(oldOperand, newOperand).Visit(conversion));

        Assert.NotSame(conversion, rewritten);
        Assert.Same(newOperand, rewritten.Operand);
        Assert.Equal(conversion.Conversion.Kind, rewritten.Conversion.Kind);
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
    public void BindingSession_CacheIsThreadSafeForParallelBinderRequests()
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

        var first = binders[0];
        Assert.All(binders, binder => Assert.Same(first, binder));
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
            operation: null,
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
            operation: null,
            diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var rewritten = Assert.IsType<BoundCallExpression>(
            new ReplacingBoundTreeRewriter(argument, replacement).Visit(call));

        Assert.NotSame(call, rewritten);
        Assert.Same(replacement, rewritten.Arguments[0]);
        Assert.Same(rewritten.Receiver, rewritten.Children[0]);
        Assert.Same(rewritten.Arguments[0], rewritten.Children[1]);
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
    public void SemanticBindingCache_AkcssAssignmentEdit_ReusesUnchangedStyleOperations()
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

        Assert.Same(oldBackgroundOperation, newModel.GetOperation(newBackground));
        Assert.NotSame(oldPaddingOperation, newModel.GetOperation(newPadding));
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
}

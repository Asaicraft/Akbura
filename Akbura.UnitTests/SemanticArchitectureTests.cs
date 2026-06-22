using Akbura.Language;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;

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

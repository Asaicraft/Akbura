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

public sealed class SemanticBindingCacheTests : SemanticArchitectureTestBase
{
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
    public void SemanticBindingCache_ProtectsSnapshotCachesWithReaderWriterLock()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "SemanticBindingCache.cs");

        Assert.Contains("ReaderWriterLockSlim", source);
        Assert.Contains("LockRecursionPolicy.NoRecursion", source);
        Assert.Contains("EnterReadLock", source);
        Assert.Contains("EnterWriteLock", source);
        Assert.Contains("ExitReadLock", source);
        Assert.Contains("ExitWriteLock", source);
        Assert.DoesNotContain("ConcurrentDictionary<AkburaSyntax", source);
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
    public void SemanticBindingCache_AllowsParallelSemanticQueries()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <TextBlock Text={count.ToString()} />
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[1]);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(markup.Element.StartTag!.Attributes[0]);
        var symbols = new AkburaSymbol?[64];
        var operations = new AkburaOperation?[64];
        var diagnostics = new ImmutableArray<AkburaSemanticDiagnostic>[64];
        var boundNodes = new BoundNode[64];

        Parallel.For(0, symbols.Length, index =>
        {
            symbols[index] = model.GetSymbolInfo(state).Symbol;
            operations[index] = model.GetOperation(attribute);
            diagnostics[index] = model.GetSemanticDiagnostics(root);
            boundNodes[index] = model.BindingSession.BindSemanticSyntax(markup);
        });

        Assert.All(symbols, symbol =>
        {
            var stateSymbol = Assert.IsAssignableFrom<IStateSymbol>(symbol);
            Assert.Equal("count", stateSymbol.Name);
            Assert.Equal("Int32", stateSymbol.Type.Name);
        });
        Assert.All(operations, operation =>
        {
            Assert.NotNull(operation);
            Assert.Equal(AkburaOperationKind.MarkupAttribute, operation.Kind);
        });
        Assert.All(diagnostics, item => Assert.Empty(item));
        Assert.All(boundNodes, boundNode => Assert.Equal(BoundKind.MarkupRoot, boundNode.Kind));

        Assert.Same(model.GetSymbolInfo(state).Symbol, model.GetSymbolInfo(state).Symbol);
        Assert.Same(model.GetOperation(attribute), model.GetOperation(attribute));
        Assert.Same(
            model.BindingSession.BindSemanticSyntax(markup),
            model.BindingSession.BindSemanticSyntax(markup));
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
            Microsoft.CodeAnalysis.Location.None);
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

}

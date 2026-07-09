using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
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

public sealed class BoundTreeArchitectureTests : SemanticArchitectureTestBase
{
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

}

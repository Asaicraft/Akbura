using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBuilder
{
    internal const string MarkupForeachAnnotationKind = "AkburaMarkupForeach";
    internal const string MarkupLoopStatementAnnotationKind = "AkburaMarkupLoopStatement";

    internal CSharp.CompilationUnitSyntax CreateMarkupForeachProbe(MarkupForeachStatementSyntax syntax)
    {
        var statement = AnnotateMarkupForeach(syntax)
            .WithStatement(CSharpSyntaxFactory.Block())
            .WithAdditionalAnnotations(new SyntaxAnnotation(MarkupForeachAnnotationKind));
        return CreateStatementProbe(syntax, statement);
    }

    internal CSharpProbeProjection CreateMarkupForeachHeaderProjection(MarkupForeachHeaderSyntax syntax,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(CompletionAnnotationKind);
        var source = CSharpSyntaxFactory.ParseStatement("foreach (" + syntax.Token.ToFullString() + ") {}")
            .WithAdditionalAnnotations(annotation);
        if (source is CSharp.ForEachStatementSyntax parsed && syntax.Parent is MarkupForeachStatementSyntax loop)
        {
            source = parsed.WithAdditionalAnnotations(new SyntaxAnnotation(CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), SymbolKind.CSharpSymbol,
                    parsed.Identifier.ValueText, loop.Header.GetAbsoluteCSharpSpan(
                        loop.Header.GetRawCSharpForeach()?.Identifier.Span ?? default)).Serialize()));
        }
        var root = CreateStatementProbe(syntax, source, includeAllVisibleSymbols: true);
        var projection = CreateProjection(root, source, annotation,
            MarkupForeachHeaderSyntax.CSharpPrefixLength + relativePosition);
        var span = new Microsoft.CodeAnalysis.Text.TextSpan(
            projection.ProjectedSpan.Start + MarkupForeachHeaderSyntax.CSharpPrefixLength,
            syntax.Token.FullSpan.Length);
        return new CSharpProbeProjection(projection.Root, span, span.Start + relativePosition,
            projection.StateNames, projection.ActiveAnnotation, projection.SymbolOrigins);
    }

    internal CSharpProbeProjection CreateMarkupLoopStatementProjection(MarkupCodeStatementSyntax syntax,
        int relativePosition) => CreateStatementProjection(syntax,
            CSharpSyntaxFactory.ParseStatement(syntax.Token.ToFullString()), relativePosition);

    internal static MarkupForeachStatementSyntax? GetContainingMarkupForeach(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (current is MarkupCodeBlockSyntax { Parent: MarkupForeachStatementSyntax loop })
            {
                return loop;
            }
            if (current is MarkupForeachKeyClauseSyntax { Parent: MarkupForeachStatementSyntax keyedLoop })
            {
                return keyedLoop;
            }
        }
        return null;
    }

    internal static string GetMarkupLoopIndexName(MarkupForeachStatementSyntax loop) =>
        "__sourceIndex" + loop.ForeachKeyword.Span.Start.ToString(CultureInfo.InvariantCulture);

    internal static SyntaxNode? GetMarkupLoopCodeGenerationSyntax(CSharpOperationDefinition definition)
    {
        if (definition.Operation is not { } operation)
        {
            return definition.Syntax;
        }
        var identifiers = new Dictionary<Microsoft.CodeAnalysis.Text.TextSpan, string>();
        foreach (var descendant in EnumerateLoopOperations(operation))
        {
            if (descendant is not Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation property ||
                property.Syntax is not CSharp.IdentifierNameSyntax identifier)
            {
                continue;
            }
            foreach (var declaration in property.Property.DeclaringSyntaxReferences)
            {
                foreach (var annotation in declaration.GetSyntax().GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
                {
                    if (CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) &&
                        origin.Kind == SymbolKind.MarkupLoopIndex)
                    {
                        identifiers[identifier.Span] = "__sourceIndex" +
                            origin.DeclarationSpan.Start.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        return identifiers.Count == 0 ? operation.Syntax :
            new BoundMarkupLoopIndexRewriter(identifiers).Visit(operation.Syntax);
    }

    private static IEnumerable<Microsoft.CodeAnalysis.IOperation> EnumerateLoopOperations(Microsoft.CodeAnalysis.IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in EnumerateLoopOperations(child))
            {
                yield return descendant;
            }
        }
    }

    internal static TNode RewriteMarkupLoopIdentifiers<TNode>(AkburaSyntax scope, TNode node)
        where TNode : SyntaxNode
    {
        var loop = GetContainingMarkupForeach(scope);
        if (loop == null)
        {
            return node;
        }
        var generatedName = GetMarkupLoopIndexName(loop);
        return (TNode)new MarkupLoopIndexRewriter(generatedName).Visit(node)!;
    }

    private static CSharp.ForEachStatementSyntax AnnotateMarkupForeach(MarkupForeachStatementSyntax syntax)
    {
        var statement = syntax.Header.GetRawCSharpForeach() ??
            (CSharp.ForEachStatementSyntax)CSharpSyntaxFactory.ParseStatement("foreach (var __invalid in new object[0]) { }");
        return statement.WithAdditionalAnnotations(new SyntaxAnnotation(
            CSharpProbeBinder.ProjectedSymbolAnnotationKind,
            new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), SymbolKind.CSharpSymbol,
                statement.Identifier.ValueText,
                syntax.Header.GetAbsoluteCSharpSpan(statement.Identifier.Span)).Serialize()));
    }

    private static CSharp.StatementSyntax WrapMarkupCodeScope(MarkupCodeBlockSyntax block,
        AkburaSyntax scope, CSharp.StatementSyntax statement)
    {
        var precedingStatements = block.Content
            .Where(content => content.Span.End <= scope.Position)
            .Select(CreateMarkupLoopFlowStatement);
        var body = CSharpSyntaxFactory.Block(precedingStatements.Append(statement));
        if (block.Parent is MarkupForeachStatementSyntax loop)
        {
            return AnnotateMarkupForeach(loop).WithStatement(body);
        }
        if (block.Parent is MarkupCodeIfStatementSyntax conditional)
        {
            var condition = GetCondition(conditional.Condition);
            return ReferenceEquals(block, conditional.Body)
                ? CSharpSyntaxFactory.IfStatement(condition, body)
                : CSharpSyntaxFactory.IfStatement(condition, CSharpSyntaxFactory.Block(),
                    CSharpSyntaxFactory.ElseClause(body));
        }
        return body;
    }

    private static CSharp.StatementSyntax CreateMarkupLoopFlowStatement(MarkupContentSyntax syntax)
    {
        if (syntax is MarkupCodeStatementSyntax code)
        {
            return ParseAnnotatedMarkupLocal(code) ?? code.GetRawCSharpStatement();
        }
        if (syntax is MarkupCodeIfStatementSyntax conditional)
        {
            var body = CSharpSyntaxFactory.Block(conditional.Body.Content.Select(CreateMarkupLoopFlowStatement));
            var alternative = conditional.ElseBody == null ? null : CSharpSyntaxFactory.ElseClause(
                CSharpSyntaxFactory.Block(conditional.ElseBody.Content.Select(CreateMarkupLoopFlowStatement)));
            return CSharpSyntaxFactory.IfStatement(GetCondition(conditional.Condition), body, alternative);
        }
        return CSharpSyntaxFactory.EmptyStatement();
    }

    private static CSharp.StatementSyntax? ParseAnnotatedMarkupLocal(MarkupCodeStatementSyntax syntax)
    {
        if (syntax.GetRawCSharpStatement() is not CSharp.LocalDeclarationStatementSyntax local)
        {
            return null;
        }
        return local.ReplaceNodes(local.Declaration.Variables, (original, _) =>
            original.WithAdditionalAnnotations(new SyntaxAnnotation(CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), SymbolKind.CSharpSymbol,
                    original.Identifier.ValueText,
                    syntax.GetAbsoluteCSharpSpan(original.Identifier.Span)).Serialize())));
    }

    private sealed class MarkupLoopIndexRewriter(string generatedName) : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(CSharp.InvocationExpressionSyntax node)
        {
            if (node.Expression is CSharp.IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
                node.ArgumentList.Arguments.Count == 1 &&
                node.ArgumentList.Arguments[0].Expression is CSharp.IdentifierNameSyntax { Identifier.ValueText: "index" })
            {
                return CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                    CSharpSyntaxFactory.Literal("index")).WithTriviaFrom(node);
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(CSharp.IdentifierNameSyntax node)
        {
            if (node.Identifier.ValueText != "index" || node.Parent is CSharp.MemberAccessExpressionSyntax member &&
                    ReferenceEquals(member.Name, node) || node.Parent is CSharp.MemberBindingExpressionSyntax ||
                node.Parent is CSharp.QualifiedNameSyntax || node.Parent is CSharp.AliasQualifiedNameSyntax ||
                node.Parent is CSharp.NameColonSyntax || node.Parent is CSharp.NameEqualsSyntax)
            {
                return base.VisitIdentifierName(node);
            }
            return node.WithIdentifier(CSharpSyntaxFactory.Identifier(node.Identifier.LeadingTrivia,
                generatedName, node.Identifier.TrailingTrivia));
        }
    }

    private sealed class BoundMarkupLoopIndexRewriter(Dictionary<Microsoft.CodeAnalysis.Text.TextSpan, string> identifiers)
        : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(CSharp.InvocationExpressionSyntax node)
        {
            if (node.Expression is CSharp.IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
                node.ArgumentList.Arguments.Count == 1 &&
                identifiers.ContainsKey(node.ArgumentList.Arguments[0].Expression.Span))
            {
                return CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                    CSharpSyntaxFactory.Literal("index")).WithTriviaFrom(node);
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(CSharp.IdentifierNameSyntax node) =>
            identifiers.TryGetValue(node.Span, out var name)
                ? node.WithIdentifier(CSharpSyntaxFactory.Identifier(node.Identifier.LeadingTrivia, name,
                    node.Identifier.TrailingTrivia))
                : base.VisitIdentifierName(node);
    }
}

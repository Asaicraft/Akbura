using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBuilder
{
    internal const string MarkupConditionAnnotationKind = "AkburaMarkupCondition";

    public CSharp.CompilationUnitSyntax CreateMarkupConditionProbe(CSharpExpressionSyntax syntax,
        CSharp.ExpressionSyntax expression)
    {
        var condition = expression.WithAdditionalAnnotations(new SyntaxAnnotation(MarkupConditionAnnotationKind));
        return CreateStatementProbe(syntax,
            CSharpSyntaxFactory.IfStatement(condition, CSharpSyntaxFactory.Block()));
    }

    public CSharpProbeProjection CreateMarkupConditionProjection(CSharpExpressionSyntax syntax,
        CSharp.ExpressionSyntax expression, int relativePosition)
    {
        var annotation = new SyntaxAnnotation(CompletionAnnotationKind);
        var placeholder = CSharpSyntaxFactory.IdentifierName("__akbura_completion_target")
            .WithAdditionalAnnotations(annotation);
        var statement = CSharpSyntaxFactory.IfStatement(placeholder, CSharpSyntaxFactory.Block());
        var root = CreateStatementProbe(syntax, statement, includeAllVisibleSymbols: true);
        return CreateExpressionProjectionCore(root, AnnotateMarkupCondition(syntax, expression),
            annotation, relativePosition);
    }

    internal static bool IsMarkupCondition(CSharpExpressionSyntax syntax) =>
        syntax.Parent is MarkupIfStatementSyntax or MarkupElseIfClauseSyntax;

    internal static bool HasMarkupConditionalScope(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (current is MarkupBlockSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static CSharp.StatementSyntax WrapMarkupConditionalScopes(AkburaSyntax scope,
        CSharp.StatementSyntax statement)
    {
        for (var current = scope; current != null; current = current.Parent)
        {
            if (current is MarkupBlockSyntax block)
            {
                if (block.Parent is MarkupIfStatementSyntax conditional)
                {
                    statement = CSharpSyntaxFactory.IfStatement(GetCondition(conditional.Condition),
                        CSharpSyntaxFactory.Block(statement));
                }
                else if (block.Parent is MarkupElseIfClauseSyntax clause &&
                    clause.Parent is MarkupIfStatementSyntax chain)
                {
                    statement = CSharpSyntaxFactory.IfStatement(GetCondition(clause.Condition),
                        CSharpSyntaxFactory.Block(statement));
                    statement = WrapPrecedingBranches(chain, clause, statement);
                }
                else if (block.Parent is MarkupElseClauseSyntax finalClause &&
                    finalClause.Parent is MarkupIfStatementSyntax finalChain)
                {
                    statement = WrapPrecedingBranches(finalChain, finalClause,
                        CSharpSyntaxFactory.Block(statement));
                }
            }
            else if (current is CSharpExpressionSyntax &&
                current.Parent is MarkupElseIfClauseSyntax conditionClause &&
                conditionClause.Parent is MarkupIfStatementSyntax conditionChain)
            {
                statement = WrapPrecedingBranches(conditionChain, conditionClause, statement);
            }
        }

        return statement;
    }

    private static CSharp.StatementSyntax WrapPrecedingBranches(MarkupIfStatementSyntax chain,
        AkburaSyntax targetClause, CSharp.StatementSyntax statement)
    {
        for (var i = chain.ElseIfClauses.Count - 1; i >= 0; i--)
        {
            var clause = chain.ElseIfClauses[i];
            if (clause.Position >= targetClause.Position)
            {
                continue;
            }

            statement = CSharpSyntaxFactory.IfStatement(GetCondition(clause.Condition),
                CSharpSyntaxFactory.Block(), CSharpSyntaxFactory.ElseClause(statement));
        }

        return CSharpSyntaxFactory.IfStatement(GetCondition(chain.Condition),
            CSharpSyntaxFactory.Block(), CSharpSyntaxFactory.ElseClause(statement));
    }

    internal static CSharp.ExpressionSyntax ParseMarkupCondition(CSharpExpressionSyntax syntax) =>
        syntax.GetRawCSharpExpression() ?? CSharpSyntaxFactory.ParseExpression(syntax.Tokens.ToFullString());

    private static CSharp.ExpressionSyntax GetCondition(CSharpExpressionSyntax syntax) =>
        AnnotateMarkupCondition(syntax, ParseMarkupCondition(syntax));

    private static CSharp.ExpressionSyntax AnnotateMarkupCondition(CSharpExpressionSyntax syntax,
        CSharp.ExpressionSyntax expression)
    {
        var hostOffset = syntax.Tokens.FullSpan.Start - expression.FullSpan.Start;
        return expression.ReplaceNodes(expression.DescendantNodesAndSelf()
                .OfType<CSharp.SingleVariableDesignationSyntax>(),
            (original, _) => original.WithAdditionalAnnotations(new SyntaxAnnotation(
                CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), Akbura.Language.Symbols.SymbolKind.CSharpSymbol,
                    original.Identifier.ValueText,
                    new TextSpan(hostOffset + original.Identifier.Span.Start, original.Identifier.Span.Length))
                .Serialize())));
    }
}

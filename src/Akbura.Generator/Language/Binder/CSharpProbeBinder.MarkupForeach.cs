using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal (CSharpBindingResult Source, ITypeSymbol? ItemType, CSharpBindingResult Header)
        BindMarkupForeachHeader(MarkupForeachStatementSyntax syntax)
    {
        var tree = CreateSyntaxTree(new CSharpProbeBuilder(this).CreateMarkupForeachProbe(syntax));
        var semantic = CreateSemanticModel(tree);
        var statement = tree.GetRoot().GetAnnotatedNodes(CSharpProbeBuilder.MarkupForeachAnnotationKind)
            .OfType<CSharp.ForEachStatementSyntax>().Single();
        var item = semantic.GetDeclaredSymbol(statement) as ILocalSymbol;
        return (BindExpression(semantic, statement.Expression, isBindingPath: false), item?.Type,
            BindStatement(semantic, statement, item));
    }

    internal BoundStatement BindMarkupLoopStatement(MarkupCodeStatementSyntax syntax)
    {
        var source = syntax.GetRawCSharpStatement()
            .WithAdditionalAnnotations(new SyntaxAnnotation(CSharpProbeBuilder.MarkupLoopStatementAnnotationKind));
        var tree = CreateSyntaxTree(new CSharpProbeBuilder(this).CreateStatementProbe(syntax, source));
        var semantic = CreateSemanticModel(tree);
        var statement = tree.GetRoot().GetAnnotatedNodes(CSharpProbeBuilder.MarkupLoopStatementAnnotationKind)
            .OfType<CSharp.StatementSyntax>().Single();
        return BindStatementTree(syntax, semantic, statement, isBindingPath: false);
    }

    internal void AddMarkupLoopProbeMembers(AkburaSyntax syntax,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> members)
    {
        var loop = CSharpProbeBuilder.GetContainingMarkupForeach(syntax);
        if (loop == null)
        {
            return;
        }
        // A getter-only property provides readonly semantics without altering active projection text.
        // Each probe describes one active fragment and annotates this symbol with its nearest loop.
        members.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(CSharpSyntaxKind.IntKeyword)), "index")
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(new SyntaxAnnotation(ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), Symbols.SymbolKind.MarkupLoopIndex,
                    "index", loop.ForeachKeyword.Span).Serialize())));
    }
}

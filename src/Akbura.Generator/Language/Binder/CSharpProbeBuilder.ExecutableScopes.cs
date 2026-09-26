using System;
using System.Linq;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBuilder
{
    /// <summary>
    /// Keep a target inside its original executable header. In particular, an
    /// out-variable or pattern variable is declared by that header, not by a
    /// fabricated default-valued local in the probe method.
    /// </summary>
    private static CSharp.StatementSyntax WrapExecutableBlockScope(
        CSharpBlockSyntax block, AkburaSyntax target, CSharp.StatementSyntax statement)
    {
        var child = target;
        while (child.Parent != null && !ReferenceEquals(child.Parent, block))
        {
            child = child.Parent;
        }

        using var preceding = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        AddPrecedingLocalDeclarationsFromList(
            block.Tokens,
            child,
            preceding,
            includeLocalFunctions: true);
        preceding.Add(statement);
        var body = CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(preceding.ToImmutable()));

        if (block.Parent is not CSharpStatementSyntax owner ||
            owner.GetRawCSharpStatement() is not { } header)
        {
            return body;
        }

        var hostOffset = owner.Tokens.FullSpan.Start - header.FullSpan.Start;
        var declarations = header.DescendantNodesAndSelf()
            .Where(node => node is CSharp.SingleVariableDesignationSyntax or CSharp.VariableDeclaratorSyntax);
        header = header.ReplaceNodes(declarations, (original, rewritten) =>
        {
            var identifier = GetCSharpDeclarationIdentifier(original);
            return rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(
                CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"),
                    Akbura.Language.Symbols.SymbolKind.CSharpSymbol, identifier.ValueText,
                    new TextSpan(hostOffset + identifier.Span.Start, identifier.Span.Length)).Serialize()));
        });
        if (header is CSharp.ForEachStatementSyntax forEach)
        {
            header = forEach.WithAdditionalAnnotations(new SyntaxAnnotation(
                CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"),
                    Akbura.Language.Symbols.SymbolKind.CSharpSymbol, forEach.Identifier.ValueText,
                    new TextSpan(hostOffset + forEach.Identifier.Span.Start, forEach.Identifier.Span.Length)).Serialize()));
        }

        // Local functions use ApplyContainingMethodContext; do not nest a
        // synthetic local function around the probe target.
        return header switch
        {
            CSharp.IfStatementSyntax conditional => conditional.WithStatement(body),
            CSharp.WhileStatementSyntax loop => loop.WithStatement(body),
            CSharp.ForStatementSyntax loop => loop.WithStatement(body),
            CSharp.ForEachStatementSyntax loop => loop.WithStatement(body),
            CSharp.ForEachVariableStatementSyntax loop => loop.WithStatement(body),
            CSharp.UsingStatementSyntax usingStatement => usingStatement.WithStatement(body),
            CSharp.LockStatementSyntax lockStatement => lockStatement.WithStatement(body),
            CSharp.FixedStatementSyntax fixedStatement => fixedStatement.WithStatement(body),
            CSharp.CheckedStatementSyntax checkedStatement => checkedStatement.WithBlock(body),
            CSharp.UnsafeStatementSyntax unsafeStatement => unsafeStatement.WithBlock(body),
            CSharp.LocalFunctionStatementSyntax localFunction when owner.Parent is not AkburaDocumentSyntax =>
                CreateProjectedLocalFunction(owner, localFunction, body),
            _ => body,
        };
    }
}

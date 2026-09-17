using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(MarkupForeachHeaderSyntax syntax) =>
        GetMarkupLoopFragmentReferences(syntax, syntax.Token.FullSpan,
            SyntaxFactory.ParseStatement("foreach (" + syntax.Token.ToFullString() + ") {}"),
            CreateCSharpCompletionProjection(syntax, 0), MarkupForeachHeaderSyntax.CSharpPrefixLength);

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(MarkupCodeStatementSyntax syntax) =>
        GetMarkupLoopFragmentReferences(syntax, syntax.Token.FullSpan,
            SyntaxFactory.ParseStatement(syntax.Token.ToFullString()), CreateCSharpCompletionProjection(syntax, 0), 0);

    private ImmutableArray<CSharpSymbolReference> GetMarkupLoopFragmentReferences(AkburaSyntax syntax,
        Microsoft.CodeAnalysis.Text.TextSpan hostSpan, Microsoft.CodeAnalysis.SyntaxNode source,
        CSharpProbeProjection projection, int sourcePrefixLength)
    {
        var semanticModel = CreateReferenceProbeSemanticModel(projection.Root, out var syntaxTree);
        var projected = syntaxTree.GetRoot().GetAnnotatedNodes(projection.ActiveAnnotation).Single();
        var symbolsByName = new Dictionary<string, Symbols.ISymbol>(StringComparer.Ordinal);
        var symbolsByCommandTypeName = new Dictionary<string, Symbols.ISymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(symbolsByName, symbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(syntax, symbolsByName);
        var references = CollectCSharpSymbolReferences(semanticModel,
            [new CSharpReferenceTarget(source, projected, hostSpan.Start - sourcePrefixLength)],
            symbolsByName, symbolsByCommandTypeName);
        references = references.Where(reference => hostSpan.Contains(reference.SourceSpan)).ToImmutableArray();
        if (syntax is MarkupForeachHeaderSyntax header && projected is
                Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax projectedLoop &&
            semanticModel.GetDeclaredSymbol(projectedLoop) is { } item && header.GetRawCSharpForeach() is { } rawLoop)
        {
            references = references.Add(new CSharpSymbolReference(SyntaxFactory.IdentifierName(rawLoop.Identifier),
                header.GetAbsoluteCSharpSpan(rawLoop.Identifier.Span), new Symbols.CSharpSymbolDefinition(item),
                akburaSymbol: null, rawLoop.Identifier.ValueText));
        }
        return references;
    }
}

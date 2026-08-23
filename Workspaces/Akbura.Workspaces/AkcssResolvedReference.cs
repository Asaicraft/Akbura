using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Text;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Workspaces;

internal readonly struct AkcssResolvedReference
{
    public AkcssResolvedReference(
        AkcssReferenceKind kind,
        TextSpan sourceSpan,
        AkburaSymbol? symbol,
        CSharpSymbolDefinition csharpDefinition)
    {
        Kind = kind;
        SourceSpan = sourceSpan;
        Symbol = symbol;
        CSharpDefinition = csharpDefinition;
    }

    public AkcssReferenceKind Kind { get; }

    public TextSpan SourceSpan { get; }

    public AkburaSymbol? Symbol { get; }

    public CSharpSymbolDefinition CSharpDefinition { get; }
}

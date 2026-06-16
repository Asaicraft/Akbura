using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Symbols;

internal readonly struct CSharpSymbolReference
{
    public CSharpSymbolReference(
        CSharp.ExpressionSyntax syntax,
        CSharpSymbolDefinition csharpDefinition,
        ISymbol? akburaSymbol,
        string? name = null)
    {
        Syntax = syntax;
        CSharpDefinition = csharpDefinition;
        AkburaSymbol = akburaSymbol;
        Name = string.IsNullOrWhiteSpace(name) ? csharpDefinition.Name : name!;
    }

    public CSharp.ExpressionSyntax Syntax { get; }

    public TextSpan Span => Syntax.Span;

    public string Name { get; }

    public CSharpSymbolDefinition CSharpDefinition { get; }

    public ISymbol? AkburaSymbol { get; }

    public bool IsAkburaSymbol => AkburaSymbol != null;
}

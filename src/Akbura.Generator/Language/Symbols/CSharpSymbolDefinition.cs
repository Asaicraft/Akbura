using Microsoft.CodeAnalysis;
using RoslynISymbol = Microsoft.CodeAnalysis.ISymbol;
using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;

namespace Akbura.Language.Symbols;

internal readonly struct CSharpSymbolDefinition
{
    public CSharpSymbolDefinition(RoslynISymbol symbol)
    {
        Symbol = symbol ?? throw new System.ArgumentNullException(nameof(symbol));
    }

    public RoslynISymbol? Symbol { get; }

    public bool IsDefault => Symbol == null;

    public RoslynSymbolKind? Kind => Symbol?.Kind;

    public string Name => Symbol?.Name ?? string.Empty;

    public string MetadataName => Symbol?.MetadataName ?? string.Empty;

    public INamedTypeSymbol? NamedType => Symbol as INamedTypeSymbol;

    public string ToDisplayString(SymbolDisplayFormat? format = null)
    {
        return Symbol?.ToDisplayString(format) ?? string.Empty;
    }

    public bool Equals(CSharpSymbolDefinition other)
    {
        return SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);
    }

    public override bool Equals(object? obj)
    {
        return obj is CSharpSymbolDefinition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Symbol == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(Symbol);
    }
}

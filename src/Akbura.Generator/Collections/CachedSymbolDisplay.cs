using Microsoft.CodeAnalysis;
using System;
using System.Runtime.CompilerServices;

namespace Akbura.Collections;

/// <summary>
/// Reuses fully qualified names without extending the lifetime of type symbols.
/// </summary>
internal static class CachedSymbolDisplay
{
    private static readonly ConditionalWeakTable<ITypeSymbol, string> s_fullyQualifiedNames = new();

    public static string GetFullyQualifiedName(ITypeSymbol symbol)
    {
        if (symbol is null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        return s_fullyQualifiedNames.GetValue(symbol,
            static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}

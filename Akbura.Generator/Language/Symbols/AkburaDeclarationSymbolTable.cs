using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Symbols;

internal sealed class AkburaDeclarationSymbolTable
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfos = new();
    private readonly Dictionary<DeclaredSymbolsKey, ImmutableArray<AkburaSymbol>> _declaredSymbols = new();

    public AkburaDeclarationSymbolTable(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public AkburaSymbolInfo GetSymbolInfo(AkburaDeclaration declaration)
    {
        var syntax = declaration.Syntax;
        if (_symbolInfos.TryGetValue(syntax, out var symbolInfo))
        {
            return symbolInfo;
        }

        symbolInfo = _semanticModel.CreateDeclarationSymbolInfo(declaration);
        _symbolInfos[syntax] = symbolInfo;
        return symbolInfo;
    }

    public ImmutableArray<AkburaSymbol> GetDeclaredSymbols(
        AkburaDeclaration declaration,
        params AkburaDeclarationKind[] allowedKinds)
    {
        var key = new DeclaredSymbolsKey(declaration.Syntax, GetKindMask(allowedKinds));
        if (_declaredSymbols.TryGetValue(key, out var symbols))
        {
            return symbols;
        }

        symbols = CreateDeclaredSymbols(declaration, key.KindMask);
        _declaredSymbols[key] = symbols;
        return symbols;
    }

    public bool TryGetDeclaredSymbol(
        AkburaDeclaration declaration,
        out AkburaSymbol symbol)
    {
        var symbolInfo = GetSymbolInfo(declaration);
        if (symbolInfo.Symbol != null)
        {
            symbol = symbolInfo.Symbol;
            return true;
        }

        symbol = null!;
        return false;
    }

    public ImmutableArray<AkburaSymbol> GetTailwindUtilityParameters(AkburaDeclaration utilityDeclaration)
    {
        if (GetSymbolInfo(utilityDeclaration).Symbol is not ITailwindUtilitySymbol utility ||
            utility.Parameters.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        return ImmutableArray<AkburaSymbol>.CastUp(utility.Parameters);
    }

    private ImmutableArray<AkburaSymbol> CreateDeclaredSymbols(
        AkburaDeclaration declaration,
        ulong allowedKindMask)
    {
        if (declaration.Children.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AkburaSymbol>();
        foreach (var child in declaration.Children)
        {
            if (CanCreateDeclaredSymbol(child.Kind) &&
                IsAllowed(child.Kind, allowedKindMask) &&
                TryGetDeclaredSymbol(child, out var symbol))
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    private static bool CanCreateDeclaredSymbol(AkburaDeclarationKind declarationKind)
    {
        return declarationKind is
            AkburaDeclarationKind.State or
            AkburaDeclarationKind.Parameter or
            AkburaDeclarationKind.InjectedService or
            AkburaDeclarationKind.Command or
            AkburaDeclarationKind.UseEffect or
            AkburaDeclarationKind.UserHook or
            AkburaDeclarationKind.AkcssModule or
            AkburaDeclarationKind.AkcssStyle or
            AkburaDeclarationKind.AkcssUtility;
    }

    private static bool IsAllowed(
        AkburaDeclarationKind declarationKind,
        ulong allowedKindMask)
    {
        return allowedKindMask == 0 ||
               (allowedKindMask & GetKindBit(declarationKind)) != 0;
    }

    private static ulong GetKindMask(AkburaDeclarationKind[] allowedKinds)
    {
        var mask = 0UL;
        foreach (var kind in allowedKinds)
        {
            mask |= GetKindBit(kind);
        }

        return mask;
    }

    private static ulong GetKindBit(AkburaDeclarationKind declarationKind)
    {
        var shift = (int)declarationKind;
        return shift is >= 0 and < 64
            ? 1UL << shift
            : 0;
    }

    private readonly struct DeclaredSymbolsKey : IEquatable<DeclaredSymbolsKey>
    {
        public DeclaredSymbolsKey(AkburaSyntax syntax, ulong kindMask)
        {
            Syntax = syntax;
            KindMask = kindMask;
        }

        public AkburaSyntax Syntax { get; }

        public ulong KindMask { get; }

        public bool Equals(DeclaredSymbolsKey other)
        {
            return ReferenceEquals(Syntax, other.Syntax) &&
                   KindMask == other.KindMask;
        }

        public override bool Equals(object? obj)
        {
            return obj is DeclaredSymbolsKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeHelpers.GetHashCode(Syntax) * 397) ^
                       KindMask.GetHashCode();
            }
        }
    }
}

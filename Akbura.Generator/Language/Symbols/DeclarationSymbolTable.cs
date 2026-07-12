using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Symbols;

internal sealed class DeclarationSymbolTable
{
    private readonly AkburaSemanticModel _semanticModel;
    private ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfos =
        ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo>.Empty;
    private ImmutableDictionary<DeclaredSymbolsKey, ImmutableArray<AkburaSymbol>> _declaredSymbols =
        ImmutableDictionary<DeclaredSymbolsKey, ImmutableArray<AkburaSymbol>>.Empty;

    public DeclarationSymbolTable(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public AkburaSymbolInfo GetSymbolInfo(Declaration declaration)
    {
        if (declaration is not SingleDeclaration singleDeclaration)
        {
            return AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax);
        }

        return ImmutableInterlocked.GetOrAdd(
            ref _symbolInfos,
            singleDeclaration.Syntax,
            static (_, arg) => arg.Table._semanticModel.CreateDeclarationSymbolInfo(arg.Declaration),
            (Table: this, Declaration: declaration));
    }

    public ImmutableArray<AkburaSymbol> GetDeclaredSymbols(
        Declaration declaration,
        params DeclarationKind[] allowedKinds)
    {
        if (declaration is not SingleDeclaration singleDeclaration)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        var key = new DeclaredSymbolsKey(singleDeclaration.Syntax, GetKindMask(allowedKinds));
        return ImmutableInterlocked.GetOrAdd(
            ref _declaredSymbols,
            key,
            static (cacheKey, arg) => arg.Table.CreateDeclaredSymbols(
                arg.Declaration,
                cacheKey.KindMask),
            (Table: this, Declaration: declaration));
    }

    public bool TryGetDeclaredSymbol(
        Declaration declaration,
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

    public ImmutableArray<AkburaSymbol> GetTailwindUtilityParameters(Declaration utilityDeclaration)
    {
        if (GetSymbolInfo(utilityDeclaration).Symbol is not ITailwindUtilitySymbol utility ||
            utility.Parameters.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        return ImmutableArray<AkburaSymbol>.CastUp(utility.Parameters);
    }

    private ImmutableArray<AkburaSymbol> CreateDeclaredSymbols(
        Declaration declaration,
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

    private static bool CanCreateDeclaredSymbol(DeclarationKind declarationKind)
    {
        return declarationKind is
            DeclarationKind.State or
            DeclarationKind.Parameter or
            DeclarationKind.InjectedService or
            DeclarationKind.Command or
            DeclarationKind.UseEffect or
            DeclarationKind.UserHook or
            DeclarationKind.AkcssModule or
            DeclarationKind.AkcssStyle or
            DeclarationKind.AkcssUtility;
    }

    private static bool IsAllowed(
        DeclarationKind declarationKind,
        ulong allowedKindMask)
    {
        return allowedKindMask == 0 ||
               (allowedKindMask & GetKindBit(declarationKind)) != 0;
    }

    private static ulong GetKindMask(DeclarationKind[] allowedKinds)
    {
        var mask = 0UL;
        foreach (var kind in allowedKinds)
        {
            mask |= GetKindBit(kind);
        }

        return mask;
    }

    private static ulong GetKindBit(DeclarationKind declarationKind)
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
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Syntax), KindMask);
        }
    }
}

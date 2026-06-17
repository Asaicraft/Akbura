using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class UserHookSymbol : Symbol, IUserHookSymbol
{
    public UserHookSymbol(
        string invocationName,
        INamedTypeSymbol hookType,
        IMethodSymbol useHookMethod,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(invocationName))
        {
            throw new ArgumentException("Hook invocation name cannot be empty.", nameof(invocationName));
        }

        InvocationName = invocationName;
        HookType = new CSharpSymbolDefinition(hookType ?? throw new ArgumentNullException(nameof(hookType)));
        UseHookMethod = new CSharpSymbolDefinition(useHookMethod ?? throw new ArgumentNullException(nameof(useHookMethod)));
        ReturnType = new CSharpSymbolDefinition(useHookMethod.ReturnType);
    }

    public override SymbolKind Kind => SymbolKind.UserHook;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name => InvocationName;

    public override string MetadataName => HookType.MetadataName;

    public override CSharpSymbolDefinition CSharpDefinition => HookType;

    public string InvocationName { get; }

    public CSharpSymbolDefinition HookType { get; }

    public CSharpSymbolDefinition UseHookMethod { get; }

    public CSharpSymbolDefinition ReturnType { get; }

    public override string ToDisplayString()
    {
        return $"{InvocationName} -> {HookType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
    }
}

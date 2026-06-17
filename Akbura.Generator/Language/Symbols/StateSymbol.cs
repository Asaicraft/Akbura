using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class StateSymbol : Symbol, IStateSymbol
{
    public StateSymbol(
        StateDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition type,
        CSharpSymbolDefinition initializerType,
        IUserHookSymbol? userHook,
        bool hasExplicitType,
        StateBindingKind bindingKind,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        Type = type;
        InitializerType = initializerType;
        UserHook = userHook;
        HasExplicitType = hasExplicitType;
        BindingKind = bindingKind;
    }

    public override SymbolKind Kind => SymbolKind.State;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public StateDeclarationSyntax DeclarationSyntax { get; }

    public StateInitializerSyntax InitializerSyntax => DeclarationSyntax.Initializer;

    public CSharpExpressionSyntax InitializerExpression => InitializerSyntax.Expression;

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition InitializerType { get; }

    public IUserHookSymbol? UserHook { get; }

    public bool HasExplicitType { get; }

    public bool IsBindable => BindingKind != StateBindingKind.None;

    public bool IsReadOnly => BindingKind == StateBindingKind.Out;

    public StateBindingKind BindingKind { get; }

    public override string ToDisplayString()
    {
        return !Type.IsDefault ? $"state {Type.Name} {Name}" : $"state {Name}";
    }
}

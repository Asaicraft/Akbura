using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class CommandParameterSymbol : Symbol, ICommandParameterSymbol
{
    public CommandParameterSymbol(
        string name,
        int ordinal,
        CSharpSymbolDefinition type,
        IParameterSymbol? csharpParameter = null,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command parameter symbol name cannot be empty.", nameof(name));
        }

        Name = name;
        Ordinal = ordinal;
        Type = type;
        CSharpParameter = csharpParameter;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public int Ordinal { get; }

    public CSharpSymbolDefinition Type { get; }

    public IParameterSymbol? CSharpParameter { get; }

    public override CSharpSymbolDefinition CSharpDefinition => CSharpParameter == null
        ? default
        : new CSharpSymbolDefinition(CSharpParameter);

    public override string ToDisplayString()
    {
        return !Type.IsDefault ? $"{Type.Name} {Name}" : Name;
    }
}

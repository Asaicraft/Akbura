using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class ParamSymbol : Symbol, IParamSymbol
{
    public ParamSymbol(
        ParamDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition type,
        CSharpSymbolDefinition defaultValueType,
        bool hasExplicitType,
        ParamBindingKind bindingKind,
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
            throw new ArgumentException("Parameter symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        Type = type;
        DefaultValueType = defaultValueType;
        HasExplicitType = hasExplicitType;
        BindingKind = bindingKind;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public ParamDeclarationSyntax DeclarationSyntax { get; }

    public ParamBindingKind BindingKind { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition DefaultValueType { get; }

    public bool HasExplicitType { get; }

    public bool HasDefaultValue => DeclarationSyntax.DefaultValue != null;

    public CSharpExpressionSyntax? DefaultValueSyntax => DeclarationSyntax.DefaultValue;

    public bool ReceivesValueFromParent => BindingKind is ParamBindingKind.Default or ParamBindingKind.Bind;

    public bool SendsValueToParent => BindingKind is ParamBindingKind.Bind or ParamBindingKind.Out;

    public bool IsTwoWayBinding => BindingKind == ParamBindingKind.Bind;

    public override string ToDisplayString()
    {
        var bindingText = BindingKind switch
        {
            ParamBindingKind.Bind => " bind",
            ParamBindingKind.Out => " out",
            _ => string.Empty,
        };

        return !Type.IsDefault
            ? $"param{bindingText} {Type.Name} {Name}"
            : $"param{bindingText} {Name}";
    }
}

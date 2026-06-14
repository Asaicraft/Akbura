using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language.Symbols;

internal sealed class PropertySymbol : Symbol, IPropertySymbol
{
    public PropertySymbol(
        string name,
        CSharpSymbolDefinition type,
        CSharpSymbolDefinition avaloniaPropertyDefinition = default,
        CSharpSymbolDefinition clrPropertyDefinition = default,
        IParamSymbol? parameter = null,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Property symbol name cannot be empty.", nameof(name));
        }

        Name = name;
        Type = type;
        AvaloniaPropertyDefinition = avaloniaPropertyDefinition;
        ClrPropertyDefinition = clrPropertyDefinition;
        Parameter = parameter;
    }

    public override SymbolKind Kind => SymbolKind.Property;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition AvaloniaPropertyDefinition { get; }

    public CSharpSymbolDefinition ClrPropertyDefinition { get; }

    public IParamSymbol? Parameter { get; }

    public bool IsAvaloniaProperty => !AvaloniaPropertyDefinition.IsDefault;

    public bool IsClrProperty => !ClrPropertyDefinition.IsDefault;

    public bool IsParameter => Parameter != null;

    public bool CanRead => Parameter != null ||
        IsAvaloniaProperty ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { GetMethod.DeclaredAccessibility: Accessibility.Public };

    public bool CanWrite => Parameter != null ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public } ||
        (ClrPropertyDefinition.IsDefault && IsAvaloniaProperty);

    public override CSharpSymbolDefinition CSharpDefinition => !ClrPropertyDefinition.IsDefault
        ? ClrPropertyDefinition
        : AvaloniaPropertyDefinition;

    public override string ToDisplayString()
    {
        if (!Type.IsDefault)
        {
            return $"{Name}: {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
        }

        return Name;
    }
}

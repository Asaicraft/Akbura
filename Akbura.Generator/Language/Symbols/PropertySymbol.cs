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
        ICommandSymbol? command = null,
        SymbolLanguage language = SymbolLanguage.Markup,
        ISymbol? containingSymbol = null,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
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
        Command = command;
        Language = language;
    }

    public override SymbolKind Kind => SymbolKind.Property;

    public override SymbolLanguage Language { get; }

    public override string Name { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition AvaloniaPropertyDefinition { get; }

    public CSharpSymbolDefinition ClrPropertyDefinition { get; }

    public IParamSymbol? Parameter { get; }

    public ICommandSymbol? Command { get; }

    public bool IsAvaloniaProperty => !AvaloniaPropertyDefinition.IsDefault;

    public bool IsClrProperty => !ClrPropertyDefinition.IsDefault;

    public bool IsParameter => Parameter != null;

    public bool IsCommand => Command != null;

    public bool CanRead => Parameter?.SendsValueToParent == true ||
        Command != null ||
        IsAvaloniaProperty ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { GetMethod.DeclaredAccessibility: Accessibility.Public };

    public bool CanWrite => Parameter?.ReceivesValueFromParent == true ||
        Command != null ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public } ||
        (ClrPropertyDefinition.IsDefault && IsAvaloniaProperty);

    public override CSharpSymbolDefinition CSharpDefinition => !ClrPropertyDefinition.IsDefault
        ? ClrPropertyDefinition
        : AvaloniaPropertyDefinition;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitProperty(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitProperty(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitProperty(this, parameter);
    }

    public override string ToDisplayString()
    {
        if (!Type.IsDefault)
        {
            return $"{Name}: {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
        }

        return Name;
    }
}

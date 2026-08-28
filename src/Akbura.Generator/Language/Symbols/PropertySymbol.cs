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
        CSharpSymbolDefinition attachedPropertyDefinition = default,
        CSharpSymbolDefinition attachedGetterDefinition = default,
        CSharpSymbolDefinition attachedSetterDefinition = default,
        CSharpSymbolDefinition attachedTargetType = default,
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
        AttachedPropertyDefinition = attachedPropertyDefinition;
        AttachedGetterDefinition = attachedGetterDefinition;
        AttachedSetterDefinition = attachedSetterDefinition;
        AttachedTargetType = attachedTargetType;
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

    public CSharpSymbolDefinition AttachedPropertyDefinition { get; }

    public CSharpSymbolDefinition AttachedGetterDefinition { get; }

    public CSharpSymbolDefinition AttachedSetterDefinition { get; }

    public CSharpSymbolDefinition AttachedTargetType { get; }

    public CSharpSymbolDefinition ClrPropertyDefinition { get; }

    public PropertyAccessKind ReadKind => GetReadKind();

    public CSharpSymbolDefinition ReadDefinition => ReadKind switch
    {
        PropertyAccessKind.AttachedAccessor => AttachedGetterDefinition,
        PropertyAccessKind.ClrProperty => ClrPropertyDefinition,
        PropertyAccessKind.AvaloniaProperty => !AvaloniaPropertyDefinition.IsDefault
            ? AvaloniaPropertyDefinition
            : AttachedPropertyDefinition,
        _ => default,
    };

    public PropertyAccessKind WriteKind => GetWriteKind();

    public CSharpSymbolDefinition WriteDefinition => WriteKind switch
    {
        PropertyAccessKind.AttachedAccessor => AttachedSetterDefinition,
        PropertyAccessKind.ClrProperty => ClrPropertyDefinition,
        PropertyAccessKind.AvaloniaProperty => !AvaloniaPropertyDefinition.IsDefault
            ? AvaloniaPropertyDefinition
            : AttachedPropertyDefinition,
        _ => default,
    };

    public IParamSymbol? Parameter { get; }

    public ICommandSymbol? Command { get; }

    public bool IsAvaloniaProperty => !AvaloniaPropertyDefinition.IsDefault;

    public bool IsAttachedProperty => !AttachedPropertyDefinition.IsDefault;

    public bool IsClrProperty => !ClrPropertyDefinition.IsDefault;

    public bool IsParameter => Parameter != null;

    public bool IsCommand => Command != null;

    public bool CanRead => Parameter?.SendsValueToParent == true ||
        Command != null ||
        !AttachedGetterDefinition.IsDefault ||
        IsAvaloniaProperty ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { GetMethod.DeclaredAccessibility: Accessibility.Public };

    public bool CanWrite => Parameter?.ReceivesValueFromParent == true ||
        Command != null ||
        !AttachedSetterDefinition.IsDefault ||
        ClrPropertyDefinition.Symbol is RoslynPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public } ||
        (ClrPropertyDefinition.IsDefault && IsAvaloniaProperty);

    private PropertyAccessKind GetReadKind()
    {
        if (!AttachedGetterDefinition.IsDefault)
        {
            return PropertyAccessKind.AttachedAccessor;
        }

        if (IsAvaloniaProperty)
        {
            return PropertyAccessKind.AvaloniaProperty;
        }

        if (ClrPropertyDefinition.Symbol is RoslynPropertySymbol { GetMethod.DeclaredAccessibility: Accessibility.Public })
        {
            return PropertyAccessKind.ClrProperty;
        }

        if (Parameter?.SendsValueToParent == true)
        {
            return PropertyAccessKind.Parameter;
        }

        return Command != null
            ? PropertyAccessKind.Command
            : PropertyAccessKind.None;
    }

    private PropertyAccessKind GetWriteKind()
    {
        if (!AttachedSetterDefinition.IsDefault)
        {
            return PropertyAccessKind.AttachedAccessor;
        }

        if (IsAvaloniaProperty &&
            (ClrPropertyDefinition.IsDefault ||
             ClrPropertyDefinition.Symbol is RoslynPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public }))
        {
            return PropertyAccessKind.AvaloniaProperty;
        }

        if (ClrPropertyDefinition.Symbol is RoslynPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public })
        {
            return PropertyAccessKind.ClrProperty;
        }

        if (Parameter?.ReceivesValueFromParent == true)
        {
            return PropertyAccessKind.Parameter;
        }

        return Command != null
            ? PropertyAccessKind.Command
            : PropertyAccessKind.None;
    }

    public override CSharpSymbolDefinition CSharpDefinition => !ClrPropertyDefinition.IsDefault
        ? ClrPropertyDefinition
        : !AvaloniaPropertyDefinition.IsDefault
            ? AvaloniaPropertyDefinition
            : AttachedPropertyDefinition;

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

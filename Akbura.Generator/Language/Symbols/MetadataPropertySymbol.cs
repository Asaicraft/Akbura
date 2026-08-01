using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MetadataPropertySymbol : Symbol, IPropertySymbol
{
    private MetadataPropertySymbol(
        ISymbol containingSymbol,
        string name,
        CSharpSymbolDefinition type,
        PropertyAccessKind accessKind,
        CSharpSymbolDefinition avaloniaPropertyDefinition,
        CSharpSymbolDefinition attachedPropertyDefinition,
        CSharpSymbolDefinition attachedGetterDefinition,
        CSharpSymbolDefinition attachedSetterDefinition,
        CSharpSymbolDefinition attachedTargetType,
        CSharpSymbolDefinition clrPropertyDefinition,
        bool canRead,
        bool canWrite,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations)
        : base(
            containingSymbol,
            locations,
            isImplicitlyDeclared: true)
    {
        Name = name;
        Type = type;
        AccessKind = accessKind;
        AvaloniaPropertyDefinition = avaloniaPropertyDefinition;
        AttachedPropertyDefinition = attachedPropertyDefinition;
        AttachedGetterDefinition = attachedGetterDefinition;
        AttachedSetterDefinition = attachedSetterDefinition;
        AttachedTargetType = attachedTargetType;
        ClrPropertyDefinition = clrPropertyDefinition;
        CanRead = canRead;
        CanWrite = canWrite;
    }

    private PropertyAccessKind AccessKind { get; }

    public override SymbolKind Kind => SymbolKind.Property;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition AvaloniaPropertyDefinition { get; }

    public CSharpSymbolDefinition AttachedPropertyDefinition { get; }

    public CSharpSymbolDefinition AttachedGetterDefinition { get; }

    public CSharpSymbolDefinition AttachedSetterDefinition { get; }

    public CSharpSymbolDefinition AttachedTargetType { get; }

    public CSharpSymbolDefinition ClrPropertyDefinition { get; }

    public PropertyAccessKind ReadKind => CanRead ? AccessKind : PropertyAccessKind.None;

    public CSharpSymbolDefinition ReadDefinition => GetDefinition(ReadKind, forWrite: false);

    public PropertyAccessKind WriteKind => CanWrite ? AccessKind : PropertyAccessKind.None;

    public CSharpSymbolDefinition WriteDefinition => GetDefinition(WriteKind, forWrite: true);

    public IParamSymbol? Parameter => null;

    public ICommandSymbol? Command => null;

    public bool IsAvaloniaProperty => AccessKind == PropertyAccessKind.AvaloniaProperty;

    public bool IsAttachedProperty => AccessKind == PropertyAccessKind.AttachedAccessor;

    public bool IsClrProperty => AccessKind == PropertyAccessKind.ClrProperty;

    public bool IsParameter => AccessKind == PropertyAccessKind.Parameter;

    public bool IsCommand => AccessKind == PropertyAccessKind.Command;

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public override CSharpSymbolDefinition CSharpDefinition => !WriteDefinition.IsDefault
        ? WriteDefinition
        : ReadDefinition;

    public static MetadataPropertySymbol? Create(
        ISymbol containingSymbol,
        string? name,
        ITypeSymbol? type,
        PropertyAccessKind accessKind,
        string? avaloniaPropertyName,
        string? attachedGetterName,
        string? attachedSetterName,
        ITypeSymbol? ownerType,
        ITypeSymbol? attachedTargetType,
        bool canRead,
        bool canWrite,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations)
    {
        if (string.IsNullOrWhiteSpace(name) || type == null)
        {
            return null;
        }

        var owner = ownerType as INamedTypeSymbol;
        var avaloniaProperty = FindMember<IFieldSymbol>(owner, avaloniaPropertyName);
        var clrProperty = FindMember<Microsoft.CodeAnalysis.IPropertySymbol>(owner, name);
        var attachedGetter = FindMethod(owner, attachedGetterName, parameterCount: 1);
        var attachedSetter = FindMethod(owner, attachedSetterName, parameterCount: 2);

        return new MetadataPropertySymbol(
            containingSymbol,
            name!,
            new CSharpSymbolDefinition(type),
            accessKind,
            accessKind == PropertyAccessKind.AvaloniaProperty && avaloniaProperty != null
                ? new CSharpSymbolDefinition(avaloniaProperty)
                : default,
            accessKind == PropertyAccessKind.AttachedAccessor && avaloniaProperty != null
                ? new CSharpSymbolDefinition(avaloniaProperty)
                : default,
            attachedGetter == null
                ? default
                : new CSharpSymbolDefinition(attachedGetter),
            attachedSetter == null
                ? default
                : new CSharpSymbolDefinition(attachedSetter),
            attachedTargetType == null
                ? default
                : new CSharpSymbolDefinition(attachedTargetType),
            clrProperty == null
                ? default
                : new CSharpSymbolDefinition(clrProperty),
            canRead,
            canWrite,
            locations);
    }

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
        return Type.IsDefault
            ? Name
            : $"{Name}: {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
    }

    private CSharpSymbolDefinition GetDefinition(
        PropertyAccessKind kind,
        bool forWrite)
    {
        return kind switch
        {
            PropertyAccessKind.ClrProperty => ClrPropertyDefinition,
            PropertyAccessKind.AvaloniaProperty => AvaloniaPropertyDefinition,
            PropertyAccessKind.AttachedAccessor => forWrite
                ? AttachedSetterDefinition
                : AttachedGetterDefinition,
            _ => default,
        };
    }

    private static TSymbol? FindMember<TSymbol>(
        INamedTypeSymbol? owner,
        string? name)
        where TSymbol : class, Microsoft.CodeAnalysis.ISymbol
    {
        if (owner == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (var current = owner; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name!))
            {
                if (member is TSymbol result)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static IMethodSymbol? FindMethod(
        INamedTypeSymbol? owner,
        string? name,
        int parameterCount)
    {
        if (owner == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (var current = owner; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name!))
            {
                if (member is IMethodSymbol { IsStatic: true } method &&
                    method.Parameters.Length == parameterCount)
                {
                    return method;
                }
            }
        }

        return null;
    }
}

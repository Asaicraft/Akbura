using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal enum PropertyReadKind : byte
{
    None,
    ClrProperty,
    AvaloniaProperty,
    AttachedAccessor,
    DirectMember,
}

/// <summary>
/// Contains the semantic decisions required to read one property value.
/// </summary>
internal readonly struct PropertyReadPlan
{
    private PropertyReadPlan(
        PropertyReadKind kind,
        RoslynPropertySymbol? clrProperty,
        RoslynSymbol? avaloniaProperty,
        IMethodSymbol? attachedGetter,
        ITypeSymbol? receiverType,
        string? memberName)
    {
        Kind = kind;
        ClrProperty = clrProperty;
        AvaloniaProperty = avaloniaProperty;
        AttachedGetter = attachedGetter;
        ReceiverType = receiverType;
        MemberName = memberName;
    }

    public PropertyReadKind Kind { get; }

    public RoslynPropertySymbol? ClrProperty { get; }

    public RoslynSymbol? AvaloniaProperty { get; }

    public IMethodSymbol? AttachedGetter { get; }

    public ITypeSymbol? ReceiverType { get; }

    public string? MemberName { get; }

    public bool IsValid => Kind != PropertyReadKind.None;

    public static PropertyReadPlan Create(AkburaPropertySymbol property)
    {
        Debug.Assert(property != null);

        if (property == null)
        {
            return default;
        }

        return property.ReadKind switch
        {
            PropertyAccessKind.ClrProperty => CreateClrProperty(property),
            PropertyAccessKind.AvaloniaProperty => CreateAvaloniaProperty(property),
            PropertyAccessKind.AttachedAccessor => CreateAttachedAccessor(property),
            PropertyAccessKind.Parameter or PropertyAccessKind.Command => CreateDirectMember(property),
            _ => default,
        };
    }

    private static PropertyReadPlan CreateClrProperty(AkburaPropertySymbol property)
    {
        var clrProperty = property.ReadDefinition.Symbol as RoslynPropertySymbol ??
            property.ClrPropertyDefinition.Symbol as RoslynPropertySymbol;

        if (clrProperty is not { IsStatic: false, ContainingType: { } receiverType })
        {
            return default;
        }

        return new PropertyReadPlan(
            PropertyReadKind.ClrProperty,
            clrProperty,
            avaloniaProperty: null,
            attachedGetter: null,
            receiverType,
            memberName: null);
    }

    private static PropertyReadPlan CreateAvaloniaProperty(AkburaPropertySymbol property)
    {
        var avaloniaProperty = GetStaticMember(property.ReadDefinition.Symbol) ??
            GetStaticMember(property.AvaloniaPropertyDefinition.Symbol) ??
            GetStaticMember(property.AttachedPropertyDefinition.Symbol);

        if (avaloniaProperty?.ContainingType is not { } receiverType)
        {
            return default;
        }

        return new PropertyReadPlan(
            PropertyReadKind.AvaloniaProperty,
            clrProperty: null,
            avaloniaProperty,
            attachedGetter: null,
            receiverType,
            memberName: null);
    }

    private static PropertyReadPlan CreateAttachedAccessor(AkburaPropertySymbol property)
    {
        var attachedGetter = property.ReadDefinition.Symbol as IMethodSymbol ??
            property.AttachedGetterDefinition.Symbol as IMethodSymbol;

        if (attachedGetter is not { IsStatic: true, Parameters.Length: > 0 })
        {
            return default;
        }

        var receiverType = property.AttachedTargetType.Symbol as ITypeSymbol ?? attachedGetter.Parameters[0].Type;

        return new PropertyReadPlan(
            PropertyReadKind.AttachedAccessor,
            clrProperty: null,
            avaloniaProperty: null,
            attachedGetter,
            receiverType,
            memberName: null);
    }

    private static PropertyReadPlan CreateDirectMember(AkburaPropertySymbol property)
    {
        if (string.IsNullOrEmpty(property.Name))
        {
            return default;
        }

        return new PropertyReadPlan(
            PropertyReadKind.DirectMember,
            clrProperty: null,
            avaloniaProperty: null,
            attachedGetter: null,
            receiverType: null,
            property.Name);
    }

    private static RoslynSymbol? GetStaticMember(RoslynSymbol? symbol)
    {
        return symbol is IFieldSymbol { IsStatic: true } or RoslynPropertySymbol { IsStatic: true } ? symbol : null;
    }
}

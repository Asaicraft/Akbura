using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal enum PropertyWriteKind : byte
{
    None,
    ClrProperty,
    AvaloniaProperty,
    AttachedAccessor,
    DirectMember,
}

internal enum PropertyWriteEnd : byte
{
    None,
    Assignment,
    Invocation,
}

/// <summary>
/// Contains the semantic decisions required to write one property value.
/// </summary>
internal readonly struct PropertyWritePlan
{
    private PropertyWritePlan(
        PropertyWriteKind kind,
        RoslynPropertySymbol? clrProperty,
        RoslynSymbol? avaloniaProperty,
        IMethodSymbol? attachedSetter,
        ITypeSymbol? receiverType,
        string? memberName)
    {
        Kind = kind;
        ClrProperty = clrProperty;
        AvaloniaProperty = avaloniaProperty;
        AttachedSetter = attachedSetter;
        ReceiverType = receiverType;
        MemberName = memberName;
    }

    public PropertyWriteKind Kind { get; }

    public RoslynPropertySymbol? ClrProperty { get; }

    public RoslynSymbol? AvaloniaProperty { get; }

    public IMethodSymbol? AttachedSetter { get; }

    public ITypeSymbol? ReceiverType { get; }

    public string? MemberName { get; }

    public bool IsValid => Kind != PropertyWriteKind.None;

    public static PropertyWritePlan Create(AkburaPropertySymbol property)
    {
        Debug.Assert(property != null);

        if (property == null)
        {
            return default;
        }

        return property.WriteKind switch
        {
            PropertyAccessKind.ClrProperty => CreateClrProperty(property),
            PropertyAccessKind.AvaloniaProperty => CreateAvaloniaProperty(property),
            PropertyAccessKind.AttachedAccessor => CreateAttachedAccessor(property),
            PropertyAccessKind.Parameter or PropertyAccessKind.Command => CreateDirectMember(property),
            _ => default,
        };
    }

    private static PropertyWritePlan CreateClrProperty(AkburaPropertySymbol property)
    {
        var clrProperty = property.WriteDefinition.Symbol as RoslynPropertySymbol ??
            property.ClrPropertyDefinition.Symbol as RoslynPropertySymbol;

        if (clrProperty is not { IsStatic: false, ContainingType: { } receiverType })
        {
            return default;
        }

        return new PropertyWritePlan(
            PropertyWriteKind.ClrProperty,
            clrProperty,
            avaloniaProperty: null,
            attachedSetter: null,
            receiverType,
            memberName: null);
    }

    private static PropertyWritePlan CreateAvaloniaProperty(AkburaPropertySymbol property)
    {
        var avaloniaProperty = GetStaticMember(property.WriteDefinition.Symbol) ??
            GetStaticMember(property.AvaloniaPropertyDefinition.Symbol) ??
            GetStaticMember(property.AttachedPropertyDefinition.Symbol);

        if (avaloniaProperty?.ContainingType is not { } receiverType)
        {
            return default;
        }

        return new PropertyWritePlan(
            PropertyWriteKind.AvaloniaProperty,
            clrProperty: null,
            avaloniaProperty,
            attachedSetter: null,
            receiverType,
            memberName: null);
    }

    private static PropertyWritePlan CreateAttachedAccessor(AkburaPropertySymbol property)
    {
        var attachedSetter = property.WriteDefinition.Symbol as IMethodSymbol ??
            property.AttachedSetterDefinition.Symbol as IMethodSymbol;

        if (attachedSetter is not { IsStatic: true, Parameters.Length: > 0 })
        {
            return default;
        }

        var receiverType = property.AttachedTargetType.Symbol as ITypeSymbol ?? attachedSetter.Parameters[0].Type;

        return new PropertyWritePlan(
            PropertyWriteKind.AttachedAccessor,
            clrProperty: null,
            avaloniaProperty: null,
            attachedSetter,
            receiverType,
            memberName: null);
    }

    private static PropertyWritePlan CreateDirectMember(AkburaPropertySymbol property)
    {
        if (string.IsNullOrEmpty(property.Name))
        {
            return default;
        }

        return new PropertyWritePlan(
            PropertyWriteKind.DirectMember,
            clrProperty: null,
            avaloniaProperty: null,
            attachedSetter: null,
            receiverType: null,
            property.Name);
    }

    private static RoslynSymbol? GetStaticMember(RoslynSymbol? symbol)
    {
        return symbol is IFieldSymbol { IsStatic: true } or RoslynPropertySymbol { IsStatic: true } ? symbol : null;
    }
}

/// <summary>
/// Writes the destination around a property value directly to CodeWriter.
/// </summary>
internal readonly ref struct PropertyWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public PropertyWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
    }

    public PropertyWriteEnd WriteStart(
        in PropertyWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!plan.IsValid || string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid property write reached code generation.");
            return PropertyWriteEnd.None;
        }

        return plan.Kind switch
        {
            PropertyWriteKind.ClrProperty => WriteClrProperty(plan, targetExpression),
            PropertyWriteKind.AvaloniaProperty => WriteAvaloniaProperty(plan, targetExpression),
            PropertyWriteKind.AttachedAccessor => WriteAttachedAccessor(plan, targetExpression),
            PropertyWriteKind.DirectMember => WriteDirectMember(plan, targetExpression),
            _ => PropertyWriteEnd.None,
        };
    }

    public void WriteEnd(PropertyWriteEnd end)
    {
        switch (end)
        {
            case PropertyWriteEnd.Assignment:
                _writer.Write(";");
                return;

            case PropertyWriteEnd.Invocation:
                _writer.Write(");");
                return;

            case PropertyWriteEnd.None:
                return;

            default:
                Debug.Fail("Unknown property-write ending.");
                return;
        }
    }

    private PropertyWriteEnd WriteClrProperty(
        in PropertyWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.ClrProperty != null);
        Debug.Assert(plan.ReceiverType != null);

        _writer.Write("((");
        _valueWriter.WriteTypeName(plan.ReceiverType);
        _writer.Write(")").Write(targetExpression).Write(").");
        _valueWriter.WriteIdentifier(plan.ClrProperty!.Name);
        _writer.Write(" = ");

        return PropertyWriteEnd.Assignment;
    }

    private PropertyWriteEnd WriteAvaloniaProperty(
        in PropertyWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.AvaloniaProperty != null);

        _writer
            .Write("((global::Avalonia.AvaloniaObject)")
            .Write(targetExpression)
            .Write(").SetValue(");
        _valueWriter.WriteStaticMemberReference(plan.AvaloniaProperty!);
        _writer.Write(", ");

        return PropertyWriteEnd.Invocation;
    }

    private PropertyWriteEnd WriteAttachedAccessor(
        in PropertyWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.AttachedSetter != null);
        Debug.Assert(plan.AttachedSetter?.ContainingType != null);
        Debug.Assert(plan.ReceiverType != null);

        var attachedSetter = plan.AttachedSetter!;

        _valueWriter.WriteTypeName(attachedSetter.ContainingType);
        _writer.Write(".");
        _valueWriter.WriteIdentifier(attachedSetter.Name);
        _writer.Write("((");
        _valueWriter.WriteTypeName(plan.ReceiverType);
        _writer.Write(")").Write(targetExpression).Write(", ");

        return PropertyWriteEnd.Invocation;
    }

    private PropertyWriteEnd WriteDirectMember(
        in PropertyWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(plan.MemberName));

        _writer.Write(targetExpression).Write(".");
        _valueWriter.WriteIdentifier(plan.MemberName!);
        _writer.Write(" = ");

        return PropertyWriteEnd.Assignment;
    }
}

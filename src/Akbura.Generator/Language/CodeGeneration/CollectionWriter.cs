using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal enum CollectionWriteKind : byte
{
    None,
    Property,
    ComponentParameter,
}

/// <summary>
/// Contains the semantic decisions required to append one collection value.
/// </summary>
internal readonly struct CollectionWritePlan
{
    private CollectionWritePlan(
        CollectionWriteKind kind,
        PropertyReadPlan property,
        ITypeSymbol? collectionType,
        ITypeSymbol? elementType,
        string? componentParameterName)
    {
        Kind = kind;
        Property = property;
        CollectionType = collectionType;
        ElementType = elementType;
        SupportsUntypedReconciliation =
            collectionType != null && ImplementsNonGenericIList(collectionType);
        SupportsTypedReconciliation =
            elementType != null &&
            collectionType != null &&
            ImplementsGenericIList(collectionType, elementType);
        ComponentParameterName = componentParameterName;
    }

    public CollectionWriteKind Kind { get; }

    public PropertyReadPlan Property { get; }

    public ITypeSymbol? CollectionType { get; }

    public ITypeSymbol? ElementType { get; }

    public bool SupportsUntypedReconciliation { get; }

    public bool SupportsTypedReconciliation { get; }

    public string? ComponentParameterName { get; }

    public bool IsValid => Kind != CollectionWriteKind.None;

    public static CollectionWritePlan CreateProperty(
        in PropertyReadPlan property,
        ITypeSymbol collectionType,
        ITypeSymbol? elementType = null)
    {
        Debug.Assert(property.IsValid);
        Debug.Assert(collectionType != null);

        return !property.IsValid || collectionType == null
            ? default
            : new CollectionWritePlan(
                CollectionWriteKind.Property,
                property,
                collectionType,
                elementType,
                componentParameterName: null);
    }

    public static CollectionWritePlan CreateComponentParameter(
        ITypeSymbol collectionType,
        string componentParameterName,
        ITypeSymbol? elementType = null)
    {
        Debug.Assert(collectionType != null);
        Debug.Assert(!string.IsNullOrEmpty(componentParameterName));

        return collectionType == null ||
            string.IsNullOrEmpty(componentParameterName)
                ? default
                : new CollectionWritePlan(
                    CollectionWriteKind.ComponentParameter,
                    property: default,
                    collectionType,
                    elementType,
                    componentParameterName);
    }

    private static bool ImplementsGenericIList(
        ITypeSymbol collectionType,
        ITypeSymbol elementType)
    {
        if (collectionType is INamedTypeSymbol namedType &&
            IsGenericIList(namedType, elementType))
        {
            return true;
        }

        foreach (var @interface in collectionType.AllInterfaces)
        {
            if (IsGenericIList(@interface, elementType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericIList(
        INamedTypeSymbol type,
        ITypeSymbol elementType)
    {
        var original = type.OriginalDefinition;
        return original.Name == "IList" &&
            original.Arity == 1 &&
            original.ContainingNamespace.ToDisplayString() ==
                "System.Collections.Generic" &&
            SymbolEqualityComparer.Default.Equals(
                type.TypeArguments[0],
                elementType);
    }

    private static bool ImplementsNonGenericIList(ITypeSymbol collectionType)
    {
        if (IsNonGenericIList(collectionType))
        {
            return true;
        }

        foreach (var @interface in collectionType.AllInterfaces)
        {
            if (IsNonGenericIList(@interface))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonGenericIList(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
            namedType.Name == "IList" &&
            namedType.Arity == 0 &&
            namedType.ContainingNamespace.ToDisplayString() ==
                "System.Collections";
    }
}

/// <summary>
/// Writes the destination around one collection value directly to CodeWriter.
/// </summary>
internal readonly ref struct CollectionWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly PropertyReadWriter _readWriter;

    public CollectionWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _readWriter = new PropertyReadWriter(writer!);
    }

    public bool WriteStart(
        in CollectionWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!plan.IsValid || string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid collection write reached code generation.");
            return false;
        }

        switch (plan.Kind)
        {
            case CollectionWriteKind.Property:
                if (!WriteTarget(plan, targetExpression))
                {
                    return false;
                }

                _writer.Write(".Add(");
                return true;

            case CollectionWriteKind.ComponentParameter:
                Debug.Assert(!string.IsNullOrEmpty(plan.ComponentParameterName));

                _writer.Write(targetExpression);
                _writer.Write(".");

                GeneratedMemberNameWriter.WriteCollectionAddMethod(_writer, plan.ComponentParameterName!);

                _writer.Write("(");
                return true;

            default:
                Debug.Fail("An invalid collection write reached code generation.");
                return false;
        }
    }

    public bool WriteTarget(
        in CollectionWritePlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        var hasValidTarget = plan.Kind switch
        {
            CollectionWriteKind.Property => plan.Property.IsValid,
            CollectionWriteKind.ComponentParameter =>
                !string.IsNullOrEmpty(plan.ComponentParameterName),
            _ => false,
        };

        if (plan.CollectionType == null ||
            string.IsNullOrEmpty(targetExpression) ||
            !hasValidTarget)
        {
            return false;
        }

        _writer.Write("((");
        _valueWriter.WriteTypeName(plan.CollectionType);
        _writer.Write(")");

        switch (plan.Kind)
        {
            case CollectionWriteKind.Property:
                _readWriter.Write(plan.Property, targetExpression);
                break;

            case CollectionWriteKind.ComponentParameter:
                _writer.Write(targetExpression);
                _writer.Write(".");
                _valueWriter.WriteIdentifier(plan.ComponentParameterName!);
                break;

            default:
                throw new InvalidOperationException(
                    "An invalid collection target reached code generation.");
        }

        _writer.Write("!)");
        return true;
    }

    public void WriteEnd()
    {
        _writer.Write(");");
    }
}

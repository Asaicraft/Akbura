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
        string? componentParameterName)
    {
        Kind = kind;
        Property = property;
        CollectionType = collectionType;
        ComponentParameterName = componentParameterName;
    }

    public CollectionWriteKind Kind { get; }

    public PropertyReadPlan Property { get; }

    public ITypeSymbol? CollectionType { get; }

    public string? ComponentParameterName { get; }

    public bool IsValid => Kind != CollectionWriteKind.None;

    public static CollectionWritePlan CreateProperty(
        in PropertyReadPlan property,
        ITypeSymbol collectionType)
    {
        Debug.Assert(property.IsValid);
        Debug.Assert(collectionType != null);

        return !property.IsValid || collectionType == null
            ? default
            : new CollectionWritePlan(
                CollectionWriteKind.Property,
                property,
                collectionType,
                componentParameterName: null);
    }

    public static CollectionWritePlan CreateComponentParameter(
        ITypeSymbol collectionType,
        string componentParameterName)
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
                    componentParameterName);
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
                Debug.Assert(plan.CollectionType != null);

                _writer.Write("((");
                _valueWriter.WriteTypeName(plan.CollectionType);
                _writer.Write(")");
                _readWriter.Write(plan.Property, targetExpression);
                _writer.Write("!).Add(");
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

    public void WriteEnd()
    {
        _writer.Write(");");
    }
}

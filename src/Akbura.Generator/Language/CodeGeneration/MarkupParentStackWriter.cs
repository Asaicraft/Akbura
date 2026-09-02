using System;
using System.Buffers;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal enum MarkupParentStackKind : byte
{
    None,
    Expression,
    ComponentHierarchy,
}

/// <summary>
/// Describes either an existing parent-stack expression or an element hierarchy
/// whose expression must be streamed to the generated source.
/// </summary>
internal readonly ref struct MarkupParentStackPlan
{
    public MarkupParentStackPlan(string expression)
    {
        Debug.Assert(!string.IsNullOrEmpty(expression));

        Kind = MarkupParentStackKind.Expression;
        Expression = expression;
        Elements = default;
        ElementId = -1;
        ScopeId = -1;
    }

    public MarkupParentStackPlan(
        ReadOnlySpan<ComponentElementPlan> elements,
        int elementId,
        int scopeId)
    {
        Debug.Assert(!elements.IsEmpty);
        Debug.Assert((uint)elementId < (uint)elements.Length);
        Debug.Assert(scopeId >= 0);

        Kind = MarkupParentStackKind.ComponentHierarchy;
        Expression = null;
        Elements = elements;
        ElementId = elementId;
        ScopeId = scopeId;
    }

    public MarkupParentStackKind Kind { get; }

    public string? Expression { get; }

    public ReadOnlySpan<ComponentElementPlan> Elements { get; }

    public int ElementId { get; }

    public int ScopeId { get; }
}

/// <summary>
/// Streams a direct-parent stack without materializing the hierarchy or its
/// generated expression.
/// </summary>
internal readonly ref struct MarkupParentStackWriter
{
    private const int StackHierarchyCapacity = 16;

    private readonly CodeWriter _writer;

    public MarkupParentStackWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);
        _writer = writer!;
    }

    public bool Write(in MarkupParentStackPlan plan)
    {
        switch (plan.Kind)
        {
            case MarkupParentStackKind.Expression:
                if (string.IsNullOrEmpty(plan.Expression))
                {
                    return false;
                }

                _writer.Write(plan.Expression!);
                return true;

            case MarkupParentStackKind.ComponentHierarchy:
                return WriteComponentHierarchy(plan);

            default:
                return false;
        }
    }

    internal static bool CanWrite(in MarkupParentStackPlan plan)
    {
        return plan.Kind switch
        {
            MarkupParentStackKind.Expression =>
                !string.IsNullOrEmpty(plan.Expression),
            MarkupParentStackKind.ComponentHierarchy =>
                TryGetHierarchyLength(plan, out _),
            _ => false,
        };
    }

    private bool WriteComponentHierarchy(in MarkupParentStackPlan plan)
    {
        if (!TryGetHierarchyLength(plan, out var hierarchyLength))
        {
            return false;
        }

        int[]? rentedHierarchy = null;
        Span<int> hierarchy = hierarchyLength <= StackHierarchyCapacity
            ? stackalloc int[StackHierarchyCapacity]
            : (rentedHierarchy = ArrayPool<int>.Shared.Rent(hierarchyLength));

        try
        {
            FillHierarchy(plan, hierarchy[..hierarchyLength]);
            WriteHierarchy(plan, hierarchy[..hierarchyLength]);
            return true;
        }
        finally
        {
            if (rentedHierarchy != null)
            {
                ArrayPool<int>.Shared.Return(rentedHierarchy);
            }
        }
    }

    private void WriteHierarchy(
        in MarkupParentStackPlan plan,
        ReadOnlySpan<int> hierarchy)
    {
        _writer.Write("new global::System.Object[] { ");

        var valueWriter = new CSharpValueWriter(_writer);
        var hasValue = plan.ScopeId == 0;

        if (hasValue)
        {
            _writer.Write("this");
        }

        for (var i = 0; i < hierarchy.Length; i++)
        {
            if (hasValue)
            {
                _writer.Write(", ");
            }

            valueWriter.WriteIdentifier(plan.Elements[hierarchy[i]].Identifier);
            hasValue = true;
        }

        _writer.Write(" }");
    }

    private static bool TryGetHierarchyLength(
        in MarkupParentStackPlan plan,
        out int hierarchyLength)
    {
        hierarchyLength = 0;

        if (plan.Elements.IsEmpty ||
            (uint)plan.ElementId >= (uint)plan.Elements.Length ||
            plan.ScopeId < 0)
        {
            return false;
        }

        var currentId = plan.ElementId;

        while (currentId >= 0)
        {
            if ((uint)currentId >= (uint)plan.Elements.Length)
            {
                return false;
            }

            ref readonly var element = ref plan.Elements[currentId];

            if (element.Id != currentId)
            {
                return false;
            }

            if (element.ScopeId != plan.ScopeId)
            {
                break;
            }

            hierarchyLength++;

            if (hierarchyLength > plan.Elements.Length)
            {
                return false;
            }

            currentId = element.ParentId;
        }

        return hierarchyLength > 0 && currentId >= -1;
    }

    private static void FillHierarchy(
        in MarkupParentStackPlan plan,
        Span<int> hierarchy)
    {
        var currentId = plan.ElementId;

        for (var i = hierarchy.Length - 1; i >= 0; i--)
        {
            hierarchy[i] = currentId;
            currentId = plan.Elements[currentId].ParentId;
        }
    }
}

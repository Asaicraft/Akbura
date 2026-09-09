using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentFirstUpdateActionWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly SourceMappingWriter _mappings;

    public ComponentFirstUpdateActionWriter(CodeWriter writer, ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void WriteNameAssignment(
        in ComponentNameAssignmentPlan plan,
        string targetExpression,
        string? nameScopeExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        using var mapping = _mappings.WriteStart(plan.Syntax);
        _writer.Write(targetExpression);
        _writer.Write(".Name = ");
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(";");

        if (string.IsNullOrEmpty(nameScopeExpression))
        {
            return;
        }

        _writer.Write(nameScopeExpression!);
        _writer.Write(".Register(");
        _writer.WriteStringLiteral(plan.Name);
        _writer.Write(", ");
        _writer.Write(targetExpression);
        _writer.WriteLine(");");
    }

    public void WriteRoutedEvent(
        in ComponentRoutedEventPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        using var mapping = _mappings.WriteStart(plan.Syntax!);

        switch (plan.Kind)
        {
            case ComponentRoutedEventKind.ClrEvent when plan.EventSymbol is IEventSymbol clrEvent:
                _writer.Write("((");
                _valueWriter.WriteTypeName(clrEvent.ContainingType);
                _writer.Write(")");
                _writer.Write(targetExpression);
                _writer.Write(").");
                _valueWriter.WriteIdentifier(clrEvent.Name);
                _writer.Write(" += ");
                _writer.Write(plan.HandlerExpression!);
                _writer.WriteLine(";");
                return;
            case ComponentRoutedEventKind.AvaloniaRoutedEvent when plan.EventSymbol != null:
                _writer.Write("((global::Avalonia.Interactivity.Interactive)");
                _writer.Write(targetExpression);
                _writer.Write(").AddHandler(");
                _valueWriter.WriteStaticMemberReference(plan.EventSymbol);
                _writer.Write(", ");
                _writer.Write(plan.HandlerExpression!);
                _writer.WriteLine(");");
                return;
            default:
                Debug.Fail("An invalid routed-event plan reached code generation.");
                return;
        }
    }

    public void WriteStructuralRoutedEvent(
        in ComponentRoutedEventPlan plan,
        int ownerRuntimeId,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(ownerRuntimeId >= 0);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        var slot = ComponentHotReloadIdentity.CreateRoutedEventSlot(plan);
        using var mapping = _mappings.WriteStart(plan.Syntax!);

        WriteOwnedOperationCondition(
            ownerRuntimeId,
            slot,
            ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(
                plan.Syntax!));

        switch (plan.Kind)
        {
            case ComponentRoutedEventKind.ClrEvent
                when plan.EventSymbol is IEventSymbol clrEvent:
                _writer.Write(
                    ComponentStructuralHotReloadWriter.RenderStateFieldName);
                _writer.WriteLine(".ApplyClrEventOperation(");
                _writer.CurrentIndent += _writer.TabSize;
                _writer.WriteIntegerLiteral(ownerRuntimeId);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(slot);
                _writer.WriteLine(",");
                _writer.Write(targetExpression);
                _writer.WriteLine(",");
                _writer.Write("typeof(");
                _valueWriter.WriteTypeName(clrEvent.ContainingType);
                _writer.WriteLine("),");
                _writer.WriteStringLiteral(clrEvent.Name);
                _writer.WriteLine(",");
                WriteHandler(plan);
                _writer.WriteLine(");");
                _writer.CurrentIndent -= _writer.TabSize;
                break;

            case ComponentRoutedEventKind.AvaloniaRoutedEvent
                when plan.EventSymbol != null:
                _writer.Write(
                    ComponentStructuralHotReloadWriter.RenderStateFieldName);
                _writer.WriteLine(".ApplyRoutedEventOperation(");
                _writer.CurrentIndent += _writer.TabSize;
                _writer.WriteIntegerLiteral(ownerRuntimeId);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(slot);
                _writer.WriteLine(",");
                _writer.Write("(global::Avalonia.Interactivity.Interactive)");
                _writer.Write(targetExpression);
                _writer.WriteLine(",");
                _valueWriter.WriteStaticMemberReference(plan.EventSymbol);
                _writer.WriteLine(",");
                WriteHandler(plan);
                _writer.WriteLine(");");
                _writer.CurrentIndent -= _writer.TabSize;
                break;

            default:
                Debug.Fail(
                    "An invalid structural routed-event plan reached code generation.");
                break;
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    public void WriteCommandBinding(
        in ComponentCommandBindingPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        using var mapping = _mappings.WriteStart(plan.Syntax);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, targetExpression);
        _valueWriter.WriteIdentifier(plan.CommandName);
        propertyWriter.WriteEnd(end);
        _writer.WriteLine();
    }

    public static bool CanWriteStructuralCommandBinding(
        in ComponentCommandBindingPlan plan)
    {
        if (!plan.IsValid ||
            plan.Destination.Kind == PropertyWriteKind.ClrProperty &&
            plan.Destination.ClrProperty?.GetMethod == null)
        {
            return false;
        }

        return plan.Destination.Kind is
            PropertyWriteKind.ClrProperty or
            PropertyWriteKind.AvaloniaProperty or
            PropertyWriteKind.ComponentParameter or
            PropertyWriteKind.DirectMember;
    }

    public bool WriteStructuralCommandBinding(
        in ComponentCommandBindingPlan plan,
        int ownerRuntimeId,
        string targetExpression)
    {
        Debug.Assert(ownerRuntimeId >= 0);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!CanWriteStructuralCommandBinding(plan) ||
            ownerRuntimeId < 0 ||
            string.IsNullOrEmpty(targetExpression))
        {
            return false;
        }

        var destination = plan.Destination;
        var methodName = destination.Kind == PropertyWriteKind.AvaloniaProperty
            ? "ReconcileAvaloniaValue"
            : "ReconcileClrValue";

        using var mapping = _mappings.WriteStart(plan.Syntax);
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(".");
        _writer.Write(methodName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(ownerRuntimeId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreatePropertySlot(destination));
        _writer.WriteLine(",");
        _writer.Write(targetExpression);
        _writer.WriteLine(",");

        if (destination.Kind == PropertyWriteKind.AvaloniaProperty)
        {
            Debug.Assert(destination.AvaloniaProperty != null);
            _valueWriter.WriteStaticMemberReference(
                destination.AvaloniaProperty!);
            _writer.WriteLine(",");
        }
        else
        {
            WriteClrDeclaringType(destination);
            _writer.WriteLine(",");
            var propertyName = destination.Kind == PropertyWriteKind.ClrProperty
                ? destination.ClrProperty!.Name
                : destination.MemberName!;
            _writer.WriteStringLiteral(propertyName);
            _writer.WriteLine(",");
        }

        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(
                plan.Syntax));
        _writer.WriteLine(",");
        _valueWriter.WriteIdentifier(plan.CommandName);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        return true;
    }

    private void WriteOwnedOperationCondition(
        int ownerRuntimeId,
        string slot,
        string identity)
    {
        _writer.Write("if (");
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ShouldApplyOwnedOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(ownerRuntimeId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(slot);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(identity);
        _writer.WriteLine("))");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteHandler(in ComponentRoutedEventPlan plan)
    {
        Debug.Assert(plan.HandlerType != null);

        _writer.Write("(");
        _valueWriter.WriteTypeName(plan.HandlerType!);
        _writer.Write(")(");
        _writer.Write(plan.HandlerExpression!);
        _writer.Write(")");
    }

    private void WriteClrDeclaringType(in PropertyWritePlan destination)
    {
        if (destination.ReceiverType == null)
        {
            _writer.Write("null");
            return;
        }

        _writer.Write("typeof(");
        _valueWriter.WriteTypeName(destination.ReceiverType);
        _writer.Write(")");
    }
}

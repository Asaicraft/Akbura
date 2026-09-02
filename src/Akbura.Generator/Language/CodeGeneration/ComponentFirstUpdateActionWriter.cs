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
        string targetExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        using var mapping = _mappings.WriteStart(plan.Syntax);
        _writer.Write(targetExpression);
        _writer.Write(".Name = ");
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(";");
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
}

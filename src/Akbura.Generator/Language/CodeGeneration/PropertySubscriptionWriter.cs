using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes reverse property synchronization without consulting the semantic model.
/// </summary>
internal readonly ref struct PropertySubscriptionWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly PropertyReadWriter _readWriter;
    private readonly SourceMappingWriter _mappings;

    public PropertySubscriptionWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _readWriter = new PropertyReadWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void WriteHandler(
        in ComponentPropertySubscriptionPlan subscription)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            switch (subscription.Observation.Kind)
            {
                case PropertyObservationKind.AvaloniaProperty:
                case PropertyObservationKind.GeneratedParameter:
                    WriteAvaloniaHandler(subscription);
                    return;

                case PropertyObservationKind.NotifyPropertyChanged:
                    WriteNotifyPropertyChangedHandler(subscription);
                    return;

                default:
                    Debug.Fail("An invalid property observation reached code generation.");
                    return;
            }
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteRegistration(
        in ComponentElementPlan element,
        in ComponentPropertySubscriptionPlan subscription)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            switch (subscription.Observation.Kind)
            {
                case PropertyObservationKind.AvaloniaProperty:
                case PropertyObservationKind.GeneratedParameter:
                    WriteAvaloniaRegistration(element, subscription);
                    return;

                case PropertyObservationKind.NotifyPropertyChanged:
                    WriteNotifyPropertyChangedRegistration(element, subscription);
                    return;

                default:
                    Debug.Fail("An invalid property observation reached code generation.");
                    return;
            }
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteAvaloniaHandler(
        in ComponentPropertySubscriptionPlan subscription)
    {
        WriteHandlerStart(subscription.Id);
        _writer.Write("global::Avalonia.AvaloniaPropertyChangedEventArgs ");
        WriteChangeName(subscription.Id);
        _writer.WriteLine(")");
        _writer.CurrentIndent -= _writer.TabSize;
        OpenBlock();
        WriteAvaloniaGuard(subscription.Observation, subscription.Id);
        WriteAvaloniaAssignment(subscription);
        CloseBlock();
    }

    private void WriteNotifyPropertyChangedHandler(
        in ComponentPropertySubscriptionPlan subscription)
    {
        var property = subscription.Observation.Symbol as IPropertySymbol;

        if (property == null)
        {
            Debug.Fail("A notify-property observation must contain a CLR property.");
            return;
        }

        WriteHandlerStart(subscription.Id);
        _writer.Write("global::System.ComponentModel.PropertyChangedEventArgs ");
        WriteEventName(subscription.Id);
        _writer.WriteLine(")");
        _writer.CurrentIndent -= _writer.TabSize;
        OpenBlock();
        WriteNotifyPropertyChangedGuard(property, subscription.Id);
        WriteNotifyPropertyChangedAssignment(subscription, property, "__sender!");
        CloseBlock();
    }

    private void WriteHandlerStart(int id)
    {
        _writer.Write("private void ");
        WriteHandlerName(id);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("object? __sender,");
    }

    private void WriteAvaloniaRegistration(
        in ComponentElementPlan element,
        in ComponentPropertySubscriptionPlan subscription)
    {
        _writer.Write("((global::Avalonia.AvaloniaObject)");
        WriteElementReference(element);
        _writer.Write(").PropertyChanged += ");

        if (!element.IsLocal)
        {
            WriteHandlerName(subscription.Id);
            _writer.WriteLine(";");
            return;
        }

        _writer.Write("(_, ");
        WriteChangeName(subscription.Id);
        _writer.WriteLine(") =>");
        OpenBlock();
        WriteAvaloniaGuard(subscription.Observation, subscription.Id);
        WriteAvaloniaAssignment(subscription);
        CloseBlock(";");
    }

    private void WriteNotifyPropertyChangedRegistration(
        in ComponentElementPlan element,
        in ComponentPropertySubscriptionPlan subscription)
    {
        var property = subscription.Observation.Symbol as IPropertySymbol;

        if (property == null)
        {
            Debug.Fail("A notify-property observation must contain a CLR property.");
            return;
        }

        _writer.Write("if (");
        WriteElementReference(element);
        _writer.Write(" is global::System.ComponentModel.INotifyPropertyChanged ");
        WriteNotifierName(subscription.Id);
        _writer.WriteLine(")");
        OpenBlock();
        WriteNotifierName(subscription.Id);
        _writer.Write(".PropertyChanged += ");

        if (!element.IsLocal)
        {
            WriteHandlerName(subscription.Id);
            _writer.WriteLine(";");
            CloseBlock();
            return;
        }

        _writer.Write("(__sender, ");
        WriteEventName(subscription.Id);
        _writer.WriteLine(") =>");
        OpenBlock();
        WriteNotifyPropertyChangedGuard(property, subscription.Id);
        WriteNotifyPropertyChangedAssignment(subscription, property, "__sender!");
        CloseBlock(";");
        CloseBlock();
    }

    private void WriteAvaloniaGuard(
        in PropertyObservationPlan observation,
        int id)
    {
        _writer.Write("if (");
        WriteChangeName(id);
        _writer.Write(".Property != ");
        WriteObservedAvaloniaProperty(observation);
        _writer.WriteLine(")");
        OpenBlock();
        _writer.WriteLine("return;");
        CloseBlock();
        _writer.WriteLine();
    }

    private void WriteNotifyPropertyChangedGuard(
        IPropertySymbol property,
        int id)
    {
        _writer.Write("if (!global::System.String.IsNullOrEmpty(");
        WriteEventName(id);
        _writer.WriteLine(".PropertyName) &&");
        _writer.CurrentIndent += _writer.TabSize;
        WriteEventName(id);
        _writer.Write(".PropertyName != ");
        _writer.WriteStringLiteral(property.Name);
        _writer.WriteLine(")");
        _writer.CurrentIndent -= _writer.TabSize;
        OpenBlock();
        _writer.WriteLine("return;");
        CloseBlock();
        _writer.WriteLine();
    }

    private void WriteAvaloniaAssignment(
        in ComponentPropertySubscriptionPlan subscription)
    {
        using var mapping = _mappings.WriteStart(subscription.Syntax);

        WriteTargetOperation(subscription.TargetOperation);
        _writer.Write(" = (");
        _valueWriter.WriteTypeName(subscription.ValueType);
        _writer.Write(")");
        WriteChangeName(subscription.Id);
        _writer.WriteLine(".NewValue!;");
    }

    private void WriteNotifyPropertyChangedAssignment(
        in ComponentPropertySubscriptionPlan subscription,
        IPropertySymbol property,
        string senderExpression)
    {
        using var mapping = _mappings.WriteStart(subscription.Syntax);

        WriteTargetOperation(subscription.TargetOperation);
        _writer.Write(" = ");
        _readWriter.Write(property, senderExpression);
        _writer.WriteLine(";");
    }

    private void WriteObservedAvaloniaProperty(
        in PropertyObservationPlan observation)
    {
        var targetProperty = observation.TargetProperty;
        Debug.Assert(targetProperty.IsValid);

        var writer = new MarkupTargetPropertyWriter(_writer);
        writer.Write(targetProperty);
    }

    private void WriteTargetOperation(
        in CSharpOperationDefinition operation)
    {
        var syntax = operation.Syntax;

        if (syntax == null)
        {
            Debug.Fail("A property subscription must have a target operation.");
            _writer.Write("/* invalid binding target */");
            return;
        }

        _writer.Write(syntax.ToString());
    }

    private void WriteElementReference(
        in ComponentElementPlan element)
    {
        _writer.Write(element.Identifier);
    }

    private void WriteHandlerName(int id)
    {
        _writer.Write("__OnPropertyBindingChanged");
        _writer.WriteIntegerLiteral(id);
    }

    private void WriteNotifierName(int id)
    {
        _writer.Write("__notifier");
        _writer.WriteIntegerLiteral(id);
    }

    private void WriteChangeName(int id)
    {
        _writer.Write("__change");
        _writer.WriteIntegerLiteral(id);
    }

    private void WriteEventName(int id)
    {
        _writer.Write("__event");
        _writer.WriteIntegerLiteral(id);
    }

    private void OpenBlock()
    {
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void CloseBlock(string suffix = "")
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("}").WriteLine(suffix);
    }
}

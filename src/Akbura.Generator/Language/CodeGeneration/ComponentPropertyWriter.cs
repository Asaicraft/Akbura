using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentPropertyWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;
    private readonly SourceMappingWriter _mappings;

    public ComponentPropertyWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _environment = environment;
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void Write(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        if (!plan.Destination.IsValid || string.IsNullOrEmpty(targetExpression))
        {
            throw new InvalidOperationException("The component property write is not initialized.");
        }

        using var mapping = _mappings.WriteStart(plan.Syntax);
        var targetContext = context.WithTarget(
            targetExpression,
            GetTargetPropertyExpression(plan.Destination));

        switch (plan.ValueKind)
        {
            case ComponentPropertyValueKind.Constant:
            case ComponentPropertyValueKind.CSharpExpression:
            case ComponentPropertyValueKind.ElementReference:
                WriteCSharpValue(component, plan, targetExpression);
                break;
            case ComponentPropertyValueKind.MarkupExtensionValue:
                WriteMarkupExtension(component, plan, targetExpression, targetContext);
                break;
            case ComponentPropertyValueKind.MarkupBinding:
                WriteBinding(component, plan, targetExpression, targetContext);
                break;
            case ComponentPropertyValueKind.DynamicResource:
                WriteDynamicResource(component, plan, targetExpression, targetContext);
                break;
            case ComponentPropertyValueKind.StaticResource:
                WriteStaticResource(component, plan, targetExpression, targetContext);
                break;
            case ComponentPropertyValueKind.BindingBaseResult:
                WriteBindingBase(component, plan, targetExpression, targetContext);
                break;
            case ComponentPropertyValueKind.RuntimeMarkupExtensionResult:
                WriteRuntimeResult(component, plan, targetExpression, targetContext);
                break;
            default:
                throw new InvalidOperationException("The component property value is not initialized.");
        }

        _writer.WriteLine();
    }

    public void WriteCachedBindingPath(in BindingWritePlan plan)
    {
        var writer = new BindingWriter(_writer, in _environment);
        writer.WriteCachedPathField(plan);
    }

    private void WriteCSharpValue(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target)
    {
        ref readonly var value = ref GetCSharpValue(component, plan.PayloadIndex);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, target);
        var valueWriter = new CSharpValueWriter(_writer);

        if (plan.ValueKind == ComponentPropertyValueKind.Constant && value.ConvertedValue != null)
        {
            WriteConvertedValue(value.ConvertedValue, value.TargetType, valueWriter);
        }
        else if (plan.ValueKind == ComponentPropertyValueKind.CSharpExpression &&
            !value.Operation.IsDefault &&
            value.Operation.Syntax != null)
        {
            _writer.Write(value.Operation.Syntax.ToString());
        }
        else if (value.Operation.ConstantValue.HasValue)
        {
            valueWriter.WriteConstant(value.Operation.ConstantValue.Value, value.TargetType);
        }
        else if (!value.Operation.IsDefault && value.Operation.Syntax != null)
        {
            _writer.Write(value.Operation.Syntax.ToString());
        }
        else if (value.ConvertedValue != null)
        {
            valueWriter.WriteConstant(value.ConvertedValue, value.TargetType);
        }
        else
        {
            _writer.WriteStringLiteral(value.LiteralValue ?? string.Empty);
        }

        propertyWriter.WriteEnd(end);
    }

    private void WriteConvertedValue(
        object convertedValue,
        ITypeSymbol? targetType,
        CSharpValueWriter valueWriter)
    {
        switch (convertedValue)
        {
            case GridDefinitionListValue definitions:
                WriteGridDefinitions(definitions, targetType, valueWriter);
                return;

            case MarkupLiteralValue literal:
                WriteMarkupLiteral(literal, valueWriter);
                return;

            default:
                valueWriter.WriteConstant(convertedValue, targetType);
                return;
        }
    }

    private void WriteMarkupLiteral(
        MarkupLiteralValue literal,
        CSharpValueWriter valueWriter)
    {
        switch (literal.ConverterKind)
        {
            case MarkupLiteralConverterKind.ParseMethod when literal.Converter.Symbol is IMethodSymbol method:
                valueWriter.WriteTypeName(method.ContainingType);
                _writer.Write(".");
                valueWriter.WriteIdentifier(method.Name);
                _writer.Write("(").WriteStringLiteral(literal.Text).Write(")");
                return;

            case MarkupLiteralConverterKind.StringConstructor:
                _writer.Write("new ");
                valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write("(").WriteStringLiteral(literal.Text).Write(")");
                return;

            case MarkupLiteralConverterKind.TypeConverter
                when literal.Converter.Symbol is INamedTypeSymbol converterType:
                _writer.Write("(");
                valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write(")new ");
                valueWriter.WriteTypeName(converterType);
                _writer.Write("().ConvertFromInvariantString(").WriteStringLiteral(literal.Text).Write(")!");
                return;

            default:
                _writer.Write("(");
                valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write(")").WriteStringLiteral(literal.Text);
                return;
        }
    }

    private void WriteGridDefinitions(
        in GridDefinitionListValue value,
        ITypeSymbol? targetType,
        CSharpValueWriter valueWriter)
    {
        var isRows = targetType?.Name == "RowDefinitions";
        var collectionType = isRows
            ? "global::Avalonia.Controls.RowDefinitions"
            : "global::Avalonia.Controls.ColumnDefinitions";
        var itemType = isRows
            ? "global::Avalonia.Controls.RowDefinition"
            : "global::Avalonia.Controls.ColumnDefinition";
        var lengthProperty = isRows ? "Height" : "Width";
        var minProperty = isRows ? "MinHeight" : "MinWidth";
        var maxProperty = isRows ? "MaxHeight" : "MaxWidth";

        _writer.Write("new ").Write(collectionType).Write(" { ");
        for (var i = 0; i < value.Definitions.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            var definition = value.Definitions[i];
            _writer.Write("new ").Write(itemType).Write(" { ").Write(lengthProperty).Write(" = ");
            WriteGridLength(definition.Length, valueWriter);

            if (definition.Min is { } min)
            {
                _writer.Write(", ").Write(minProperty).Write(" = ");
                valueWriter.WriteConstant(min, targetType: null);
            }

            if (definition.Max is { } max)
            {
                _writer.Write(", ").Write(maxProperty).Write(" = ");
                valueWriter.WriteConstant(max, targetType: null);
            }

            _writer.Write(" }");
        }

        _writer.Write(" }");
    }

    private void WriteGridLength(
        in GridDefinitionLengthValue length,
        CSharpValueWriter valueWriter)
    {
        var unit = length.UnitType switch
        {
            GridDefinitionUnitType.Auto => "global::Avalonia.Controls.GridUnitType.Auto",
            GridDefinitionUnitType.Star => "global::Avalonia.Controls.GridUnitType.Star",
            _ => "global::Avalonia.Controls.GridUnitType.Pixel",
        };

        _writer.Write("new global::Avalonia.Controls.GridLength(");
        valueWriter.WriteConstant(length.Value, targetType: null);
        _writer.Write(", ").Write(unit).Write(")");
    }

    private void WriteMarkupExtension(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, target);
        var writer = new MarkupExtensionWriter(_writer, in _environment);
        writer.Write(GetMarkupExtension(component, plan.PayloadIndex).Extension, context);
        propertyWriter.WriteEnd(end);
    }

    private void WriteBinding(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var resultWriter = new BindingBaseResultWriter(_writer, in _environment);
        resultWriter.WriteBinding(
            CreateTarget(plan.Destination, target),
            GetBinding(component, plan.PayloadIndex),
            context);
    }

    private void WriteDynamicResource(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new DynamicResourceWriter(_writer, in _environment);
        ref readonly var result = ref GetMarkupExtension(component, plan.PayloadIndex);
        writer.Write(CreateTarget(plan.Destination, target), result, context);
    }

    private void WriteStaticResource(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new StaticResourceWriter(_writer, in _environment);
        ref readonly var result = ref GetMarkupExtension(component, plan.PayloadIndex);
        writer.Write(CreateTarget(plan.Destination, target), result, context);
    }

    private void WriteBindingBase(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new BindingBaseResultWriter(_writer, in _environment);
        writer.WriteMarkupExtension(
            CreateTarget(plan.Destination, target),
            GetMarkupExtension(component, plan.PayloadIndex).Extension,
            context);
    }

    private void WriteRuntimeResult(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new RuntimeMarkupExtensionResultWriter(_writer, in _environment);
        writer.Write(
            CreateTarget(plan.Destination, target),
            GetMarkupExtension(component, plan.PayloadIndex).Extension,
            context);
    }

    private static ref readonly ComponentCSharpValuePlan GetCSharpValue(
        in ComponentPlan component,
        int index)
    {
        if ((uint)index >= (uint)component.CSharpValues.Length)
        {
            throw new InvalidOperationException("The C# value index is outside the component plan.");
        }

        return ref component.CSharpValues.ItemRef(index);
    }

    private static ref readonly MarkupExtensionResultPlan GetMarkupExtension(
        in ComponentPlan component,
        int index)
    {
        if ((uint)index >= (uint)component.MarkupExtensions.Length)
        {
            throw new InvalidOperationException("The markup-extension index is outside the component plan.");
        }

        return ref component.MarkupExtensions.ItemRef(index);
    }

    private static ref readonly BindingWritePlan GetBinding(
        in ComponentPlan component,
        int index)
    {
        if ((uint)index >= (uint)component.Bindings.Length)
        {
            throw new InvalidOperationException("The binding index is outside the component plan.");
        }

        return ref component.Bindings.ItemRef(index);
    }

    private static AvaloniaPropertyWriteTarget CreateTarget(
        in PropertyWritePlan destination,
        string target)
    {
        if (destination.Kind != PropertyWriteKind.AvaloniaProperty || destination.AvaloniaProperty == null)
        {
            throw new InvalidOperationException("The result requires an Avalonia property destination.");
        }

        return new AvaloniaPropertyWriteTarget(target, destination.AvaloniaProperty);
    }

    private static string GetTargetPropertyExpression(in PropertyWritePlan destination)
    {
        if (destination.AvaloniaProperty != null)
        {
            return GetStaticMemberExpression(destination.AvaloniaProperty);
        }

        if (destination.ClrProperty is { } property)
        {
            return "typeof(" + GetTypeExpression(property.ContainingType) + ").GetProperty(\"" +
                property.Name + "\")!";
        }

        if (destination.AttachedSetter is { } setter)
        {
            return "typeof(" + GetTypeExpression(setter.ContainingType) + ").GetMethod(\"" +
                setter.Name + "\")!";
        }

        return "null!";
    }

    private static string GetStaticMemberExpression(ISymbol symbol)
    {
        using var writer = new CodeWriter();
        var valueWriter = new CSharpValueWriter(writer);
        valueWriter.WriteStaticMemberReference(symbol);
        return writer.GetText().ToString();
    }

    private static string GetTypeExpression(ITypeSymbol type)
    {
        using var writer = new CodeWriter();
        var valueWriter = new CSharpValueWriter(writer);
        valueWriter.WriteTypeName(type);
        return writer.GetText().ToString();
    }
}

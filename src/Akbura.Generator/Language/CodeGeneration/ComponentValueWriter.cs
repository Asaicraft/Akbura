using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes lowered component values without consulting the semantic model.
/// </summary>
internal readonly ref struct ComponentValueWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public ComponentValueWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void WriteConstant(in ComponentCSharpValuePlan value)
    {
        if (value.ConvertedValue != null)
        {
            WriteConvertedValue(value.ConvertedValue, value.TargetType);
            return;
        }

        var constant = value.Operation.ConstantValue;

        if (constant.HasValue)
        {
            _valueWriter.WriteConstant(constant.Value, value.TargetType);
            return;
        }

        _writer.WriteStringLiteral(value.LiteralValue ?? string.Empty);
    }

    public void WriteExpression(in ComponentCSharpValuePlan value)
    {
        if (!value.Operation.IsDefault && value.Operation.Syntax != null)
        {
            _writer.Write(value.Operation.Syntax.ToString());
            return;
        }

        if (value.ConvertedValue != null)
        {
            WriteConvertedValue(value.ConvertedValue, value.TargetType);
            return;
        }

        _writer.Write("default");
    }

    public void WriteElementReference(string identifier)
    {
        Debug.Assert(!string.IsNullOrEmpty(identifier));

        if (string.IsNullOrEmpty(identifier))
        {
            Debug.Fail("An element reference requires an identifier.");
            _writer.Write("default");
            return;
        }

        _valueWriter.WriteIdentifier(identifier);
    }

    private void WriteConvertedValue(object convertedValue, ITypeSymbol? targetType)
    {
        switch (convertedValue)
        {
            case GridDefinitionListValue definitions:
                WriteGridDefinitions(definitions, targetType);
                return;

            case MarkupLiteralValue literal:
                WriteMarkupLiteral(literal);
                return;

            default:
                _valueWriter.WriteConstant(convertedValue, targetType);
                return;
        }
    }

    private void WriteMarkupLiteral(MarkupLiteralValue literal)
    {
        switch (literal.ConverterKind)
        {
            case MarkupLiteralConverterKind.ParseMethod when literal.Converter.Symbol is IMethodSymbol method:
                _valueWriter.WriteTypeName(method.ContainingType);
                _writer.Write(".");
                _valueWriter.WriteIdentifier(method.Name);
                _writer.Write("(").WriteStringLiteral(literal.Text).Write(")");
                return;

            case MarkupLiteralConverterKind.StringConstructor:
                _writer.Write("new ");
                _valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write("(").WriteStringLiteral(literal.Text).Write(")");
                return;

            case MarkupLiteralConverterKind.TypeConverter
                when literal.Converter.Symbol is INamedTypeSymbol converterType:
                _writer.Write("(");
                _valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write(")new ");
                _valueWriter.WriteTypeName(converterType);
                _writer.Write("().ConvertFromInvariantString(").WriteStringLiteral(literal.Text).Write(")!");
                return;

            default:
                _writer.Write("(");
                _valueWriter.WriteTypeName(literal.TargetType.Symbol);
                _writer.Write(")").WriteStringLiteral(literal.Text);
                return;
        }
    }

    private void WriteGridDefinitions(in GridDefinitionListValue value, ITypeSymbol? targetType)
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
            WriteGridLength(definition.Length);

            if (definition.Min is { } min)
            {
                _writer.Write(", ").Write(minProperty).Write(" = ");
                _valueWriter.WriteConstant(min, targetType: null);
            }

            if (definition.Max is { } max)
            {
                _writer.Write(", ").Write(maxProperty).Write(" = ");
                _valueWriter.WriteConstant(max, targetType: null);
            }

            _writer.Write(" }");
        }

        _writer.Write(" }");
    }

    private void WriteGridLength(in GridDefinitionLengthValue length)
    {
        var unit = length.UnitType switch
        {
            GridDefinitionUnitType.Auto => "global::Avalonia.Controls.GridUnitType.Auto",
            GridDefinitionUnitType.Star => "global::Avalonia.Controls.GridUnitType.Star",
            _ => "global::Avalonia.Controls.GridUnitType.Pixel",
        };

        _writer.Write("new global::Avalonia.Controls.GridLength(");
        _valueWriter.WriteConstant(length.Value, targetType: null);
        _writer.Write(", ").Write(unit).Write(")");
    }
}

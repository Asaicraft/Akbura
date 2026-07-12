using Akbura.Language.Symbols;
using System;

namespace Akbura.Language.Operations;

internal enum MarkupLiteralConverterKind : byte
{
    ParseMethod,
    StringConstructor,
}

internal sealed class MarkupLiteralValue
{
    public MarkupLiteralValue(
        string text,
        CSharpSymbolDefinition targetType,
        MarkupLiteralConverterKind converterKind,
        CSharpSymbolDefinition converter)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        TargetType = targetType;
        ConverterKind = converterKind;
        Converter = converter;
    }

    public string Text { get; }

    public CSharpSymbolDefinition TargetType { get; }

    public MarkupLiteralConverterKind ConverterKind { get; }

    public CSharpSymbolDefinition Converter { get; }
}

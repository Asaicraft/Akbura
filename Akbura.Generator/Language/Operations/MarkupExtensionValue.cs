using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal sealed class MarkupExtensionValue
{
    public MarkupExtensionValue(
        string rawText,
        string name,
        CSharpSymbolDefinition extensionType,
        CSharpSymbolDefinition constructor,
        CSharpSymbolDefinition provideValueMethod,
        CSharpSymbolDefinition resultType,
        ImmutableArray<MarkupExtensionArgumentValue> arguments,
        ImmutableArray<MarkupExtensionPropertyValue> properties,
        MarkupBindingValue? binding = null)
    {
        RawText = rawText ?? throw new ArgumentNullException(nameof(rawText));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ExtensionType = extensionType;
        Constructor = constructor;
        ProvideValueMethod = provideValueMethod;
        ResultType = resultType;
        Arguments = arguments.IsDefault
            ? ImmutableArray<MarkupExtensionArgumentValue>.Empty
            : arguments;
        Properties = properties.IsDefault
            ? ImmutableArray<MarkupExtensionPropertyValue>.Empty
            : properties;
        Binding = binding;
    }

    public string RawText { get; }

    public string Name { get; }

    public CSharpSymbolDefinition ExtensionType { get; }

    public CSharpSymbolDefinition Constructor { get; }

    public CSharpSymbolDefinition ProvideValueMethod { get; }

    public CSharpSymbolDefinition ResultType { get; }

    public ImmutableArray<MarkupExtensionArgumentValue> Arguments { get; }

    public ImmutableArray<MarkupExtensionPropertyValue> Properties { get; }

    public MarkupBindingValue? Binding { get; }
}

internal enum MarkupBindingKind
{
    Reflection,
    Compiled,
}

internal sealed class MarkupBindingValue
{
    public MarkupBindingValue(
        MarkupBindingKind kind,
        string path,
        CSharpSymbolDefinition bindingType,
        CSharpSymbolDefinition sourceType,
        CSharpSymbolDefinition resultType,
        ImmutableArray<MarkupBindingPathElement> pathElements)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Kind = kind;
        BindingType = bindingType;
        SourceType = sourceType;
        ResultType = resultType;
        PathElements = pathElements.IsDefault
            ? ImmutableArray<MarkupBindingPathElement>.Empty
            : pathElements;
    }

    public MarkupBindingKind Kind { get; }

    public string Path { get; }

    public CSharpSymbolDefinition BindingType { get; }

    public CSharpSymbolDefinition SourceType { get; }

    public CSharpSymbolDefinition ResultType { get; }

    public ImmutableArray<MarkupBindingPathElement> PathElements { get; }
}

internal enum MarkupBindingPathElementKind
{
    Property,
    Field,
    Indexer,
    ElementName,
    Self,
    Ancestor,
    TemplatedParent,
    Not,
    TypeCast,
    Unknown,
}

internal readonly struct MarkupBindingPathElement
{
    public MarkupBindingPathElement(
        MarkupBindingPathElementKind kind,
        string text,
        CSharpSymbolDefinition symbol = default,
        CSharpSymbolDefinition type = default,
        ImmutableArray<string> arguments = default,
        ImmutableArray<MarkupBindingPathArgument> boundArguments = default,
        int? level = null)
    {
        Kind = kind;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Symbol = symbol;
        Type = type;
        Arguments = arguments.IsDefault
            ? ImmutableArray<string>.Empty
            : arguments;
        BoundArguments = boundArguments.IsDefault
            ? ImmutableArray<MarkupBindingPathArgument>.Empty
            : boundArguments;
        Level = level;
    }

    public MarkupBindingPathElementKind Kind { get; }

    public string Text { get; }

    public CSharpSymbolDefinition Symbol { get; }

    public CSharpSymbolDefinition Type { get; }

    public ImmutableArray<string> Arguments { get; }

    public ImmutableArray<MarkupBindingPathArgument> BoundArguments { get; }

    public int? Level { get; }
}

internal readonly struct MarkupBindingPathArgument
{
    public MarkupBindingPathArgument(
        string text,
        CSharpSymbolDefinition parameter,
        CSharpSymbolDefinition type,
        CSharpOperationDefinition operation,
        AkburaConversion conversion,
        object? convertedValue)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Parameter = parameter;
        Type = type;
        Operation = operation;
        Conversion = conversion;
        ConvertedValue = convertedValue;
    }

    public string Text { get; }

    public CSharpSymbolDefinition Parameter { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition Operation { get; }

    public AkburaConversion Conversion { get; }

    public object? ConvertedValue { get; }
}

internal readonly struct MarkupExtensionArgumentValue
{
    public MarkupExtensionArgumentValue(
        string text,
        CSharpSymbolDefinition parameter,
        CSharpSymbolDefinition type,
        CSharpOperationDefinition operation,
        AkburaConversion conversion,
        object? convertedValue,
        MarkupExtensionValue? nestedValue)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Parameter = parameter;
        Type = type;
        Operation = operation;
        Conversion = conversion;
        ConvertedValue = convertedValue;
        NestedValue = nestedValue;
    }

    public string Text { get; }

    public CSharpSymbolDefinition Parameter { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition Operation { get; }

    public AkburaConversion Conversion { get; }

    public object? ConvertedValue { get; }

    public MarkupExtensionValue? NestedValue { get; }
}

internal readonly struct MarkupExtensionPropertyValue
{
    public MarkupExtensionPropertyValue(
        string name,
        string value,
        CSharpSymbolDefinition property,
        CSharpSymbolDefinition type,
        CSharpOperationDefinition operation,
        AkburaConversion conversion,
        object? convertedValue,
        MarkupExtensionValue? nestedValue)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Property = property;
        Type = type;
        Operation = operation;
        Conversion = conversion;
        ConvertedValue = convertedValue;
        NestedValue = nestedValue;
    }

    public string Name { get; }

    public string Value { get; }

    public CSharpSymbolDefinition Property { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition Operation { get; }

    public AkburaConversion Conversion { get; }

    public object? ConvertedValue { get; }

    public MarkupExtensionValue? NestedValue { get; }
}

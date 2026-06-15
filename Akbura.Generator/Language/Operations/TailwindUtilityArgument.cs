using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal readonly struct TailwindUtilityArgument
{
    public TailwindUtilityArgument(
        TailwindSegmentSyntax syntax,
        string text,
        CSharpSymbolDefinition type,
        CSharpOperationDefinition valueOperation,
        object? constantValue)
    {
        Syntax = syntax;
        Text = text;
        Type = type;
        ValueOperation = valueOperation;
        ConstantValue = constantValue;
    }

    public TailwindSegmentSyntax Syntax { get; }

    public string Text { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public object? ConstantValue { get; }
}

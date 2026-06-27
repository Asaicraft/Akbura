using Akbura.Language.Binder;
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
        object? constantValue,
        ICSharpOperation? valueOperationTree = null)
    {
        Syntax = syntax;
        Text = text;
        Type = type;
        ValueOperation = valueOperation;
        ConstantValue = constantValue;
        ValueOperationTree = valueOperationTree;
    }

    public TailwindSegmentSyntax Syntax { get; }

    public string Text { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public ICSharpOperation? ValueOperationTree { get; }

    public object? ConstantValue { get; }

    public TailwindUtilityArgument WithValueOperationTree(ICSharpOperation? valueOperationTree)
    {
        return new TailwindUtilityArgument(
            Syntax,
            Text,
            Type,
            ValueOperation,
            ConstantValue,
            valueOperationTree);
    }
}

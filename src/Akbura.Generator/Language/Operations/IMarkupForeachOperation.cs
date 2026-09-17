using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IMarkupForeachOperation : IOperation
{
    new MarkupForeachStatementSyntax Syntax { get; }
    CSharpOperationDefinition Source { get; }
    CSharpSymbolDefinition IterationType { get; }
    CSharpSymbolDefinition OutputType { get; }
    string IterationVariableName { get; }
    string IndexVariableName { get; }
    CSharpOperationDefinition Key { get; }
    AkburaSyntax? KeySyntax { get; }
    ImmutableArray<MarkupForeachBodyItem> Body { get; }
}

internal readonly struct MarkupForeachBodyItem
{
    public MarkupForeachBodyItem(AkburaSyntax syntax, CSharpOperationDefinition code,
        ImmutableArray<MarkupChildContent> content, ImmutableArray<MarkupForeachBodyItem> body,
        ImmutableArray<MarkupForeachBodyItem> elseBody, IMarkupForeachOperation? foreachOperation)
    {
        Syntax = syntax;
        Code = code;
        Content = content;
        Body = body;
        ElseBody = elseBody;
        ForeachOperation = foreachOperation;
    }

    public AkburaSyntax Syntax { get; }
    /// <summary>The bound ordinary C# condition or statement, not a lowered flow result.</summary>
    public CSharpOperationDefinition Code { get; }
    public ImmutableArray<MarkupChildContent> Content { get; }
    public ImmutableArray<MarkupForeachBodyItem> Body { get; }
    public ImmutableArray<MarkupForeachBodyItem> ElseBody { get; }
    public IMarkupForeachOperation? ForeachOperation { get; }
}

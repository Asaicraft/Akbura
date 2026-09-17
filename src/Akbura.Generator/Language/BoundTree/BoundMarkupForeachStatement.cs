using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupForeachStatement : BoundNode
{
    public BoundMarkupForeachStatement(MarkupForeachStatementSyntax syntax, BinderType binder,
        CSharpOperationDefinition source, CSharpSymbolDefinition iterationType, CSharpSymbolDefinition outputType,
        CSharpOperationDefinition key, AkburaSyntax? keySyntax, ImmutableArray<BoundMarkupForeachBodyItem> body,
        ImmutableArray<BoundNode> children, ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        : base(BoundKind.MarkupForeach, syntax, binder, AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics, children)
    {
        Source = source;
        IterationType = iterationType;
        OutputType = outputType;
        Key = key;
        KeySyntax = keySyntax;
        Body = body;
    }

    public new MarkupForeachStatementSyntax Syntax => (MarkupForeachStatementSyntax)base.Syntax;
    public CSharpOperationDefinition Source { get; }
    public CSharpSymbolDefinition IterationType { get; }
    public CSharpSymbolDefinition OutputType { get; }
    public CSharpOperationDefinition Key { get; }
    public AkburaSyntax? KeySyntax { get; }
    public ImmutableArray<BoundMarkupForeachBodyItem> Body { get; }

    public BoundMarkupForeachStatement Update(CSharpOperationDefinition source, CSharpSymbolDefinition iterationType,
        CSharpSymbolDefinition outputType, CSharpOperationDefinition key,
        ImmutableArray<BoundMarkupForeachBodyItem> body, ImmutableArray<BoundNode> children) =>
        source.Equals(Source) && iterationType.Equals(IterationType) && outputType.Equals(OutputType) &&
            key.Equals(Key) && body == Body && children == Children ? this :
            new(Syntax, Binder, source, iterationType, outputType, key, KeySyntax, body, children, Diagnostics);

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupForeach(this);
    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default => visitor.VisitMarkupForeach(this);
    public override TResult? Accept<TParameter, TResult>(BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default => visitor.VisitMarkupForeach(this, parameter);
}

internal readonly struct BoundMarkupForeachBodyItem
{
    public BoundMarkupForeachBodyItem(AkburaSyntax syntax, CSharpOperationDefinition code = default,
        ImmutableArray<MarkupChildContent> content = default,
        ImmutableArray<BoundMarkupForeachBodyItem> body = default,
        ImmutableArray<BoundMarkupForeachBodyItem> elseBody = default,
        BoundMarkupForeachStatement? foreachStatement = null)
    {
        Syntax = syntax;
        Code = code;
        Content = content.IsDefault ? ImmutableArray<MarkupChildContent>.Empty : content;
        Body = body.IsDefault ? ImmutableArray<BoundMarkupForeachBodyItem>.Empty : body;
        ElseBody = elseBody.IsDefault ? ImmutableArray<BoundMarkupForeachBodyItem>.Empty : elseBody;
        ForeachStatement = foreachStatement;
    }

    public AkburaSyntax Syntax { get; }
    public CSharpOperationDefinition Code { get; }
    public ImmutableArray<MarkupChildContent> Content { get; }
    public ImmutableArray<BoundMarkupForeachBodyItem> Body { get; }
    public ImmutableArray<BoundMarkupForeachBodyItem> ElseBody { get; }
    public BoundMarkupForeachStatement? ForeachStatement { get; }
}

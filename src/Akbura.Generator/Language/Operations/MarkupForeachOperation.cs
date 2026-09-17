using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupForeachOperation : IMarkupForeachOperation
{
    private IOperation? _parent;

    public MarkupForeachOperation(MarkupForeachStatementSyntax syntax, CSharpOperationDefinition source,
        CSharpSymbolDefinition iterationType, CSharpSymbolDefinition outputType,
        CSharpOperationDefinition key, AkburaSyntax? keySyntax,
        ImmutableArray<MarkupForeachBodyItem> body, ImmutableArray<IOperation> children, bool hasErrors)
    {
        Syntax = syntax;
        Source = source;
        IterationType = iterationType;
        OutputType = outputType;
        Key = key;
        KeySyntax = keySyntax;
        Body = body;
        Children = children;
        HasErrors = hasErrors;
        IterationVariableName = syntax.Header.GetRawCSharpForeach()?.Identifier.ValueText ?? string.Empty;
        IndexVariableName = CSharpProbeBuilder.GetMarkupLoopIndexName(syntax);
        foreach (var child in children)
        {
            if (child is MarkupForeachOperation loop)
            {
                loop.SetParent(this);
            }
            else if (child is MarkupIfOperation conditional)
            {
                conditional.SetParent(this);
            }
            else if (child is CSharpOperation code)
            {
                code.SetParent(this);
            }
        }
    }

    public MarkupForeachStatementSyntax Syntax { get; }
    AkburaSyntax IOperation.Syntax => Syntax;
    public CSharpOperationDefinition Source { get; }
    public CSharpSymbolDefinition IterationType { get; }
    public CSharpSymbolDefinition OutputType { get; }
    public string IterationVariableName { get; }
    public string IndexVariableName { get; }
    public CSharpOperationDefinition Key { get; }
    public AkburaSyntax? KeySyntax { get; }
    public ImmutableArray<MarkupForeachBodyItem> Body { get; }
    public OperationKind Kind => OperationKind.MarkupForeach;
    public OperationLanguage Language => OperationLanguage.Markup;
    public IOperation? Parent => _parent;
    public ImmutableArray<IOperation> Children { get; }
    public ISymbol? TargetSymbol => null;
    public ISymbol? TypeSymbol => null;
    public CSharpOperationDefinition CSharpDefinition => default;
    public bool IsImplicit => false;
    public bool HasErrors { get; }
    public object? ConstantValue => null;
    internal void SetParent(IOperation parent) => _parent = parent;
    public void Accept(OperationVisitor visitor) => visitor.VisitMarkupForeach(this);
    public TResult? Accept<TParameter, TResult>(OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter) => visitor.VisitMarkupForeach(this, parameter);
    public bool Equals(IOperation? other) => ReferenceEquals(this, other);
    public override bool Equals(object? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public string ToDisplayString() => Syntax.ToFullString();
}

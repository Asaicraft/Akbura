using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal sealed class AkcssContextualOperation : IOperation
{
    public AkcssContextualOperation(
        OperationKind kind,
        AkburaSyntax syntax,
        AkcssBindingContext? context,
        AkcssBoundData? data,
        ISymbol? targetSymbol,
        CSharpOperationDefinition csharpDefinition,
        ImmutableArray<IOperation> children,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        bool hasErrors)
    {
        Kind = kind;
        Syntax = syntax;
        Context = context;
        Data = data;
        TargetSymbol = targetSymbol;
        CSharpDefinition = csharpDefinition;
        Children = children;
        Diagnostics = diagnostics;
        HasErrors = hasErrors;
        foreach (var child in children)
        {
            switch (child)
            {
                case AkcssContextualOperation contextual:
                    contextual.SetParent(this);
                    break;
                case CSharpOperation csharp:
                    csharp.SetParent(this);
                    break;
                case MarkupIfOperation conditional:
                    conditional.SetParent(this);
                    break;
                case MarkupForeachOperation loop:
                    loop.SetParent(this);
                    break;
            }
        }
    }

    public OperationKind Kind { get; }

    public OperationLanguage Language => Kind is OperationKind.MarkupElement or OperationKind.MarkupContent
        ? OperationLanguage.Markup
        : OperationLanguage.Akcss;

    public AkburaSyntax Syntax { get; }

    public AkcssBindingContext? Context { get; }

    public AkcssBoundData? Data { get; }

    public IOperation? Parent { get; private set; }

    public ImmutableArray<IOperation> Children { get; }

    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }

    public ISymbol? TargetSymbol { get; }

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition { get; }

    internal void SetParent(IOperation parent) => Parent = parent;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => CSharpDefinition.ConstantValue is { HasValue: true } value ? value.Value : null;

    public void Accept(OperationVisitor visitor) => visitor.VisitAkcssContextual(this);

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor, TParameter parameter) =>
        visitor.VisitAkcssContextual(this, parameter);

    public bool Equals(IOperation? other) => ReferenceEquals(this, other);

    public string ToDisplayString() => Syntax.ToString();
}

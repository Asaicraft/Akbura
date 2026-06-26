using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Language.BoundTree;

internal abstract class BoundNode
{
    private readonly BoundKind _kind;

    protected BoundNode(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
    {
        Debug.Assert(kind != BoundKind.None);
        Debug.Assert(syntax != null);

        _kind = kind;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Binder = binder;
        SymbolInfo = symbolInfo;
        Operation = operation;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
        Children = children.IsDefault
            ? ImmutableArray<BoundNode>.Empty
            : children;
    }

    public AkburaSyntax Syntax { get; }

    public BinderType Binder { get; }

    public AkburaSymbolInfo SymbolInfo { get; }

    public IOperation? Operation { get; }

    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }

    public ImmutableArray<BoundNode> Children { get; }

    public BoundKind Kind => _kind;

    public virtual bool IsError => false;

    public virtual void Accept(BoundTreeVisitor visitor)
    {
        visitor.DefaultVisit(this);
    }

    public virtual TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
    {
        return visitor.DefaultVisit(this);
    }

    public virtual TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.DefaultVisit(this, parameter);
    }

    public bool HasErrors
    {
        get
        {
            if (IsError || Diagnostics.Length != 0)
            {
                return true;
            }

            foreach (var child in Children)
            {
                if (child.HasErrors)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

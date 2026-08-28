using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Language.BoundTree;

internal abstract class BoundNode
{
    private readonly BoundKind _kind;
    private readonly BoundNodeAttributes _attributes;

    [Flags]
    private enum BoundNodeAttributes : byte
    {
        HasErrors = 1 << 0
    }

    protected BoundNode(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
    {
        Debug.Assert(kind != BoundKind.None);
        Debug.Assert(syntax != null);

        _kind = kind;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Binder = binder;
        SymbolInfo = symbolInfo;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
        Children = children.IsDefault
            ? ImmutableArray<BoundNode>.Empty
            : children;

        if (hasErrors || ComputeHasErrorsFromDiagnosticsAndChildren())
        {
            _attributes |= BoundNodeAttributes.HasErrors;
        }
    }

    public AkburaSyntax Syntax { get; }

    public BinderType Binder { get; }

    public AkburaSymbolInfo SymbolInfo { get; }

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

    public bool HasErrors => HasAttribute(BoundNodeAttributes.HasErrors);

    private bool HasAttribute(BoundNodeAttributes attribute)
    {
        return (_attributes & attribute) != 0;
    }

    private bool ComputeHasErrorsFromDiagnosticsAndChildren()
    {
        if (Diagnostics.Length != 0)
        {
            return true;
        }

        for (var i = 0; i < Children.Length; i++)
        {
            if (Children[i].HasErrors)
            {
                return true;
            }
        }

        return false;
    }
}

using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Akbura.Language;

internal abstract class MemberSemanticModel : AkburaSemanticModel
{
    private readonly ReaderWriterLockSlim _nodeMapLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<AkburaSyntax, OneOrMany<BoundNode>> _guardedBoundNodeMap =
        new();

    protected MemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax? root = null)
        : base(semanticModel)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Root = root ?? scope;
    }

    public AkburaDocumentSyntax Scope { get; }

    public AkburaSyntax Root { get; }

    public Akbura.Language.Binder.Binder RootBinder => GetBinder(Root);

    public abstract override AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax);

    public abstract BoundNode BindSemanticSyntax(AkburaSyntax syntax);

    public virtual BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        if (TryGetBoundNodeFromMap(syntax, out var cached))
        {
            return cached;
        }

        var boundNode = RootBinder.BindOperationSyntax(syntax);
        AddBoundTreeToMap(boundNode);
        return boundNode;
    }

    public bool TryGetBoundNodeFromMap(
        AkburaSyntax syntax,
        out BoundNode boundNode)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (TryGetBoundNodeFromGuardedMap(syntax, out boundNode!))
        {
            return true;
        }

        if (TryGetCachedBoundNode(syntax, out boundNode!))
        {
            AddBoundTreeToMap(boundNode);
            return true;
        }

        boundNode = null!;
        return false;
    }

    public void AddBoundTreeToMap(BoundNode boundNode)
    {
        if (boundNode == null)
        {
            throw new ArgumentNullException(nameof(boundNode));
        }

        var builder = new BoundNodeMapBuilder(this);
        _nodeMapLock.EnterWriteLock();
        try
        {
            builder.Visit(boundNode);
        }
        finally
        {
            _nodeMapLock.ExitWriteLock();
        }
    }

    protected TBoundNode CacheBoundNode<TBoundNode>(
        AkburaSyntax syntax,
        TBoundNode boundNode)
        where TBoundNode : BoundNode
    {
        AddBoundNodeToMap(syntax, boundNode);
        SetCachedBoundNode(syntax, boundNode);
        return boundNode;
    }

    private void AddBoundNodeToMap(
        AkburaSyntax syntax,
        BoundNode boundNode)
    {
        _nodeMapLock.EnterWriteLock();
        try
        {
            GuardedAddBoundNodeToMap(syntax, boundNode);
        }
        finally
        {
            _nodeMapLock.ExitWriteLock();
        }
    }

    private bool TryGetBoundNodeFromGuardedMap(
        AkburaSyntax syntax,
        out BoundNode boundNode)
    {
        _nodeMapLock.EnterReadLock();
        try
        {
            if (_guardedBoundNodeMap.TryGetValue(syntax, out var nodes) &&
                !nodes.IsEmpty)
            {
                boundNode = nodes[0];
                return true;
            }
        }
        finally
        {
            _nodeMapLock.ExitReadLock();
        }

        boundNode = null!;
        return false;
    }

    private void GuardedAddBoundNodeToMap(
        AkburaSyntax syntax,
        BoundNode boundNode)
    {
        if (!ReferenceEquals(syntax.Root.Green, Root.Root.Green))
        {
            return;
        }

        if (_guardedBoundNodeMap.TryGetValue(syntax, out var nodes))
        {
            _guardedBoundNodeMap[syntax] = nodes.Add(boundNode);
            return;
        }

        _guardedBoundNodeMap.Add(syntax, OneOrMany.Create(boundNode));
    }

    private sealed class BoundNodeMapBuilder : BoundTreeWalker
    {
        private readonly MemberSemanticModel _memberModel;

        public BoundNodeMapBuilder(MemberSemanticModel memberModel)
        {
            _memberModel = memberModel;
        }

        public override void DefaultVisit(BoundNode node)
        {
            _memberModel.GuardedAddBoundNodeToMap(node.Syntax, node);
            _memberModel.SetCachedBoundNode(node.Syntax, node);
            base.DefaultVisit(node);
        }
    }
}

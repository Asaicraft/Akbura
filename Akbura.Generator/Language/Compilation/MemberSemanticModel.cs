using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Akbura.Language;

internal abstract class MemberSemanticModel : AkburaSemanticModel
{
    private readonly AkburaSemanticModel _containingSemanticModel;
    private readonly ReaderWriterLockSlim _nodeMapLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<AkburaSyntax, OneOrMany<BoundNode>> _guardedBoundNodeMap =
        new();

    protected MemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax? root = null)
        : base(semanticModel)
    {
        _containingSemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Root = root ?? scope;
    }

    public AkburaDocumentSyntax Scope { get; }

    public AkburaSyntax Root { get; }

    public Akbura.Language.Binder.Binder RootBinder => GetBinder(Root);

    public abstract override AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax);

    internal override MemberSemanticModel GetMemberSemanticModel(AkburaSyntax syntax)
    {
        return _containingSemanticModel.GetMemberSemanticModel(syntax);
    }

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

        _nodeMapLock.EnterWriteLock();
        try
        {
            NodeMapBuilder.AddToMap(boundNode, _guardedBoundNodeMap, this);
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
        if (!SemanticSyntaxIdentity.IsInSameTree(syntax, Root))
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

    private sealed class NodeMapBuilder : BoundTreeWalker
    {
        private readonly OrderPreservingMultiDictionary<AkburaSyntax, BoundNode> _map;
        private readonly MemberSemanticModel _memberModel;
        private readonly AkburaSyntax? _thisSyntaxNodeOnly;

        private NodeMapBuilder(
            OrderPreservingMultiDictionary<AkburaSyntax, BoundNode> map,
            MemberSemanticModel memberModel,
            AkburaSyntax? thisSyntaxNodeOnly)
        {
            _map = map;
            _memberModel = memberModel;
            _thisSyntaxNodeOnly = thisSyntaxNodeOnly;
        }

        public static void AddToMap(
            BoundNode root,
            Dictionary<AkburaSyntax, OneOrMany<BoundNode>> map,
            MemberSemanticModel memberModel,
            AkburaSyntax? node = null)
        {
            Debug.Assert(
                node == null || root == null || root is not BoundStatement,
                "Individually added nodes are not supposed to be statements.");

            if (root == null)
            {
                return;
            }

            var additionMap = OrderPreservingMultiDictionary<AkburaSyntax, BoundNode>.GetInstance();
            try
            {
                var builder = new NodeMapBuilder(additionMap, memberModel, node);
                builder.Visit(root);

                foreach (var key in additionMap.Keys)
                {
                    var nodesToAdd = additionMap.GetAsOneOrMany(key);
                    if (map.ContainsKey(key))
                    {
#if DEBUG
                        var existing = map[key];
                        Debug.Assert(existing.Count == nodesToAdd.Count);
                        for (var index = 0; index < existing.Count; index++)
                        {
                            Debug.Assert(
                                existing[index].Kind == nodesToAdd[index].Kind,
                                "New bound node does not match existing bound node.");
                        }
#endif
                        continue;
                    }

                    map.Add(key, nodesToAdd);
                    if (!memberModel.TryGetCachedBoundNode(key, out _))
                    {
                        memberModel.SetCachedBoundNode(key, nodesToAdd[0]);
                    }
                }
            }
            finally
            {
                additionMap.Free();
            }
        }

        public override void DefaultVisit(BoundNode node)
        {
            if (ShouldAddNode(node))
            {
                _map.Add(node.Syntax, node);
            }

            base.DefaultVisit(node);
        }

        private bool ShouldAddNode(BoundNode node)
        {
            if (!SemanticSyntaxIdentity.IsInSameTree(node.Syntax, _memberModel.Root))
            {
                return false;
            }

            return _thisSyntaxNodeOnly == null ||
                   SemanticSyntaxIdentity.Equals(node.Syntax, _thisSyntaxNodeOnly);
        }
    }
}

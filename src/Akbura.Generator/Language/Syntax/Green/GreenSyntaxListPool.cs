using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal class GreenSyntaxListPool
{
    private ArrayElement<GreenSyntaxListBuilder?>[] _freeList = new ArrayElement<GreenSyntaxListBuilder?>[10];
    private int _freeIndex;

#if DEBUG
    private readonly List<GreenSyntaxListBuilder> _allocated = [];
#endif

    public GreenSyntaxListPool()
    {
    }

    public GreenSyntaxListBuilder Allocate()
    {
        GreenSyntaxListBuilder item;
        if (_freeIndex > 0)
        {
            _freeIndex--;
            item = _freeList[_freeIndex].Value!;
            _freeList[_freeIndex].Value = null;
        }
        else
        {
            item = new GreenSyntaxListBuilder(10);
        }

#if DEBUG
        Debug.Assert(!_allocated.Contains(item));
        _allocated.Add(item);
#endif
        return item;
    }

    public GreenSyntaxListBuilder<TNode> Allocate<TNode>() where TNode : GreenNode
    {
        return new GreenSyntaxListBuilder<TNode>(Allocate());
    }

    public SeparatedGreenSyntaxListBuilder<TNode> AllocateSeparated<TNode>() where TNode : GreenNode
    {
        return new SeparatedGreenSyntaxListBuilder<TNode>(Allocate());
    }

    public void Free<TNode>(in SeparatedGreenSyntaxListBuilder<TNode> item) where TNode : GreenNode
    {
        Free(item.UnderlyingBuilder);
    }

    public void Free(GreenSyntaxListBuilder? item)
    {
        if (item is null)
        {
            return;
        }

        item.Clear();
        if (_freeIndex >= _freeList.Length)
        {
            Grow();
        }
#if DEBUG
        Debug.Assert(_allocated.Contains(item));

        _allocated.Remove(item);
#endif
        _freeList[_freeIndex].Value = item;
        _freeIndex++;
    }

    private void Grow()
    {
        var tmp = new ArrayElement<GreenSyntaxListBuilder?>[_freeList.Length * 2];
        Array.Copy(_freeList, tmp, _freeList.Length);
        _freeList = tmp;
    }

    public GreenSyntaxList<TNode> ToListAndFree<TNode>(GreenSyntaxListBuilder<TNode> item)
        where TNode : GreenNode
    {
        if (item.IsNull)
        {
            return default;
        }

        var list = item.ToList();
        Free(item);
        return list;
    }

    public SeparatedGreenSyntaxList<TNode> ToListAndFree<TNode>(in SeparatedGreenSyntaxListBuilder<TNode> item)
        where TNode : GreenNode
    {
        var list = item.ToList();
        Free(item);
        return list;
    }
}
using Akbura.Collections;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax;

internal sealed class SyntaxListBuilder
{
    private ArrayElement<GreenNode?>[] _nodes;
    public int Count { get; private set; }

    public SyntaxListBuilder(int size)
    {
        _nodes = new ArrayElement<GreenNode?>[size];
    }

    public void Clear()
    {
        Count = 0;
    }

    public void Add(AkburaSyntax item)
    {
        AddInternal(item.Green);
    }

    internal void AddInternal(GreenNode item)
    {
        Debug.Assert(item != null);

        if (Count >= _nodes.Length)
        {
            Grow(Count == 0 ? 8 : _nodes.Length * 2);
        }

        _nodes[Count++].Value = item;
    }

    public void AddRange(AkburaSyntax[] items)
    {
        AddRange(items, 0, items.Length);
    }

    public void AddRange(AkburaSyntax[] items, int offset, int length)
    {
        if (Count + length > _nodes.Length)
        {
            Grow(Count + length);
        }

        for (int i = offset, j = Count; i < offset + length; ++i, ++j)
        {
            _nodes[j].Value = items[i].Green;
        }

        var start = Count;
        Count += length;
        Validate(start, Count);
    }

    [Conditional("DEBUG")]
    private void Validate(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (_nodes[i].Value == null)
            {
                throw new InvalidOperationException("Cannot add a null node.");
            }
        }
    }

    public void AddRange(SyntaxList<AkburaSyntax> list)
    {
        this.AddRange(list, 0, list.Count);
    }

    public void AddRange(SyntaxList<AkburaSyntax> list, int offset, int count)
    {
        if (Count + count > _nodes.Length)
        {
            Grow(Count + count);
        }

        var dst = Count;
        for (int i = offset, limit = offset + count; i < limit; i++)
        {
            _nodes[dst].Value = list.ItemInternal(i)!.Green;
            dst++;
        }

        var start = Count;
        Count += count;
        Validate(start, Count);
    }

    public void AddRange<TNode>(SyntaxList<TNode> list) where TNode : AkburaSyntax
    {
        AddRange(list, 0, list.Count);
    }

    public void AddRange<TNode>(SyntaxList<TNode> list, int offset, int count) where TNode : AkburaSyntax
    {
        AddRange(new SyntaxList<AkburaSyntax>(list.Node), offset, count);
    }

    public void AddRange(SyntaxNodeOrTokenList list)
    {
        AddRange(list, 0, list.Count);
    }

    public void AddRange(SyntaxNodeOrTokenList list, int offset, int count)
    {
        if (Count + count > _nodes.Length)
        {
            Grow(Count + count);
        }

        var dst = Count;
        for (int i = offset, limit = offset + count; i < limit; i++)
        {
            _nodes[dst].Value = list[i].UnderlyingNode;
            dst++;
        }

        var start = Count;
        Count += count;
        Validate(start, Count);
    }

    public void AddRange(SyntaxTokenList list)
    {
        AddRange(list, 0, list.Count);
    }

    public void AddRange(SyntaxTokenList list, int offset, int length)
    {
        AkburaDebug.Assert(list.Node is not null);
        AddRange(new SyntaxList<AkburaSyntax>(list.Node.CreateRed()), offset, length);
    }

    private void Grow(int size)
    {
        var tmp = new ArrayElement<GreenNode?>[size];
        Array.Copy(_nodes, tmp, _nodes.Length);
        _nodes = tmp;
    }

    public bool Any(int kind)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_nodes[i].Value!.RawKind == kind)
            {
                return true;
            }
        }

        return false;
    }

    internal GreenNode? ToListNode()
    {
        switch (Count)
        {
            case 0:
                return null;
            case 1:
                return _nodes[0].Value;
            case 2:
                return GreenSyntaxList.List(_nodes[0].Value!, _nodes[1].Value!);
            case 3:
                return GreenSyntaxList.List(_nodes[0].Value!, _nodes[1].Value!, _nodes[2].Value!);
            default:
                var tmp = new ArrayElement<GreenNode>[Count];
                for (var i = 0; i < Count; i++)
                {
                    tmp[i].Value = _nodes[i].Value!;
                }

                return GreenSyntaxList.List(tmp);
        }
    }

    public static implicit operator SyntaxList<AkburaSyntax>(SyntaxListBuilder builder)
    {
        if (builder == null)
        {
            return default;
        }

        return builder.ToList();
    }

    public void RemoveLast()
    {
        Count -= 1;
        _nodes[Count] = default;
    }
}
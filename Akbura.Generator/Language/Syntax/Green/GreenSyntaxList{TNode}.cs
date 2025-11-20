using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal readonly partial struct GreenSyntaxList<TNode> : IEquatable<GreenSyntaxList<TNode>>
        where TNode : GreenNode
{
    private readonly GreenNode? _node;

    public GreenSyntaxList(GreenNode? node)
    {
        _node = node;
    }

    public GreenNode? Node => _node;

    public int Count => _node == null ? 0 : (_node.IsList ? _node.SlotCount : 1);

    public TNode? this[int index]
    {
        get
        {
            if (_node == null)
            {
                return null;
            }
            else if (_node.IsList)
            {
                Debug.Assert(index >= 0);
                Debug.Assert(index <= _node.SlotCount);

                return (TNode?)_node.GetSlot(index);
            }
            else if (index == 0)
            {
                return (TNode?)_node;
            }
            else
            {
                throw new UnreachableException();
            }
        }
    }

    public TNode GetRequiredItem(int index)
    {
        var node = this[index];
        Debug.Assert(node is object);
        return node!;
    }

    public GreenNode? ItemUntyped(int index)
    {
        Debug.Assert(_node is not null);
        var node = _node!;
        if (node.IsList)
        {
            return node.GetSlot(index);
        }

        Debug.Assert(index == 0);
        return node;
    }

    public bool Any()
    {
        return _node != null;
    }

    public bool Any(int kind)
    {
        foreach (var element in this)
        {
            if (element.RawKind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public TNode[] Nodes
    {
        get
        {
            var arr = new TNode[Count];
            for (var i = 0; i < Count; i++)
            {
                arr[i] = GetRequiredItem(i);
            }
            return arr;
        }
    }

    public TNode? Last
    {
        get
        {
            Debug.Assert(_node is not null);
            var node = _node;
            if (node.IsList)
            {
                return (TNode?)node.GetSlot(node.SlotCount - 1);
            }

            return (TNode?)node;
        }
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public void CopyTo(int offset, ArrayElement<GreenNode>[] array, int arrayOffset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            array[arrayOffset + i].Value = GetRequiredItem(i + offset);
        }
    }

    public static bool operator ==(GreenSyntaxList<TNode> left, GreenSyntaxList<TNode> right)
    {
        return left._node == right._node;
    }

    public static bool operator !=(GreenSyntaxList<TNode> left, GreenSyntaxList<TNode> right)
    {
        return left._node != right._node;
    }

    public bool Equals(GreenSyntaxList<TNode> other)
    {
        return _node == other._node;
    }

    public override bool Equals(object? obj)
    {
        return (obj is GreenSyntaxList<TNode> list) && Equals(list);
    }

    public override int GetHashCode()
    {
        return _node != null ? _node.GetHashCode() : 0;
    }

    public SeparatedGreenSyntaxList<TOther> AsSeparatedList<TOther>() where TOther : GreenNode
    {
        return new SeparatedGreenSyntaxList<TOther>(this);
    }

    public static implicit operator GreenSyntaxList<TNode>(TNode node)
    {
        return new GreenSyntaxList<TNode>(node);
    }

    public static implicit operator GreenSyntaxList<TNode>(GreenSyntaxList<GreenNode> nodes)
    {
        return new GreenSyntaxList<TNode>(nodes._node);
    }

    public static implicit operator GreenSyntaxList<GreenNode>(GreenSyntaxList<TNode> nodes)
    {
        return new GreenSyntaxList<GreenNode>(nodes.Node);
    }
}
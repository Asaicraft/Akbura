using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;

internal readonly struct SeparatedGreenSyntaxList<TNode> : IEquatable<SeparatedGreenSyntaxList<TNode>> where TNode : GreenNode
{
    private readonly GreenSyntaxList<GreenNode> _list;

    public SeparatedGreenSyntaxList(GreenSyntaxList<GreenNode> list)
    {
        Validate(list);
        _list = list;
    }

    [Conditional("DEBUG")]
    private static void Validate(GreenSyntaxList<GreenNode> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var item = list.GetRequiredItem(i);
            if ((i & 1) == 0)
            {
                Debug.Assert(!item.IsToken, "even elements of a separated list must be nodes");
            }
            else
            {
                Debug.Assert(item.IsToken, "odd elements of a separated list must be tokens");
            }
        }
    }

    public GreenNode? Node => _list.Node;

    public int Count => (_list.Count + 1) >> 1;

    public int SeparatorCount => _list.Count >> 1;

    public TNode? this[int index] => (TNode?)_list[index << 1];

    /// <summary>
    /// Gets the separator at the given index in this list.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns></returns>
    public GreenNode? GetSeparator(int index)
    {
        return _list[(index << 1) + 1];
    }

    public GreenSyntaxList<GreenNode> GetWithSeparators()
    {
        return _list;
    }

    public override string ToString()
    {
        return _list.ToString();
    }

    public string ToFullString()
    {
        return _list.ToFullString();
    }

    public static bool operator ==(in SeparatedGreenSyntaxList<TNode> left, in SeparatedGreenSyntaxList<TNode> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(in SeparatedGreenSyntaxList<TNode> left, in SeparatedGreenSyntaxList<TNode> right)
    {
        return !left.Equals(right);
    }

    public bool Equals(SeparatedGreenSyntaxList<TNode> other)
    {
        return _list == other._list;
    }

    public override bool Equals(object? obj)
    {
        return (obj is SeparatedGreenSyntaxList<TNode> list) && Equals(list);
    }

    public override int GetHashCode()
    {
        return _list.GetHashCode();
    }

    public static implicit operator SeparatedGreenSyntaxList<GreenNode>(SeparatedGreenSyntaxList<TNode> list)
    {
        return new SeparatedGreenSyntaxList<GreenNode>(list.GetWithSeparators());
    }

#if DEBUG

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    private TNode[] Nodes
    {
        get
        {
            var count = Count;
            var array = new TNode[count];
            for (var i = 0; i < count; i++)
            {
                array[i] = this[i]!;
            }
            return array;
        }
    }
#endif
}
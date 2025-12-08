using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;

[DebuggerDisplay("Count = {Count}, Nodes = {DebugView,nq}")]
internal readonly partial struct GreenChildSyntaxList
{
    private readonly GreenNode? _node;
    private readonly int _count;

    public GreenChildSyntaxList(GreenNode? node)
    {
        _node = node;
        _count = CountNodes();
    }

    public readonly int Count => _count;

    // for debugging
    private GreenNode[] Nodes
    {
        get
        {
            var result = new GreenNode[Count];
            var i = 0;

            foreach (var n in this)
            {
                result[i++] = n;
            }

            return result;
        }
    }

    // for debugging
    private string DebugView
    {
        get
        {
            var nodeStrings = new List<string>();
            foreach (var node in this)
            {
                nodeStrings.Add(node?.ToString() ?? "null");
            }
            return string.Join(", ", nodeStrings);
        }
    }

    private readonly int CountNodes()
    {
        var n = 0;
        var enumerator = GetEnumerator();
        while (enumerator.MoveNext())
        {
            n++;
        }

        return n;
    }

    public readonly Enumerator GetEnumerator()
    {
        return new Enumerator(_node);
    }
}

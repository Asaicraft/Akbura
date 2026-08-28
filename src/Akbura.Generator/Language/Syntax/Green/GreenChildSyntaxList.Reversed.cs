using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;

readonly partial struct GreenChildSyntaxList
{
    [DebuggerDisplay("Count = {Nodes.Length}, Nodes (Reversed) = {DebugView,nq}")]
    public readonly partial struct Reversed(GreenNode? node)
    {
        private readonly GreenNode? _node = node;

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_node);
        }

        // for debugging
        private GreenNode[] Nodes
        {
            get
            {
                var result = new List<GreenNode>();
                foreach (var n in this)
                {
                    result.Add(n);
                }

                return [.. result];
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

    }
}
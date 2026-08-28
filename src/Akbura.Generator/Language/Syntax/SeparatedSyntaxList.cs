using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura.Language.Syntax;

internal static class SeparatedSyntaxList
{
    public static SeparatedSyntaxList<TNode> Create<TNode>(ReadOnlySpan<TNode> nodes) where TNode : AkburaSyntax
    {
        if (nodes.Length == 0)
        {
            return default;
        }

        if (nodes.Length == 1)
        {
            return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(nodes[0], index: 0));
        }

        var builder = new SeparatedSyntaxListBuilder<TNode>(nodes.Length);

        builder.Add(nodes[0]);

        var separator = nodes[0].Green.CreateSeparator(nodes[0]);

        for (int i = 1, n = nodes.Length; i < n; i++)
        {
            builder.AddSeparator(separator);
            builder.Add(nodes[i]);
        }

        return builder.ToList();
    }
}

[CollectionBuilder(typeof(SeparatedSyntaxList), "Create")]
internal readonly partial struct SeparatedSyntaxList<TNode> : IEquatable<SeparatedSyntaxList<TNode>>, IReadOnlyList<TNode> where TNode : AkburaSyntax
{
    private readonly SyntaxNodeOrTokenList _list;
    private readonly int _count;
    private readonly int _separatorCount;

    public SeparatedSyntaxList(SyntaxNodeOrTokenList list)
        : this()
    {
        Validate(list);

        // calculating counts is very cheap when list interleaves nodes and tokens
        // so lets just do it here.

        var allCount = list.Count;
        _count = (allCount + 1) >> 1;
        _separatorCount = allCount >> 1;

        _list = list;
    }

    [Conditional("DEBUG")]
    private static void Validate(SyntaxNodeOrTokenList list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if ((i & 1) == 0)
            {
                Debug.Assert(item.IsNode, "Node missing in separated list.");
            }
            else
            {
                Debug.Assert(item.IsToken, "Separator token missing in separated list.");
            }
        }
    }

    public SeparatedSyntaxList(AkburaSyntax node, int index)
        : this(new SyntaxNodeOrTokenList(node, index))
    {
    }

    public AkburaSyntax? Node => _list.Node;

    public int Count => _count;

    public int SeparatorCount => _separatorCount;

    public TNode this[int index]
    {
        get
        {
            var node = _list.Node;
            if (node != null)
            {
                if (!node.IsList)
                {
                    if (index == 0)
                    {
                        return (TNode)node;
                    }
                }
                else
                {
                    if (unchecked((uint)index < (uint)_count))
                    {
                        return (TNode)node.GetRequiredNodeSlot(index << 1);
                    }
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Gets the separator at the given index in this list.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns></returns>
    public SyntaxToken GetSeparator(int index)
    {
        var node = _list.Node;
        if (node != null)
        {
            Debug.Assert(node.IsList, "separated list cannot be a singleton separator");
            if (unchecked((uint)index < (uint)_separatorCount))
            {
                index = (index << 1) + 1;
                var green = node.Green.GetRequiredSlot(index);
                Debug.Assert(green.IsToken);
                return new SyntaxToken(node.Parent, green, node.GetChildPosition(index), _list.index + index);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Returns the sequence of just the separator tokens.
    /// </summary>
    public IEnumerable<SyntaxToken> GetSeparators()
    {
        return _list.Where(n => n.IsToken).Select(n => n.AsToken());
    }

    /// <summary>
    /// The absolute span of the list elements in characters, including the leading and trailing trivia of the first and last elements.
    /// </summary>
    public TextSpan FullSpan => _list.FullSpan;

    /// <summary>
    /// The absolute span of the list elements in characters, not including the leading and trailing trivia of the first and last elements.
    /// </summary>
    public TextSpan Span => _list.Span;

    /// <summary>
    /// Returns the string representation of the nodes in this list including separators but not including 
    /// the first node's leading trivia and the last node or token's trailing trivia.
    /// </summary>
    /// <returns>
    /// The string representation of the nodes in this list including separators but not including 
    /// the first node's leading trivia and the last node or token's trailing trivia.
    /// </returns>
    public override string ToString()
    {
        return _list.ToString();
    }

    /// <summary>
    /// Returns the full string representation of the nodes in this list including separators, 
    /// the first node's leading trivia, and the last node or token's trailing trivia.
    /// </summary>
    /// <returns>
    /// The full string representation of the nodes in this list including separators including separators,
    /// the first node's leading trivia, and the last node or token's trailing trivia.
    /// </returns>
    public string ToFullString()
    {
        return _list.ToFullString();
    }

    public TNode First()
    {
        return this[0];
    }

    public TNode? FirstOrDefault()
    {
        if (Any())
        {
            return this[0];
        }

        return null;
    }

    public TNode Last()
    {
        return this[Count - 1];
    }

    public TNode? LastOrDefault()
    {
        if (Any())
        {
            return this[Count - 1];
        }

        return null;
    }

    public bool Contains(TNode node)
    {
        return IndexOf(node) >= 0;
    }

    public int IndexOf(TNode node)
    {
        for (int i = 0, n = Count; i < n; i++)
        {
            if (Equals(this[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    public int IndexOf(Func<TNode, bool> predicate)
    {
        for (int i = 0, n = Count; i < n; i++)
        {
            if (predicate(this[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public int IndexOf(ushort rawKind)
    {
        for (int i = 0, n = Count; i < n; i++)
        {
            if (this[i].RawKind == rawKind)
            {
                return i;
            }
        }

        return -1;
    }

    public int LastIndexOf(TNode node)
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            if (Equals(this[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    public int LastIndexOf(Func<TNode, bool> predicate)
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            if (predicate(this[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public bool Any()
    {
        return _list.Any();
    }

    public bool Any(Func<TNode, bool> predicate)
    {
        for (var i = 0; i < Count; i++)
        {
            if (predicate(this[i]))
            {
                return true;
            }
        }

        return false;
    }

    public SyntaxNodeOrTokenList GetWithSeparators()
    {
        return _list;
    }

    public static bool operator ==(SeparatedSyntaxList<TNode> left, SeparatedSyntaxList<TNode> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SeparatedSyntaxList<TNode> left, SeparatedSyntaxList<TNode> right)
    {
        return !left.Equals(right);
    }

    public bool Equals(SeparatedSyntaxList<TNode> other)
    {
        return _list == other._list;
    }

    public override bool Equals(object? obj)
    {
        return (obj is SeparatedSyntaxList<TNode> list) && Equals(list);
    }

    public override int GetHashCode()
    {
        return _list.GetHashCode();
    }

    /// <summary>
    /// Creates a new list with the specified node added to the end.
    /// </summary>
    /// <param name="node">The node to add.</param>
    public SeparatedSyntaxList<TNode> Add(TNode node)
    {
        return Insert(Count, node);
    }

    /// <summary>
    /// Creates a new list with the specified nodes added to the end.
    /// </summary>
    /// <param name="nodes">The nodes to add.</param>
    public SeparatedSyntaxList<TNode> AddRange(IEnumerable<TNode> nodes)
    {
        return InsertRange(Count, nodes);
    }

    /// <summary>
    /// Creates a new list with the specified node inserted at the index.
    /// </summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="node">The node to insert.</param>
    public SeparatedSyntaxList<TNode> Insert(int index, TNode node)
    {
        if(node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return InsertRange(index, [node]);
    }

    /// <summary>
    /// Creates a new list with the specified nodes inserted at the index.
    /// </summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="nodes">The nodes to insert.</param>
    public SeparatedSyntaxList<TNode> InsertRange(int index, IEnumerable<TNode> nodes)
    {
        if(nodes == null)
        {
            throw new ArgumentNullException(nameof(nodes));
        }

        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var nodesWithSeps = GetWithSeparators();
        var insertionIndex = index < Count ? nodesWithSeps.IndexOf(this[index]) : nodesWithSeps.Count;

        // determine how to deal with separators (commas)
        if (insertionIndex > 0 && insertionIndex < nodesWithSeps.Count)
        {
            var previous = nodesWithSeps[insertionIndex - 1];
            if (previous.IsToken && !KeepSeparatorWithPreviousNode(previous.AsToken()))
            {
                // pull back so item in inserted before separator
                insertionIndex--;
            }
        }

        var nodesToInsertWithSeparators = new List<SyntaxNodeOrToken>();
        foreach (var item in nodes)
        {
            if (item != null)
            {
                // if item before insertion point is a node, add a separator
                if (nodesToInsertWithSeparators.Count > 0 || (insertionIndex > 0 && nodesWithSeps[insertionIndex - 1].IsNode))
                {
                    nodesToInsertWithSeparators.Add(item.Green.CreateSeparator(item));
                }

                nodesToInsertWithSeparators.Add(item);
            }
        }

        // if item after last inserted node is a node, add separator
#pragma warning disable IDE0059 // Unnecessary assignment of a value
        if (insertionIndex < nodesWithSeps.Count && nodesWithSeps[insertionIndex] is { IsNode: true } nodeOrToken)
        {
            var node = nodesWithSeps[insertionIndex].AsNode();
            AkburaDebug.Assert(node is not null);
            nodesToInsertWithSeparators.Add(node.Green.CreateSeparator(node)); // separator
        }
#pragma warning restore IDE0059 // Unnecessary assignment of a value

        return new SeparatedSyntaxList<TNode>(nodesWithSeps.InsertRange(insertionIndex, nodesToInsertWithSeparators));
    }

    private static bool KeepSeparatorWithPreviousNode(in SyntaxToken separator)
    {
        // if the trivia after the separator contains an explicit end of line or a single line comment
        // then it should stay associated with previous node
        foreach (var tr in separator.TrailingTrivia)
        {
            AkburaDebug.Assert(tr.UnderlyingNode is not null);
            if (tr.UnderlyingNode.IsTriviaWithEndOfLine())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a new list with the element at the specified index removed.
    /// </summary>
    /// <param name="index">The index of the element to remove.</param>
    public SeparatedSyntaxList<TNode> RemoveAt(int index)
    {
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Remove(this[index]);
    }

    /// <summary>
    /// Creates a new list with specified element removed.
    /// </summary>
    /// <param name="node">The element to remove.</param>
    public SeparatedSyntaxList<TNode> Remove(TNode node)
    {
        var nodesWithSeps = GetWithSeparators();
        var index = nodesWithSeps.IndexOf(node);

        if (index >= 0 && index <= nodesWithSeps.Count)
        {
            nodesWithSeps = nodesWithSeps.RemoveAt(index);

            // remove separator too
            if (index < nodesWithSeps.Count && nodesWithSeps[index].IsToken)
            {
                nodesWithSeps = nodesWithSeps.RemoveAt(index);
            }
            else if (index > 0 && nodesWithSeps[index - 1].IsToken)
            {
                nodesWithSeps = nodesWithSeps.RemoveAt(index - 1);
            }

            return new SeparatedSyntaxList<TNode>(nodesWithSeps);
        }

        return this;
    }

    /// <summary>
    /// Creates a new list with the specified element replaced by the new node.
    /// </summary>
    /// <param name="nodeInList">The element to replace.</param>
    /// <param name="newNode">The new node.</param>
    public SeparatedSyntaxList<TNode> Replace(TNode nodeInList, TNode newNode)
    {
        if(newNode == null)
        {
            throw new ArgumentNullException(nameof(newNode));
        }

        var index = IndexOf(nodeInList);
        if (index >= 0 && index < Count)
        {
            return new SeparatedSyntaxList<TNode>(GetWithSeparators().Replace(nodeInList, newNode));
        }

        throw new ArgumentOutOfRangeException(nameof(nodeInList));
    }

    /// <summary>
    /// Creates a new list with the specified element replaced by the new nodes.
    /// </summary>
    /// <param name="nodeInList">The element to replace.</param>
    /// <param name="newNodes">The new nodes.</param>
    public SeparatedSyntaxList<TNode> ReplaceRange(TNode nodeInList, IEnumerable<TNode> newNodes)
    {
        if (newNodes == null)
        {
            throw new ArgumentNullException(nameof(newNodes));
        }

        var index = IndexOf(nodeInList);
        if (index >= 0 && index < Count)
        {
            var newNodeList = newNodes.ToList();
            if (newNodeList.Count == 0)
            {
                return Remove(nodeInList);
            }

            var listWithFirstReplaced = Replace(nodeInList, newNodeList[0]);

            if (newNodeList.Count > 1)
            {
                newNodeList.RemoveAt(0);
                return listWithFirstReplaced.InsertRange(index + 1, newNodeList);
            }

            return listWithFirstReplaced;
        }

        throw new ArgumentOutOfRangeException(nameof(nodeInList));
    }

    /// <summary>
    /// Creates a new list with the specified separator token replaced with the new separator.
    /// </summary>
    /// <param name="separatorToken">The separator token to be replaced.</param>
    /// <param name="newSeparator">The new separator token.</param>
    public SeparatedSyntaxList<TNode> ReplaceSeparator(SyntaxToken separatorToken, SyntaxToken newSeparator)
    {
        var nodesWithSeps = GetWithSeparators();
        var index = nodesWithSeps.IndexOf(separatorToken);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(separatorToken));
        }

        if (newSeparator.RawKind != nodesWithSeps[index].RawKind)
        {
            throw new ArgumentOutOfRangeException(nameof(newSeparator));
        }

        return new SeparatedSyntaxList<TNode>(nodesWithSeps.Replace(separatorToken, newSeparator));
    }

    // for debugging
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0305:Simplify collection initialization", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    private TNode[] Nodes => this.ToArray();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    private SyntaxNodeOrToken[] NodesWithSeparators => [.. _list];

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<TNode> IEnumerable<TNode>.GetEnumerator()
    {
        if (Any())
        {
            return new EnumeratorImpl(this);
        }

        return EmptyEnumerator.For<TNode>();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        if (Any())
        {
            return new EnumeratorImpl(this);
        }

        return EmptyEnumerator.For<TNode>();
    }

    public static implicit operator SeparatedSyntaxList<AkburaSyntax>(SeparatedSyntaxList<TNode> nodes)
    {
        return new SeparatedSyntaxList<AkburaSyntax>(nodes._list);
    }

    public static explicit operator SeparatedSyntaxList<TNode>(SeparatedSyntaxList<AkburaSyntax> nodes)
    {
        return new SeparatedSyntaxList<TNode>(nodes._list);
    }
}
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using Akbura.Collections;

namespace Akbura.Language.Syntax;

/// <summary>
/// A list of <see cref="SyntaxNodeOrToken"/> structures.
/// </summary>
[CollectionBuilder(typeof(SyntaxNodeOrTokenList), "Create")]
internal readonly struct SyntaxNodeOrTokenList : IEquatable<SyntaxNodeOrTokenList>, IReadOnlyCollection<SyntaxNodeOrToken>
{
    /// <summary>
    /// The underlying field
    /// </summary>
    private readonly AkburaSyntax? _node;

    /// <summary>
    /// The index from the parent's children list of this node.
    /// </summary>
    public readonly int index;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxNodeOrTokenList"/> structure.
    /// </summary>
    /// <param name="node">The underlying syntax node.</param>
    /// <param name="index">The index.</param>
    public SyntaxNodeOrTokenList(AkburaSyntax? node, int index)
        : this()
    {
        Debug.Assert(node != null || index == 0);
        if (node != null)
        {
            _node = node;
            this.index = index;
        }
    }

    /// <summary>
    /// Create a <see cref="SyntaxNodeOrTokenList"/> from a sequence of <see cref="SyntaxNodeOrToken"/>.
    /// </summary>
    /// <param name="nodesAndTokens">The sequence of nodes and tokens</param>
    public SyntaxNodeOrTokenList(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
        : this(CreateNode(nodesAndTokens), 0)
    {
    }

    /// <summary>
    /// Create a <see cref="SyntaxNodeOrTokenList"/> from one or more <see cref="SyntaxNodeOrToken"/>.
    /// </summary>
    /// <param name="nodesAndTokens">The nodes and tokens</param>
    public SyntaxNodeOrTokenList(params SyntaxNodeOrToken[] nodesAndTokens)
        : this(CreateNodeFromSpan(nodesAndTokens), 0)
    {
    }

    public static SyntaxNodeOrTokenList Create(ReadOnlySpan<SyntaxNodeOrToken> nodesAndTokens)
    {
        if (nodesAndTokens.Length == 0)
        {
            return default;
        }

        return new SyntaxNodeOrTokenList(CreateNodeFromSpan(nodesAndTokens), index: 0);
    }

    private static AkburaSyntax? CreateNodeFromSpan(ReadOnlySpan<SyntaxNodeOrToken> nodesAndTokens)
    {
        if (nodesAndTokens.IsEmpty)
        {
            throw new ArgumentNullException(nameof(nodesAndTokens));
        }

        switch (nodesAndTokens.Length)
        {
            case 0: return null;
            case 1:
                return nodesAndTokens[0].IsToken
                    ? GreenSyntaxList.List([nodesAndTokens[0].UnderlyingNode!]).CreateRed()
                    : nodesAndTokens[0].AsNode();
            case 2: return GreenSyntaxList.List(nodesAndTokens[0].UnderlyingNode!, nodesAndTokens[1].UnderlyingNode!).CreateRed();
            case 3: return GreenSyntaxList.List(nodesAndTokens[0].UnderlyingNode!, nodesAndTokens[1].UnderlyingNode!, nodesAndTokens[2].UnderlyingNode!).CreateRed();
            default:
            {
                var copy = new ArrayElement<GreenNode>[nodesAndTokens.Length];
                for (int i = 0, n = nodesAndTokens.Length; i < n; i++)
                {
                    copy[i].Value = nodesAndTokens[i].UnderlyingNode!;
                }

                return GreenSyntaxList.List(copy).CreateRed();
            }
        }
    }

    private static AkburaSyntax? CreateNode(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        if(nodesAndTokens == null)
        {
            throw new ArgumentNullException(nameof(nodesAndTokens));
        }

        var builder = new SyntaxNodeOrTokenListBuilder(8);
        builder.Add(nodesAndTokens);
        return builder.ToList().Node;
    }

    /// <summary>
    /// Gets the underlying syntax node.
    /// </summary>
    public AkburaSyntax? Node => _node;

    public int Position => _node?.Position ?? 0;

    public AkburaSyntax? Parent => _node?.Parent;

    /// <summary>
    /// Gets the count of nodes in this list
    /// </summary>
    public int Count => _node == null ? 0 : _node.Green.IsList ? _node.SlotCount : 1;

    /// <summary>
    /// Gets the <see cref="SyntaxNodeOrToken"/> at the specified index. 
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public SyntaxNodeOrToken this[int index]
    {
        get
        {
            if (_node != null)
            {
                if (!_node.IsList)
                {
                    if (index == 0)
                    {
                        return _node;
                    }
                }
                else
                {
                    if (unchecked((uint)index < (uint)_node.SlotCount))
                    {
                        var green = _node.Green.GetRequiredSlot(index);
                        if (green.IsToken)
                        {
                            return new SyntaxToken(Parent, green, _node.GetChildPosition(index), index + index);
                        }

                        return _node.GetRequiredNodeSlot(index);
                    }
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// The absolute span of the list elements in characters, including the leading and trailing trivia of the first and last elements.
    /// </summary>
    public TextSpan FullSpan => _node?.FullSpan ?? default;

    /// <summary>
    /// The absolute span of the list elements in characters, not including the leading and trailing trivia of the first and last elements.
    /// </summary>
    public TextSpan Span => _node?.Span ?? default;

    /// <summary>
    /// Returns the string representation of the nodes and tokens in this list, not including the first node or token's leading trivia 
    /// and the last node or token's trailing trivia.
    /// </summary>
    /// <returns>
    /// The string representation of the nodes and tokens in this list, not including the first node or token's leading trivia 
    /// and the last node or token's trailing trivia.
    /// </returns>
    public override string ToString()
    {
        return _node != null
            ? _node.ToString()
            : string.Empty;
    }

    /// <summary>
    /// Returns the full string representation of the nodes and tokens in this list including the first node or token's leading trivia 
    /// and the last node or token's trailing trivia.
    /// </summary>
    /// <returns>
    /// The full string representation of the nodes and tokens in this list including the first node or token's leading trivia 
    /// and the last node or token's trailing trivia.
    /// </returns>
    public string ToFullString()
    {
        return _node != null
            ? _node.ToFullString()
            : string.Empty;
    }

    /// <summary>
    /// Gets the first SyntaxNodeOrToken structure from this list.
    /// </summary>
    public SyntaxNodeOrToken First()
    {
        return this[0];
    }

    /// <summary>
    /// Gets the first SyntaxNodeOrToken structure from this list if present, else default(SyntaxNodeOrToken).
    /// </summary>
    public SyntaxNodeOrToken FirstOrDefault()
    {
        return Any()
            ? this[0]
            : default;
    }

    /// <summary>
    /// Gets the last SyntaxNodeOrToken structure from this list.
    /// </summary>
    public SyntaxNodeOrToken Last()
    {
        return this[^1];
    }

    /// <summary>
    /// Gets the last SyntaxNodeOrToken structure from this list if present, else default(SyntaxNodeOrToken).
    /// </summary>
    public SyntaxNodeOrToken LastOrDefault()
    {
        return Any()
            ? this[Count - 1]
            : default;
    }

    /// <summary>
    /// Returns the index from the list for the given <see cref="SyntaxNodeOrToken"/>.
    /// </summary>
    /// <param name="nodeOrToken">The node or token to search for in the list.</param>
    /// <returns>The index of the found nodeOrToken, or -1 if it wasn't found</returns>
    public int IndexOf(SyntaxNodeOrToken nodeOrToken)
    {
        var i = 0;
        foreach (var child in this)
        {
            if (child == nodeOrToken)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    /// <summary>
    /// Indicates whether there is any element in the list.
    /// </summary>
    /// <returns><c>true</c> if there are any elements in the list, else <c>false</c>.</returns>
    public bool Any()
    {
        return _node != null;
    }

    /// <summary>
    /// Copies a given count of elements into the given array at specified offsets.
    /// </summary>
    /// <param name="offset">The offset to start copying from.</param>
    /// <param name="array">The array to copy the elements into.</param>
    /// <param name="arrayOffset">The array offset to start writing to.</param>
    /// <param name="count">The count of elements to copy.</param>
    public void CopyTo(int offset, GreenNode?[] array, int arrayOffset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            array[arrayOffset + i] = this[i + offset].UnderlyingNode;
        }
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified node or token added to the end.
    /// </summary>
    /// <param name="nodeOrToken">The node or token to add.</param>
    public SyntaxNodeOrTokenList Add(SyntaxNodeOrToken nodeOrToken)
    {
        return Insert(Count, nodeOrToken);
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified nodes or tokens added to the end.
    /// </summary>
    /// <param name="nodesOrTokens">The nodes or tokens to add.</param>
    public SyntaxNodeOrTokenList AddRange(IEnumerable<SyntaxNodeOrToken> nodesOrTokens)
    {
        return InsertRange(Count, nodesOrTokens);
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified node or token inserted at the index.
    /// </summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="nodeOrToken">The node or token to insert.</param>
    public SyntaxNodeOrTokenList Insert(int index, SyntaxNodeOrToken nodeOrToken)
    {
        if(nodeOrToken == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeOrToken));
        }

        return InsertRange(index, SpecializedCollections.SingletonEnumerable(nodeOrToken));
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified nodes or tokens inserted at the index.
    /// </summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="nodesAndTokens">The nodes or tokens to insert.</param>
    public SyntaxNodeOrTokenList InsertRange(int index, IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if(nodesAndTokens == null)
        {
            throw new ArgumentNullException(nameof(nodesAndTokens));
        }

        if (nodesAndTokens.IsEmpty())
        {
            return this;
        }

        var nodes = this.ToList();
        nodes.InsertRange(index, nodesAndTokens);
        return CreateList(nodes);
    }

    private static SyntaxNodeOrTokenList CreateList(List<SyntaxNodeOrToken> items)
    {
        if (items.Count == 0)
        {
            return default;
        }

        var newGreen = GreenNode.CreateList(items, static n => n.RequiredUnderlyingNode)!;
        if (newGreen.IsToken)
        {
            newGreen = GreenSyntaxList.List(
            [
                 new ArrayElement<GreenNode> {Value = newGreen}
             ]);
        }

        return new SyntaxNodeOrTokenList(newGreen.CreateRed(), 0);
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the element at the specified index removed.
    /// </summary>
    /// <param name="index">The index of the element to remove.</param>
    public SyntaxNodeOrTokenList RemoveAt(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var nodes = this.ToList();
        nodes.RemoveAt(index);
        return CreateList(nodes);
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified element removed.
    /// </summary>
    /// <param name="nodeOrTokenInList">The element to remove.</param>
    public SyntaxNodeOrTokenList Remove(SyntaxNodeOrToken nodeOrTokenInList)
    {
        var index = IndexOf(nodeOrTokenInList);
        if (index >= 0 && index < Count)
        {
            return RemoveAt(index);
        }

        return this;
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified element replaced with a new node or token.
    /// </summary>
    /// <param name="nodeOrTokenInList">The element to replace.</param>
    /// <param name="newNodeOrToken">The new node or token.</param>
    public SyntaxNodeOrTokenList Replace(SyntaxNodeOrToken nodeOrTokenInList, SyntaxNodeOrToken newNodeOrToken)
    {
        if(nodeOrTokenInList == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeOrTokenInList));
        }

        return ReplaceRange(nodeOrTokenInList, [newNodeOrToken]);
    }

    /// <summary>
    /// Creates a new <see cref="SyntaxNodeOrTokenList"/> with the specified element replaced with a new nodes and tokens.
    /// </summary>
    /// <param name="nodeOrTokenInList">The element to replace.</param>
    /// <param name="newNodesAndTokens">The new nodes and tokens.</param>
    public SyntaxNodeOrTokenList ReplaceRange(SyntaxNodeOrToken nodeOrTokenInList, IEnumerable<SyntaxNodeOrToken> newNodesAndTokens)
    {
        var index = IndexOf(nodeOrTokenInList);
        if (index >= 0 && index < Count)
        {
            var nodes = this.ToList();
            nodes.RemoveAt(index);
            nodes.InsertRange(index, newNodesAndTokens);
            return CreateList(nodes);
        }

        throw new ArgumentOutOfRangeException(nameof(nodeOrTokenInList));
    }

    // for debugging
    [SuppressMessage("Style", "IDE0305:Simplify collection initialization", Justification = "<Pending>")]
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    private SyntaxNodeOrToken[] Nodes => this.ToArray();

    /// <summary>
    /// Gets the enumerator.
    /// </summary>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>
    /// A <see cref="IEnumerator{T}"/> that can be used to iterate through the collection.
    /// </returns>
    IEnumerator<SyntaxNodeOrToken> IEnumerable<SyntaxNodeOrToken>.GetEnumerator()
    {
        return _node == null
            ? EmptyEnumerator.For<SyntaxNodeOrToken>()
            : GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>
    /// An <see cref="IEnumerator"/> object that can be used to iterate through the collection.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return _node == null
            ? EmptyEnumerator.For<SyntaxNodeOrToken>()
            : GetEnumerator();
    }

    /// <summary>
    /// Implements the operator ==.
    /// </summary>
    /// <param name="left">The left SyntaxNodeOrTokenList</param>
    /// <param name="right">The right SyntaxNodeOrTokenList</param>
    /// <returns>
    /// <c>true</c> if both lists equal, else <c>false</c>.
    /// </returns>
    public static bool operator ==(SyntaxNodeOrTokenList left, SyntaxNodeOrTokenList right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Implements the operator !=.
    /// </summary>
    /// <param name="left">The left SyntaxNodeOrTokenList</param>
    /// <param name="right">The right SyntaxNodeOrTokenList</param>
    /// <returns>
    /// <c>true</c> if both lists not equal, else <c>false</c>.
    /// </returns>
    public static bool operator !=(SyntaxNodeOrTokenList left, SyntaxNodeOrTokenList right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Indicates whether the current object is equal to another object of the same type.
    /// </summary>
    /// <param name="other">An object to compare with this object.</param>
    /// <returns>
    /// <c>true</c> if the current object is equal to the <paramref name="other"/> parameter; otherwise,
    /// <c>false</c>.
    /// </returns>
    public bool Equals(SyntaxNodeOrTokenList other)
    {
        return _node == other._node;
    }

    /// <summary>
    /// Determines whether the specified <see cref="object"/> is equal to this instance.
    /// </summary>
    /// <param name="obj">The <see cref="object"/> to compare with this instance.</param>
    /// <returns>
    ///   <c>true</c> if the specified <see cref="object"/> is equal to this instance; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return obj is SyntaxNodeOrTokenList list && Equals(list);
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>
    /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
    /// </returns>
    public override int GetHashCode()
    {
        return _node?.GetHashCode() ?? 0;
    }

    public SeparatedSyntaxList<TOther> AsSeparatedList<TOther>() where TOther : AkburaSyntax
    {
        var builder = SeparatedSyntaxListBuilder<TOther>.Create();
        var list = this;

        foreach (var i in list)
        {
            var node = i.AsNode();
            if (node != null)
            {
                builder.Add((TOther)node);
            }
            else
            {
                builder.AddSeparator(i.AsToken());
            }
        }

        return builder.ToList();
    }

    /// <summary>
    /// Enumerator for lists of SyntaxNodeOrToken structs.
    /// </summary>
    [SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "<Pending>")]
    [SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression", Justification = "<Pending>")]
    public struct Enumerator : IEnumerator<SyntaxNodeOrToken>
    {
        private readonly SyntaxNodeOrTokenList _list;
        private int _index;

        public Enumerator(in SyntaxNodeOrTokenList list)
            : this()
        {
            _list = list;
            _index = -1;
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
        /// </returns>
        /// <exception cref="InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        public bool MoveNext()
        {
            if (_index < _list.Count)
            {
                _index++;
            }

            return _index < _list.Count;
        }

        /// <summary>
        /// Gets the struct that this enumerator instance is currently pointing to.
        /// </summary>
        public readonly SyntaxNodeOrToken Current => _list[_index];

        /// <summary>
        /// Gets the struct that this enumerator instance is currently pointing to.
        /// </summary>
        readonly object IEnumerator.Current => Current;

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        /// <exception cref="InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        readonly void IEnumerator.Reset()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        readonly void IDisposable.Dispose()
        {
        }

        public override bool Equals(object? obj)
        {
            throw new NotSupportedException();
        }

        public override int GetHashCode()
        {
            throw new NotSupportedException();
        }
    }
}
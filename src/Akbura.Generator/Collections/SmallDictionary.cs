using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Akbura.Collections;

/// <summary>
/// Dictionary designed to hold a small number of items.
/// </summary>
internal sealed class SmallDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private AvlNode? _root;

    public static readonly SmallDictionary<TKey, TValue> Empty = new(null!);

    public SmallDictionary()
        : this(EqualityComparer<TKey>.Default)
    {
    }

    public SmallDictionary(IEqualityComparer<TKey> comparer)
    {
        Comparer = comparer;
    }

    public SmallDictionary(SmallDictionary<TKey, TValue> other, IEqualityComparer<TKey> comparer)
        : this(comparer)
    {
        foreach (var item in other)
        {
            Add(item.Key, item.Value);
        }
    }

    public IEqualityComparer<TKey> Comparer { get; }

    public KeyCollection Keys => new(this);

    public ValueCollection Values => new(this);

    public TValue this[TKey key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"Could not find key {key}");
            }

            return value;
        }

        set => Insert(GetHashCode(key), key, value, add: false);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(returnValue: false)] out TValue value)
    {
        if (_root != null)
        {
            return TryGetValue(GetHashCode(key), key, out value);
        }

        value = default;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        Insert(GetHashCode(key), key, value, add: true);
    }

    public bool ContainsKey(TKey key)
    {
        return TryGetValue(key, out _);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        return new EnumerableImpl(GetEnumerator());
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new EnumerableImpl(GetEnumerator());
    }

    [Conditional("DEBUG")]
    internal void AssertBalanced()
    {
#if DEBUG
        AvlNode.AssertBalanced(_root);
#endif
    }

    private bool CompareKeys(TKey left, TKey right)
    {
        return Comparer.Equals(left, right);
    }

    private int GetHashCode(TKey key)
    {
        return Comparer.GetHashCode(key);
    }

    private bool TryGetValue(
        int hashCode,
        TKey key,
        [MaybeNullWhen(returnValue: false)] out TValue value)
    {
        Debug.Assert(_root != null);
        var node = _root!;

        do
        {
            if (node.HashCode > hashCode)
            {
                node = node.Left;
            }
            else if (node.HashCode < hashCode)
            {
                node = node.Right;
            }
            else
            {
                goto hasBucket;
            }
        }
        while (node != null);

        value = default;
        return false;

hasBucket:
        if (CompareKeys(node.Key, key))
        {
            value = node.Value;
            return true;
        }

        return GetFromList(node.Next, key, out value);
    }

    private bool GetFromList(
        Node? next,
        TKey key,
        [MaybeNullWhen(returnValue: false)] out TValue value)
    {
        while (next != null)
        {
            if (CompareKeys(key, next.Key))
            {
                value = next.Value;
                return true;
            }

            next = next.Next;
        }

        value = default;
        return false;
    }

    private void Insert(
        int hashCode,
        TKey key,
        TValue value,
        bool add)
    {
        var currentNode = _root;
        if (currentNode == null)
        {
            _root = new AvlNode(hashCode, key, value);
            return;
        }

        AvlNode? currentNodeParent = null;
        var unbalanced = currentNode;
        AvlNode? unbalancedParent = null;

        for (;;)
        {
            var currentHashCode = currentNode.HashCode;
            if (currentNode.Balance != 0)
            {
                unbalancedParent = currentNodeParent;
                unbalanced = currentNode;
            }

            if (currentHashCode > hashCode)
            {
                if (currentNode.Left == null)
                {
                    var previousNode = currentNode;
                    currentNode = new AvlNode(hashCode, key, value);
                    previousNode.Left = currentNode;
                    break;
                }

                currentNodeParent = currentNode;
                currentNode = currentNode.Left;
            }
            else if (currentHashCode < hashCode)
            {
                if (currentNode.Right == null)
                {
                    var previousNode = currentNode;
                    currentNode = new AvlNode(hashCode, key, value);
                    previousNode.Right = currentNode;
                    break;
                }

                currentNodeParent = currentNode;
                currentNode = currentNode.Right;
            }
            else
            {
                HandleInsert(currentNode, currentNodeParent, key, value, add);
                return;
            }
        }

        Debug.Assert(!ReferenceEquals(unbalanced, currentNode));

        var node = unbalanced;
        do
        {
            Debug.Assert(node.HashCode != hashCode);
            if (node.HashCode < hashCode)
            {
                node.Balance--;
                node = node.Right!;
            }
            else
            {
                node.Balance++;
                node = node.Left!;
            }
        }
        while (!ReferenceEquals(node, currentNode));

        AvlNode rotated;
        var balance = unbalanced.Balance;
        if (balance == -2)
        {
            rotated = unbalanced.Right!.Balance < 0
                ? LeftSimple(unbalanced)
                : LeftComplex(unbalanced);
        }
        else if (balance == 2)
        {
            rotated = unbalanced.Left!.Balance > 0
                ? RightSimple(unbalanced)
                : RightComplex(unbalanced);
        }
        else
        {
            return;
        }

        if (unbalancedParent == null)
        {
            _root = rotated;
        }
        else if (ReferenceEquals(unbalanced, unbalancedParent.Left))
        {
            unbalancedParent.Left = rotated;
        }
        else
        {
            unbalancedParent.Right = rotated;
        }
    }

    private static AvlNode LeftSimple(AvlNode unbalanced)
    {
        Debug.Assert(unbalanced.Right != null);
        var right = unbalanced.Right!;
        unbalanced.Right = right.Left;
        right.Left = unbalanced;

        unbalanced.Balance = 0;
        right.Balance = 0;
        return right;
    }

    private static AvlNode RightSimple(AvlNode unbalanced)
    {
        Debug.Assert(unbalanced.Left != null);
        var left = unbalanced.Left!;
        unbalanced.Left = left.Right;
        left.Right = unbalanced;

        unbalanced.Balance = 0;
        left.Balance = 0;
        return left;
    }

    private static AvlNode LeftComplex(AvlNode unbalanced)
    {
        Debug.Assert(unbalanced.Right != null);
        var right = unbalanced.Right!;
        Debug.Assert(right.Left != null);
        var rightLeft = right.Left!;
        right.Left = rightLeft.Right;
        rightLeft.Right = right;
        unbalanced.Right = rightLeft.Left;
        rightLeft.Left = unbalanced;

        var rightLeftBalance = rightLeft.Balance;
        rightLeft.Balance = 0;

        if (rightLeftBalance < 0)
        {
            right.Balance = 0;
            unbalanced.Balance = 1;
        }
        else
        {
            right.Balance = (sbyte)-rightLeftBalance;
            unbalanced.Balance = 0;
        }

        return rightLeft;
    }

    private static AvlNode RightComplex(AvlNode unbalanced)
    {
        Debug.Assert(unbalanced.Left != null);
        var left = unbalanced.Left!;
        Debug.Assert(left.Right != null);
        var leftRight = left.Right!;
        left.Right = leftRight.Left;
        leftRight.Left = left;
        unbalanced.Left = leftRight.Right;
        leftRight.Right = unbalanced;

        var leftRightBalance = leftRight.Balance;
        leftRight.Balance = 0;

        if (leftRightBalance < 0)
        {
            left.Balance = 1;
            unbalanced.Balance = 0;
        }
        else
        {
            left.Balance = 0;
            unbalanced.Balance = (sbyte)-leftRightBalance;
        }

        return leftRight;
    }

    private void HandleInsert(
        AvlNode node,
        AvlNode? parent,
        TKey key,
        TValue value,
        bool add)
    {
        Node? currentNode = node;
        do
        {
            if (CompareKeys(currentNode.Key, key))
            {
                if (add)
                {
                    throw new InvalidOperationException();
                }

                currentNode.Value = value;
                return;
            }

            currentNode = currentNode.Next;
        }
        while (currentNode != null);

        AddNode(node, parent, key, value);
    }

    private void AddNode(
        AvlNode node,
        AvlNode? parent,
        TKey key,
        TValue value)
    {
        if (node is AvlNodeHead head)
        {
            head.NextNode = new NodeLinked(key, value, head.NextNode);
            return;
        }

        var newHead = new AvlNodeHead(node.HashCode, key, value, node)
        {
            Balance = node.Balance,
            Left = node.Left,
            Right = node.Right,
        };

        if (parent == null)
        {
            _root = newHead;
            return;
        }

        if (ReferenceEquals(node, parent.Left))
        {
            parent.Left = newHead;
        }
        else
        {
            parent.Right = newHead;
        }
    }

    private int HeightApprox()
    {
        var height = 0;
        var current = _root;
        while (current != null)
        {
            height++;
            current = current.Left;
        }

        return height + height / 2;
    }

    private abstract class Node
    {
        protected Node(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        public TKey Key { get; }

        public TValue Value { get; set; }

        public virtual Node? Next => null;
    }

    private sealed class NodeLinked : Node
    {
        public NodeLinked(TKey key, TValue value, Node next)
            : base(key, value)
        {
            Next = next;
        }

        public override Node Next { get; }
    }

    private abstract class HashedNode : Node
    {
        protected HashedNode(int hashCode, TKey key, TValue value)
            : base(key, value)
        {
            HashCode = hashCode;
        }

        public int HashCode { get; }

        public sbyte Balance;
    }

    private class AvlNode : HashedNode
    {
        public AvlNode(int hashCode, TKey key, TValue value)
            : base(hashCode, key, value)
        {
        }

        public AvlNode? Left;

        public AvlNode? Right;

#if DEBUG
        public static int AssertBalanced(AvlNode? node)
        {
            if (node == null)
            {
                return 0;
            }

            var leftHeight = AssertBalanced(node.Left);
            var rightHeight = AssertBalanced(node.Right);
            if (leftHeight - rightHeight != node.Balance ||
                Math.Abs(leftHeight - rightHeight) >= 2)
            {
                throw new InvalidOperationException();
            }

            return 1 + Math.Max(leftHeight, rightHeight);
        }
#endif
    }

    private sealed class AvlNodeHead : AvlNode
    {
        public AvlNodeHead(int hashCode, TKey key, TValue value, Node next)
            : base(hashCode, key, value)
        {
            NextNode = next;
        }

        public Node NextNode;

        public override Node Next => NextNode;
    }

    public readonly struct KeyCollection : IEnumerable<TKey>
    {
        private readonly SmallDictionary<TKey, TValue> _dictionary;

        public KeyCollection(SmallDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_dictionary);
        }

        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
        {
            return new EnumerableImpl(GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new EnumerableImpl(GetEnumerator());
        }

        public struct Enumerator
        {
            private readonly Stack<AvlNode>? _stack;
            private Node? _next;
            private Node? _current;

            public Enumerator(SmallDictionary<TKey, TValue> dictionary)
                : this()
            {
                var root = dictionary._root;
                if (root == null)
                {
                    return;
                }

                if (ReferenceEquals(root.Left, root.Right))
                {
                    _next = root;
                }
                else
                {
                    _stack = new Stack<AvlNode>(dictionary.HeightApprox());
                    _stack.Push(root);
                }
            }

            public TKey Current => _current!.Key;

            public bool MoveNext()
            {
                if (_next != null)
                {
                    _current = _next;
                    _next = _next.Next;
                    return true;
                }

                if (_stack == null || _stack.Count == 0)
                {
                    return false;
                }

                var current = _stack.Pop();
                _current = current;
                _next = current.Next;

                PushIfNotNull(current.Left);
                PushIfNotNull(current.Right);
                return true;
            }

            private void PushIfNotNull(AvlNode? child)
            {
                if (child != null)
                {
                    _stack!.Push(child);
                }
            }
        }

        private sealed class EnumerableImpl : IEnumerator<TKey>
        {
            private Enumerator _enumerator;

            public EnumerableImpl(Enumerator enumerator)
            {
                _enumerator = enumerator;
            }

            public TKey Current => _enumerator.Current;

            object? IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }
    }

    public readonly struct ValueCollection : IEnumerable<TValue>
    {
        private readonly SmallDictionary<TKey, TValue> _dictionary;

        public ValueCollection(SmallDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_dictionary);
        }

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return new EnumerableImpl(GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new EnumerableImpl(GetEnumerator());
        }

        public struct Enumerator
        {
            private readonly Stack<AvlNode>? _stack;
            private Node? _next;
            private Node? _current;

            public Enumerator(SmallDictionary<TKey, TValue> dictionary)
                : this()
            {
                var root = dictionary._root;
                if (root == null)
                {
                    return;
                }

                if (ReferenceEquals(root.Left, root.Right))
                {
                    _next = root;
                }
                else
                {
                    _stack = new Stack<AvlNode>(dictionary.HeightApprox());
                    _stack.Push(root);
                }
            }

            public TValue Current => _current!.Value;

            public bool MoveNext()
            {
                if (_next != null)
                {
                    _current = _next;
                    _next = _next.Next;
                    return true;
                }

                if (_stack == null || _stack.Count == 0)
                {
                    return false;
                }

                var current = _stack.Pop();
                _current = current;
                _next = current.Next;

                PushIfNotNull(current.Left);
                PushIfNotNull(current.Right);
                return true;
            }

            private void PushIfNotNull(AvlNode? child)
            {
                if (child != null)
                {
                    _stack!.Push(child);
                }
            }
        }

        private sealed class EnumerableImpl : IEnumerator<TValue>
        {
            private Enumerator _enumerator;

            public EnumerableImpl(Enumerator enumerator)
            {
                _enumerator = enumerator;
            }

            public TValue Current => _enumerator.Current;

            object? IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }
    }

    public struct Enumerator
    {
        private readonly Stack<AvlNode>? _stack;
        private Node? _next;
        private Node? _current;

        public Enumerator(SmallDictionary<TKey, TValue> dictionary)
            : this()
        {
            var root = dictionary._root;
            if (root == null)
            {
                return;
            }

            if (ReferenceEquals(root.Left, root.Right))
            {
                _next = root;
            }
            else
            {
                _stack = new Stack<AvlNode>(dictionary.HeightApprox());
                _stack.Push(root);
            }
        }

        public KeyValuePair<TKey, TValue> Current => new(_current!.Key, _current.Value);

        public bool MoveNext()
        {
            if (_next != null)
            {
                _current = _next;
                _next = _next.Next;
                return true;
            }

            if (_stack == null || _stack.Count == 0)
            {
                return false;
            }

            var current = _stack.Pop();
            _current = current;
            _next = current.Next;

            PushIfNotNull(current.Left);
            PushIfNotNull(current.Right);
            return true;
        }

        private void PushIfNotNull(AvlNode? child)
        {
            if (child != null)
            {
                _stack!.Push(child);
            }
        }
    }

    private sealed class EnumerableImpl : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private Enumerator _enumerator;

        public EnumerableImpl(Enumerator enumerator)
        {
            _enumerator = enumerator;
        }

        public KeyValuePair<TKey, TValue> Current => _enumerator.Current;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }
}

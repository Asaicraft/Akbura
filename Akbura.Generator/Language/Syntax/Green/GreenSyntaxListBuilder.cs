using Akbura.Collections;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CSharpTrivia = Microsoft.CodeAnalysis.SyntaxTrivia;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Akbura.Pools;

namespace Akbura.Language.Syntax.Green;
internal sealed class GreenSyntaxListBuilder
{
    private ArrayElement<GreenNode?>[] _nodes;
    public int Count { get; private set; }
    public int Capacity => _nodes.Length;

    public GreenSyntaxListBuilder(int size)
    {
        _nodes = new ArrayElement<GreenNode?>[size];
    }

    public static GreenSyntaxListBuilder Create()
    {
        return new GreenSyntaxListBuilder(8);
    }

    public void Clear()
    {
        Array.Clear(_nodes, 0, _nodes.Length);
        Count = 0;
    }

    public GreenNode? this[int index]
    {
        get => _nodes[index];
        set => _nodes[index].Value = value;
    }

    public void Add(GreenNode? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.IsList)
        {
            var slotCount = item.SlotCount;

            // Necessary, but not sufficient (e.g. for nested lists).
            EnsureAdditionalCapacity(slotCount);

            for (var i = 0; i < slotCount; i++)
            {
                Add(item.GetSlot(i));
            }
        }
        else
        {
            EnsureAdditionalCapacity(1);

            _nodes[Count++].Value = item;
        }
    }

    public void AddRange(GreenNode[] items)
    {
        AddRange(items, 0, items.Length);
    }

    public void AddRange(GreenNode[] items, int offset, int length)
    {
        // Necessary, but not sufficient (e.g. for nested lists).
        EnsureAdditionalCapacity(length - offset);

        var oldCount = Count;

        for (var i = offset; i < length; i++)
        {
            Add(items[i]);
        }

        Validate(oldCount, Count);
    }

    [Conditional("DEBUG")]
    private void Validate(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            Debug.Assert(_nodes[i].Value != null);
        }
    }

    public GreenSyntaxListBuilder AddRange(GreenSyntaxList<GreenNode> list)
    {
        AddRange(list, 0, list.Count);
        return this;
    }

    public void AddRange(GreenSyntaxList<GreenNode> list, int offset, int length)
    {
        // Necessary, but not sufficient (e.g. for nested lists).
        EnsureAdditionalCapacity(length - offset);

        var oldCount = Count;

        for (var i = offset; i < length; i++)
        {
            Add(list[i]);
        }

        Validate(oldCount, Count);
    }

    public void AddRange<TNode>(GreenSyntaxList<TNode> list) where TNode : GreenNode
    {
        AddRange(list, 0, list.Count);
    }

    public void AddRange<TNode>(GreenSyntaxList<TNode> list, int offset, int length) where TNode : GreenNode
    {
        AddRange(new GreenSyntaxList<GreenNode>(list.Node), offset, length);
    }

    public void RemoveLast()
    {
        Count--;
        _nodes[Count].Value = null;
    }

    private void EnsureAdditionalCapacity(int additionalCount)
    {
        var currentSize = _nodes.Length;
        var requiredSize = Count + additionalCount;

        if (requiredSize <= currentSize)
        {
            return;
        }

        var newSize =
            requiredSize < 8 ? 8 :
            requiredSize >= (int.MaxValue / 2) ? int.MaxValue :
            Math.Max(requiredSize, currentSize * 2); // NB: Size will *at least* double.
        Debug.Assert(newSize >= requiredSize);

        Array.Resize(ref _nodes, newSize);
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

    public GreenNode[] ToArray()
    {
        var array = new GreenNode[Count];
        for (var i = 0; i < array.Length; i++)
        {
            array[i] = _nodes[i]!;
        }

        return array;
    }

    public GreenNode? ToListNode()
    {
        switch (Count)
        {
            case 0:
                return null;
            case 1:
                return _nodes[0];
            case 2:
                return GreenSyntaxList.List(_nodes[0]!, _nodes[1]!);
            case 3:
                return GreenSyntaxList.List(_nodes[0]!, _nodes[1]!, _nodes[2]!);
            default:
                var tmp = new ArrayElement<GreenNode>[Count];
                Array.Copy(_nodes, tmp, Count);
                return GreenSyntaxList.List(tmp);
        }
    }

    public GreenSyntaxList<GreenNode> ToList()
    {
        return new GreenSyntaxList<GreenNode>(ToListNode());
    }

    public GreenSyntaxList<TNode> ToList<TNode>() where TNode : GreenNode
    {
        return new GreenSyntaxList<TNode>(ToListNode());
    }

    public string ToFullString()
    {
        var stringBuilder = PooledStringBuilder.GetInstance();
        var writer = new StringWriter(stringBuilder.Builder);

        for (var i = 0; i < Count; i++)
        {
            var triviaNode = _nodes[i].Value!;

            AkburaDebug.AssertNotNull(triviaNode);

            triviaNode.WriteTo(writer);
        }

        return stringBuilder.ToStringAndFree();
    }

    public Microsoft.CodeAnalysis.SyntaxTriviaList ToCSharpSyntaxTriviaArray(bool leading)
    {
        var fullString = ToFullString();

        return leading 
            ? CSharpFactory.ParseLeadingTrivia(fullString)
            : CSharpFactory.ParseTrailingTrivia(fullString);
    }
}
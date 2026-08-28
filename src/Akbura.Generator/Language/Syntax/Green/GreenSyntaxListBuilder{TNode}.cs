using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal readonly struct GreenSyntaxListBuilder<TNode> where TNode : GreenNode
{
    private readonly GreenSyntaxListBuilder _builder;

    public GreenSyntaxListBuilder(int size)
        : this(new GreenSyntaxListBuilder(size))
    {
    }

    public static GreenSyntaxListBuilder<TNode> Create()
    {
        return new GreenSyntaxListBuilder<TNode>(8);
    }

    public GreenSyntaxListBuilder(GreenSyntaxListBuilder builder)
    {
        _builder = builder;
    }

    public bool IsNull => _builder == null;

    public int Count => _builder.Count;

    public TNode this[int index]
    {
        get
        {
            // We only allow assigning non-null nodes into us, and .Add filters null out.  So we should never get null here.
            var result = _builder[index];
            Debug.Assert(result != null);
            return (TNode)result!;
        }
        set
        {
            _builder[index] = value;
        }
    }

    public void Clear()
    {
        _builder.Clear();
    }

    /// <summary>
    /// Adds <paramref name="node"/> to the end of this builder.  No change happens if <see langword="null"/> is
    /// passed in.
    /// </summary>
    public GreenSyntaxListBuilder<TNode> Add(TNode? node)
    {
        _builder.Add(node);
        return this;
    }

    public void AddRange(TNode[] items, int offset, int length)
    {
        _builder.AddRange(items, offset, length);
    }

    public void AddRange(GreenSyntaxList<TNode> nodes)
    {
        _builder.AddRange(nodes);
    }

    public void AddRange(GreenSyntaxList<TNode> nodes, int offset, int length)
    {
        _builder.AddRange(nodes, offset, length);
    }

    public bool Any(int kind)
    {
        return _builder.Any(kind);
    }

    public GreenSyntaxList<TNode> ToList()
    {
        return _builder.ToList();
    }

    public GreenNode? ToListNode()
    {
        return _builder.ToListNode();
    }

    public static implicit operator GreenSyntaxListBuilder(GreenSyntaxListBuilder<TNode> builder)
    {
        return builder._builder;
    }

    public static implicit operator GreenSyntaxList<TNode>(GreenSyntaxListBuilder<TNode> builder)
    {
        if (builder._builder != null)
        {
            return builder.ToList();
        }

        return default;
    }

    public GreenSyntaxList<TDerived> ToList<TDerived>() where TDerived : GreenNode
    {
        return new GreenSyntaxList<TDerived>(ToListNode());
    }
}
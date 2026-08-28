using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;

// The null-suppression uses in this type are covered under the following issue to
// better design this type around a null _builder
// https://github.com/dotnet/roslyn/issues/40858
internal readonly struct SeparatedGreenSyntaxListBuilder<TNode> where TNode : GreenNode
{
    private readonly GreenSyntaxListBuilder? _builder;

    public SeparatedGreenSyntaxListBuilder(int size)
        : this(new GreenSyntaxListBuilder(size))
    {
    }

    public static SeparatedGreenSyntaxListBuilder<TNode> Create()
    {
        return new SeparatedGreenSyntaxListBuilder<TNode>(8);
    }

    public SeparatedGreenSyntaxListBuilder(GreenSyntaxListBuilder builder)
    {
        _builder = builder;
    }

    public bool IsNull => _builder == null;

    public int Count => _builder!.Count;

    public GreenNode? this[int index]
    {
        get => _builder![index];
        set => _builder![index] = value;
    }

    public void Clear()
    {
        _builder!.Clear();
    }

    public void RemoveLast()
    {
        _builder!.RemoveLast();
    }

    public SeparatedGreenSyntaxListBuilder<TNode> Add(TNode node)
    {
        _builder!.Add(node);
        return this;
    }

    public void AddSeparator(GreenNode separatorToken)
    {
        _builder!.Add(separatorToken);
    }

    public void AddRange(TNode[] items, int offset, int length)
    {
        _builder!.AddRange(items, offset, length);
    }

    public void AddRange(in SeparatedGreenSyntaxList<TNode> nodes)
    {
        _builder!.AddRange(nodes.GetWithSeparators());
    }

    public void AddRange(in SeparatedGreenSyntaxList<TNode> nodes, int count)
    {
        var list = nodes.GetWithSeparators();
        _builder!.AddRange(list, Count, Math.Min(count * 2, list.Count));
    }

    public bool Any(int kind)
    {
        return _builder!.Any(kind);
    }

    public SeparatedGreenSyntaxList<TNode> ToList()
    {
        return _builder == null
            ? default
            : new SeparatedGreenSyntaxList<TNode>(new GreenSyntaxList<GreenNode>(_builder.ToListNode()));
    }

    /// <summary>
    /// WARN WARN WARN: This should be used with extreme caution - the underlying builder does
    /// not give any indication that it is from a separated syntax list but the constraints
    /// (node, token, node, token, ...) should still be maintained.
    /// </summary>
    /// <remarks>
    /// In order to avoid creating a separate pool of SeparatedSyntaxListBuilders, we expose
    /// our underlying GreenSyntaxListBuilder to SyntaxListPool.
    /// </remarks>
    public GreenSyntaxListBuilder? UnderlyingBuilder => _builder;

    public static implicit operator SeparatedGreenSyntaxList<TNode>(in SeparatedGreenSyntaxListBuilder<TNode> builder)
    {
        return builder.ToList();
    }

    public static implicit operator GreenSyntaxListBuilder?(in SeparatedGreenSyntaxListBuilder<TNode> builder)
    {
        return builder._builder;
    }
}